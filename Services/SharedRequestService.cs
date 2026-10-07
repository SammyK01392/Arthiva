using MoneySpend.Data;
using MoneySpend.Models;

namespace MoneySpend.Services;

public enum ContactLinkState { NotLinked, NotConnected, Connected, Unknown }

public sealed record SharedRequestResult(bool Success, string? Message = null);

public interface ISharedRequestService
{
    /// <summary>Is this local contact linked to an app user AND actively connected? Unknown = couldn't check (offline).</summary>
    Task<ContactLinkState> GetLinkStateAsync(int contactId);

    /// <summary>Lend: I lent them money. Borrow: I borrowed from them. Local record is created for both of you only when they accept.</summary>
    Task<SharedRequestResult> SendBorrowLendRequestAsync(
        int contactId, string type, decimal amount, DateTime date, int? accountId);

    /// <summary>After SplitService.CreateAsync (you paid): one request per connected friend with a share.</summary>
    Task<SharedRequestResult> SendSplitRequestsAsync(int splitId);

    /// <summary>accountId = PRIVATE choice, kept on this device only (null = no account entry).</summary>
    Task<SharedRequestResult> AcceptAsync(string requestId, int? accountId);
    Task<SharedRequestResult> RejectAsync(string requestId);
    Task<SharedRequestResult> CancelAsync(string requestId);

    /// <summary>Pull everything from Firebase, update local mirrors, apply newly accepted requests (idempotent).</summary>
    Task<SharedRequestResult> ReconcileAsync();

    Task<List<SharedRequestLink>> GetRequestsAsync();
}

public class SharedRequestService : ISharedRequestService
{
    private enum SendOutcome { Sent, Queued, Denied }

    private sealed class SharedRequestDto
    {
        public string? Type { get; set; }
        public string? FromUid { get; set; }
        public string? ToUid { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public string? Title { get; set; }
        public long Date { get; set; }
        public string? ParentId { get; set; }
        public string? Status { get; set; }
        public long Version { get; set; }
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
    }

    private readonly IFirebaseRtdbClient _rtdb;
    private readonly IFirebaseAuthService _auth;
    private readonly IFriendConnectionService _friends;
    private readonly IContactService _contacts;
    private readonly IBorrowLendService _borrowLend;
    private readonly INotificationService _notifications;
    private readonly IGenericRepository<SharedRequestLink> _links;
    private readonly IGenericRepository<BorrowLend> _blRepo;
    private readonly IGenericRepository<SplitExpense> _splitRepo;
    private readonly IGenericRepository<SplitShare> _shareRepo;

    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    public SharedRequestService(
        IFirebaseRtdbClient rtdb,
        IFirebaseAuthService auth,
        IFriendConnectionService friends,
        IContactService contacts,
        IBorrowLendService borrowLend,
        INotificationService notifications,
        IGenericRepository<SharedRequestLink> links,
        IGenericRepository<BorrowLend> blRepo,
        IGenericRepository<SplitExpense> splitRepo,
        IGenericRepository<SplitShare> shareRepo)
    {
        _rtdb = rtdb;
        _auth = auth;
        _friends = friends;
        _contacts = contacts;
        _borrowLend = borrowLend;
        _notifications = notifications;
        _links = links;
        _blRepo = blRepo;
        _splitRepo = splitRepo;
        _shareRepo = shareRepo;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    private static SharedRequestResult Fail(string message) => new(false, message);

    private static string NewRequestId() => Guid.NewGuid().ToString("N"); // 32 lowercase hex = rules regex

    private static string ClampTitle(string? title, string fallback)
    {
        var t = string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
        return t.Length > 80 ? t[..80] : t;
    }

    private static string Describe(FirebaseRtdbException ex) => ex.Kind switch
    {
        FirebaseErrorKind.Offline => "You're offline. Please try again when you're connected.",
        FirebaseErrorKind.Unauthorized => "Please login first.",
        FirebaseErrorKind.PermissionDenied => "This isn't allowed any more (the request may have changed).",
        FirebaseErrorKind.Server => "The server is busy. Please try again shortly.",
        _ => "Something went wrong. Please try again."
    };

    private static async Task<SharedRequestResult> RunAsync(Func<Task<SharedRequestResult>> op)
    {
        try { return await op(); }
        catch (FirebaseRtdbException ex) { return Fail(Describe(ex)); }
        catch (InvalidOperationException ex) { return Fail(ex.Message); }
    }

    private async Task<string> RequireUidAsync()
        => await _auth.GetUidAsync() is { Length: > 0 } uid
            ? uid
            : throw new InvalidOperationException("Please login first.");

    private async Task<SharedRequestLink?> GetLinkAsync(string requestId)
        => (await _links.FindAsync(l => l.SharedRequestId == requestId)).FirstOrDefault();

    private static bool IsTransient(FirebaseRtdbException ex)
        => ex.Kind is FirebaseErrorKind.Offline or FirebaseErrorKind.Server;

    // ─────────────────────────────────────────────
    //  Link state
    // ─────────────────────────────────────────────
    public async Task<ContactLinkState> GetLinkStateAsync(int contactId)
    {
        var contact = await _contacts.GetByIdAsync(contactId);
        if (contact is null || string.IsNullOrEmpty(contact.LinkedUid))
            return ContactLinkState.NotLinked;

        // Not logged in to Firebase → plain local contact, normal local flow.
        if (!await _auth.IsLoggedInAsync())
            return ContactLinkState.NotLinked;

        var otherUid = contact.LinkedUid;
        try
        {
            var conns = await _friends.GetConnectionsAsync();
            return conns.Any(c => c.OtherUid == otherUid && c.Status == ConnectionStatus.Active)
                ? ContactLinkState.Connected
                : ContactLinkState.NotConnected;
        }
        catch (FirebaseRtdbException ex) when (IsTransient(ex))
        {
            return ContactLinkState.Unknown;
        }
    }

    // ─────────────────────────────────────────────
    //  Create: Borrow / Lend
    // ─────────────────────────────────────────────
    public Task<SharedRequestResult> SendBorrowLendRequestAsync(
        int contactId, string type, decimal amount, DateTime date, int? accountId)
        => RunAsync(async () =>
        {
            if (type != SharedRequestType.Lend && type != SharedRequestType.Borrow)
                return Fail("Invalid request type.");

            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0) return Fail("Enter a valid amount.");

            var uid = await RequireUidAsync();

            var contact = await _contacts.GetByIdAsync(contactId);
            if (contact is null || string.IsNullOrEmpty(contact.LinkedUid))
                return Fail("This contact isn't linked to an app user.");

            var otherUid = contact.LinkedUid;
            var conns = await _friends.GetConnectionsAsync();
            var conn = conns.FirstOrDefault(c => c.OtherUid == otherUid && c.Status == ConnectionStatus.Active);
            if (conn is null) return Fail("You're not connected with this person any more.");

            var link = new SharedRequestLink
            {
                SharedRequestId = NewRequestId(),
                Role = SharedRequestRole.Sender,
                Type = type,
                OtherUid = otherUid,
                OtherName = conn.DisplayName,
                ContactId = contact.Id,
                Amount = amount,
                // Generic on purpose: your private notes are never sent to Firebase.
                Title = type == SharedRequestType.Lend ? "Money lent" : "Money borrowed",
                RequestDate = date,
                Status = SharedRequestStatus.Pending,
                Version = 1,
                AccountId = accountId,
                FanoutPending = true
            };
            await _links.AddAsync(link);

            return await DeliverAsync(link, uid,
                "Request sent. It will be recorded for both of you once they accept.");
        });

    // ─────────────────────────────────────────────
    //  Create: Split (one request per friend with a share)
    // ─────────────────────────────────────────────
    public Task<SharedRequestResult> SendSplitRequestsAsync(int splitId)
        => RunAsync(async () =>
        {
            if (!await _auth.IsLoggedInAsync()) return new SharedRequestResult(true);
            var uid = await RequireUidAsync();

            var split = await _splitRepo.GetByIdAsync(splitId);
            // Only the person who PAID sends requests (they are owed money).
            if (split is null || split.IsDeleted || split.PaidByContactId is not null)
                return new SharedRequestResult(true);

            var shares = (await _shareRepo.FindAsync(s => s.SplitExpenseId == splitId && s.BorrowLendId > 0)).ToList();
            if (shares.Count == 0) return new SharedRequestResult(true);

            var contactsById = (await _contacts.GetAllAsync()).ToDictionary(c => c.Id);

            // Connected set; if we can't check (offline) assume linked == connected, a denial is handled per request.
            HashSet<string>? active = null;
            try
            {
                active = (await _friends.GetConnectionsAsync())
                    .Where(c => c.Status == ConnectionStatus.Active)
                    .Select(c => c.OtherUid)
                    .ToHashSet();
            }
            catch (FirebaseRtdbException ex) when (IsTransient(ex)) { }

            var parentId = NewRequestId();
            var count = 0;

            foreach (var share in shares)
            {
                if (!contactsById.TryGetValue(share.ContactId, out var contact)) continue;
                if (string.IsNullOrEmpty(contact.LinkedUid)) continue;

                var otherUid = contact.LinkedUid;
                if (active is not null && !active.Contains(otherUid)) continue;

                var blId = share.BorrowLendId;
                var already = (await _links.FindAsync(l =>
                    l.Role == SharedRequestRole.Sender && l.BorrowLendId == blId)).Any();
                if (already) continue;

                var link = new SharedRequestLink
                {
                    SharedRequestId = NewRequestId(),
                    Role = SharedRequestRole.Sender,
                    Type = SharedRequestType.Split,
                    OtherUid = otherUid,
                    OtherName = contact.Name,
                    ContactId = contact.Id,
                    Amount = share.ShareAmount,
                    Title = ClampTitle(split.Title, "Shared bill"),
                    RequestDate = split.SplitDate,
                    ParentId = parentId,
                    Status = SharedRequestStatus.Pending,
                    Version = 1,
                    BorrowLendId = share.BorrowLendId, // the Lend SplitService already made = the sender's local record
                    FanoutPending = true
                };
                await _links.AddAsync(link);
                await TrySendAsync(link, uid);
                count++;
            }

            return new SharedRequestResult(true,
                count > 0 ? $"Shared request sent to {count} friend(s)." : null);
        });

    // ─────────────────────────────────────────────
    //  Delivery to Firebase (idempotent, retryable)
    // ─────────────────────────────────────────────
    private async Task<SharedRequestResult> DeliverAsync(SharedRequestLink link, string uid, string okMessage)
    {
        return await TrySendAsync(link, uid) switch
        {
            SendOutcome.Sent => new SharedRequestResult(true, okMessage),
            SendOutcome.Queued => new SharedRequestResult(true,
                "You're offline. The request will be sent automatically when you're back online."),
            _ => Fail("Couldn't send the request. Check that you're still connected with this person.")
        };
    }

    private async Task<SendOutcome> TrySendAsync(SharedRequestLink link, string uid)
    {
        // Step 1: create the request node.
        try
        {
            await EnsureSplitStampAsync(link);
            await CreateRemoteIfMissingAsync(link, uid);
        }
        catch (FirebaseRtdbException ex) when (IsTransient(ex))
        {
            return SendOutcome.Queued;
        }
        catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.PermissionDenied)
        {
            // Connection no longer active → the request can never be created.
            link.Status = SharedRequestStatus.Cancelled;
            link.FanoutPending = false;
            link.UpdatedAt = DateTime.UtcNow;
            await _links.UpdateAsync(link);
            return SendOutcome.Denied;
        }

        // Step 2: make it visible to both users (index entries). Retried by reconcile if this fails.
        try
        {
            await FanoutAsync(uid, link.OtherUid, link.SharedRequestId);
        }
        catch (FirebaseRtdbException ex) when (IsTransient(ex))
        {
            return SendOutcome.Queued;
        }

        link.FanoutPending = false;
        link.UpdatedAt = DateTime.UtcNow;
        await _links.UpdateAsync(link);
        return SendOutcome.Sent;
    }

    private async Task CreateRemoteIfMissingAsync(SharedRequestLink link, string uid)
    {
        var path = $"sharedRequests/{link.SharedRequestId}";
        var body = new
        {
            type = link.Type,
            fromUid = uid,
            toUid = link.OtherUid,
            amount = link.Amount,
            currency = "INR",
            title = link.Title,
            date = new DateTimeOffset(link.RequestDate).ToUnixTimeMilliseconds(),
            parentId = link.ParentId,
            status = SharedRequestStatus.Pending,
            version = 1,
            createdAt = FirebaseRtdbClient.ServerTimestamp,
            updatedAt = FirebaseRtdbClient.ServerTimestamp
        };

        try
        {
            await _rtdb.PutAsync(path, body);
        }
        catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.PermissionDenied)
        {
            // A retry of a create that already succeeded is denied by the rules (node exists).
            // That's fine as long as it's really ours.
            var existing = await _rtdb.GetAsync<SharedRequestDto>(path);
            if (existing?.FromUid != uid) throw;
        }
    }

    private Task FanoutAsync(string uid, string otherUid, string requestId)
        => _rtdb.UpdateAsync(new Dictionary<string, object?>
        {
            [$"userRequests/{uid}/{requestId}"] = FirebaseRtdbClient.ServerTimestamp,
            [$"userRequests/{otherUid}/{requestId}"] = FirebaseRtdbClient.ServerTimestamp
        });

    /// <summary>Split sender: tag the Lend SplitService already created so both sides share one id.</summary>
    private async Task EnsureSplitStampAsync(SharedRequestLink link)
    {
        if (link.Type != SharedRequestType.Split || link.BorrowLendId is not int blId) return;

        var bl = await _blRepo.GetByIdAsync(blId);
        if (bl is null || bl.SharedRequestId == link.SharedRequestId) return;

        bl.SharedRequestId = link.SharedRequestId;
        bl.UpdatedAt = DateTime.UtcNow;
        await _blRepo.UpdateAsync(bl);
    }

    private async Task FlushPendingAsync(string uid)
    {
        var pending = await _links.FindAsync(l =>
            l.Role == SharedRequestRole.Sender
            && l.FanoutPending
            && l.Status == SharedRequestStatus.Pending);

        foreach (var link in pending)
        {
            try { await TrySendAsync(link, uid); }
            catch (FirebaseRtdbException) { /* retried at next start/resume */ }
        }
    }

    // ─────────────────────────────────────────────
    //  Accept / Reject / Cancel
    // ─────────────────────────────────────────────
    public Task<SharedRequestResult> AcceptAsync(string requestId, int? accountId)
        => RunAsync(async () =>
        {
            var uid = await RequireUidAsync();
            var link = await GetLinkAsync(requestId);

            if (link is null || link.Role != SharedRequestRole.Receiver)
                return Fail("Request not found.");
            if (link.Status != SharedRequestStatus.Pending)
                return Fail($"This request is already {link.Status.ToLowerInvariant()}.");

            // Remember the PRIVATE account choice BEFORE touching the server, so that if we
            // crash right after the remote write, reconcile still applies it correctly.
            link.AccountId = link.Type == SharedRequestType.Split ? null : accountId;
            link.UpdatedAt = DateTime.UtcNow;
            await _links.UpdateAsync(link);

            var moved = await TransitionAsync(link, uid, SharedRequestStatus.Accepted);
            if (!moved.Success) return moved;

            await ApplyAcceptedAsync(link.SharedRequestId);
            return new SharedRequestResult(true);
        });

    public Task<SharedRequestResult> RejectAsync(string requestId)
        => TransitionPublicAsync(requestId, SharedRequestRole.Receiver, SharedRequestStatus.Rejected);

    public Task<SharedRequestResult> CancelAsync(string requestId)
        => TransitionPublicAsync(requestId, SharedRequestRole.Sender, SharedRequestStatus.Cancelled);

    private Task<SharedRequestResult> TransitionPublicAsync(string requestId, string requiredRole, string newStatus)
        => RunAsync(async () =>
        {
            var uid = await RequireUidAsync();
            var link = await GetLinkAsync(requestId);

            if (link is null || link.Role != requiredRole) return Fail("Request not found.");
            if (link.Status != SharedRequestStatus.Pending)
                return Fail($"This request is already {link.Status.ToLowerInvariant()}.");

            return await TransitionAsync(link, uid, newStatus);
        });

    /// <summary>
    /// Pending → newStatus. The security rules enforce who may do which transition and that
    /// version goes up by exactly one, so two devices (or the two users) racing each other
    /// can never both win.
    /// </summary>
    private async Task<SharedRequestResult> TransitionAsync(SharedRequestLink link, string uid, string newStatus)
    {
        var path = $"sharedRequests/{link.SharedRequestId}";

        var dto = await _rtdb.GetAsync<SharedRequestDto>(path);
        if (dto is null) return Fail("This request no longer exists.");

        if (dto.Status != SharedRequestStatus.Pending)
            return await ExplainLostRaceAsync(link, uid, dto);

        var newVersion = dto.Version + 1;
        try
        {
            await _rtdb.PatchAsync(path, new
            {
                status = newStatus,
                version = newVersion,
                updatedAt = FirebaseRtdbClient.ServerTimestamp
            });
        }
        catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.PermissionDenied)
        {
            // Lost the race (e.g. the sender cancelled at the same moment).
            var current = await _rtdb.GetAsync<SharedRequestDto>(path);
            if (current is null) return Fail("This request no longer exists.");
            return await ExplainLostRaceAsync(link, uid, current);
        }

        try { await FanoutAsync(uid, link.OtherUid, link.SharedRequestId); }
        catch (FirebaseRtdbException ex) when (IsTransient(ex)) { /* other side's reconcile still re-reads Pending/changed requests */ }

        link.Status = newStatus;
        link.Version = newVersion;
        link.UpdatedAt = DateTime.UtcNow;
        await _links.UpdateAsync(link);

        return new SharedRequestResult(true);
    }

    private async Task<SharedRequestResult> ExplainLostRaceAsync(SharedRequestLink link, string uid, SharedRequestDto current)
    {
        await UpsertFromDtoAsync(link, link.SharedRequestId, current, uid, link.IndexStamp, null);

        // Accepted on another device of mine → mirror it here too.
        if (current.Status == SharedRequestStatus.Accepted)
            await ApplyAcceptedAsync(link.SharedRequestId);

        var status = (current.Status ?? "changed").ToLowerInvariant();
        return Fail($"This request was already {status}.");
    }

    // ─────────────────────────────────────────────
    //  Apply an accepted request to the local books (IDEMPOTENT)
    // ─────────────────────────────────────────────
    private async Task ApplyAcceptedAsync(string requestId)
    {
        await _applyLock.WaitAsync();
        try
        {
            var link = await GetLinkAsync(requestId);
            if (link is null || link.Status != SharedRequestStatus.Accepted) return;
            if (link.BorrowLendId is not null) return; // already applied on this device

            // Safety net: a record carrying this id already exists (crash after insert, or a Drive restore).
            var existing = (await _blRepo.FindAsync(b => b.SharedRequestId == requestId)).FirstOrDefault();
            if (existing is not null)
            {
                link.BorrowLendId = existing.Id;
                link.UpdatedAt = DateTime.UtcNow;
                await _links.UpdateAsync(link);
                return;
            }

            var contactId = await ResolveContactAsync(link);

            // Sender Lend/Split → sender is the lender. Sender Borrow → sender is the borrower.
            var senderIsLender = link.Type != SharedRequestType.Borrow;
            var iAmSender = link.Role == SharedRequestRole.Sender;
            var iAmLender = iAmSender == senderIsLender;

            // Receiver of a Split share: cash-basis, nothing moves in an account until they pay it back.
            // Any device where no account was chosen also records no account entry ("Unassigned").
            int? accountId = (link.Type == SharedRequestType.Split && !iAmSender) ? null : link.AccountId;

            var record = new BorrowLend
            {
                ContactId = contactId,
                Type = iAmLender ? "Lend" : "Borrow",
                TotalAmount = link.Amount,
                GivenDate = link.RequestDate,
                Notes = link.Title,
                ReminderEnabled = false,
                SharedRequestId = requestId // set BEFORE insert → the id is stored atomically with the record
            };
            await _borrowLend.CreateAsync(record, accountId);

            link.BorrowLendId = record.Id;
            link.ContactId = contactId;
            link.UpdatedAt = DateTime.UtcNow;
            await _links.UpdateAsync(link);
        }
        finally { _applyLock.Release(); }
    }

    private async Task<int> ResolveContactAsync(SharedRequestLink link)
    {
        if (link.ContactId is int cid)
        {
            var current = await _contacts.GetByIdAsync(cid);
            if (current is not null && !current.IsDeleted) return current.Id;
        }

        var all = await _contacts.GetAllAsync();

        var byUid = all.FirstOrDefault(c => c.LinkedUid == link.OtherUid);
        if (byUid is not null) return byUid.Id;

        // Same display name, not linked to anyone yet → reuse it (same approach as SplitService.AddFriendAsync).
        var byName = all.FirstOrDefault(c =>
            string.IsNullOrEmpty(c.LinkedUid)
            && string.Equals(c.Name?.Trim(), link.OtherName, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            await _friends.LinkContactAsync(byName.Id, link.OtherUid);
            return byName.Id;
        }

        var created = await _friends.CreateLinkedContactAsync(link.OtherUid, link.OtherName);
        return created.Id;
    }

    // ─────────────────────────────────────────────
    //  Reconcile (pull)
    // ─────────────────────────────────────────────
    public async Task<SharedRequestResult> ReconcileAsync()
    {
        var uid = await _auth.GetUidAsync();
        if (string.IsNullOrEmpty(uid)) return new SharedRequestResult(true);

        await _reconcileLock.WaitAsync();
        try
        {
            return await RunAsync(async () =>
            {
                await FlushPendingAsync(uid);

                var index = await _rtdb.GetAsync<Dictionary<string, long>>($"userRequests/{uid}")
                            ?? new Dictionary<string, long>();
                if (index.Count == 0) return new SharedRequestResult(true);

                var names = (await _friends.GetConnectionsAsync())
                    .GroupBy(c => c.OtherUid)
                    .ToDictionary(g => g.Key, g => g.First().DisplayName);

                var known = (await _links.GetAllAsync()).ToDictionary(l => l.SharedRequestId);

                foreach (var entry in index)
                {
                    var id = entry.Key;
                    var stamp = entry.Value;
                    known.TryGetValue(id, out var link);

                    // Nothing can have changed for a finished + applied request whose index entry is unchanged.
                    if (link is not null
                        && link.IndexStamp == stamp
                        && link.Status != SharedRequestStatus.Pending
                        && (link.BorrowLendId is not null
                            || link.Status is SharedRequestStatus.Rejected or SharedRequestStatus.Cancelled))
                        continue;

                    var dto = await _rtdb.GetAsync<SharedRequestDto>($"sharedRequests/{id}");
                    if (dto is null) continue;

                    var updated = await UpsertFromDtoAsync(link, id, dto, uid, stamp, names);
                    if (updated is null) continue;

                    if (updated.Status == SharedRequestStatus.Accepted && updated.BorrowLendId is null)
                        await ApplyAcceptedAsync(updated.SharedRequestId);
                }

                return new SharedRequestResult(true);
            });
        }
        finally { _reconcileLock.Release(); }
    }

    private async Task<SharedRequestLink?> UpsertFromDtoAsync(
        SharedRequestLink? link, string requestId, SharedRequestDto dto, string uid, long stamp,
        IReadOnlyDictionary<string, string>? names)
    {
        string role;
        string otherUid;
        if (dto.FromUid == uid) { role = SharedRequestRole.Sender; otherUid = dto.ToUid ?? string.Empty; }
        else if (dto.ToUid == uid) { role = SharedRequestRole.Receiver; otherUid = dto.FromUid ?? string.Empty; }
        else return null;

        var status = dto.Status ?? SharedRequestStatus.Pending;

        string? knownName = null;
        if (names is not null) names.TryGetValue(otherUid, out knownName);

        if (link is null)
        {
            link = new SharedRequestLink
            {
                SharedRequestId = requestId,
                Role = role,
                Type = dto.Type ?? SharedRequestType.Lend,
                OtherUid = otherUid,
                OtherName = SharedIdentity.Clamp(knownName),
                Amount = dto.Amount,
                Title = ClampTitle(dto.Title, "Shared request"),
                RequestDate = DateTimeOffset.FromUnixTimeMilliseconds(dto.Date).LocalDateTime,
                ParentId = dto.ParentId,
                Status = status,
                Version = dto.Version,
                IndexStamp = stamp
            };
            await _links.AddAsync(link);

            if (role == SharedRequestRole.Receiver && status == SharedRequestStatus.Pending)
                await NotifyAsync("New request", link.Headline);

            return link;
        }

        var changed = link.Status != status;
        link.Status = status;
        link.Version = dto.Version;
        link.IndexStamp = stamp;
        if (!string.IsNullOrEmpty(knownName)) link.OtherName = knownName;
        link.UpdatedAt = DateTime.UtcNow;
        await _links.UpdateAsync(link);

        if (changed && link.Role == SharedRequestRole.Sender
            && status is SharedRequestStatus.Accepted or SharedRequestStatus.Rejected)
        {
            await NotifyAsync(
                status == SharedRequestStatus.Accepted ? "Request accepted" : "Request rejected",
                $"{link.OtherName} {status.ToLowerInvariant()} your request of ₹{link.Amount:N2}.");
        }

        return link;
    }

    private async Task NotifyAsync(string title, string body)
    {
        try
        {
            await _notifications.CreateAsync(new Notification
            {
                Title = title,
                Body = body,
                Type = "SharedRequest",
                ReminderDate = DateTime.Now,
                IsScheduled = false,
                ActionRoute = "SharedRequestListPage"
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "SharedRequestService.Notify");
        }
    }

    public async Task<List<SharedRequestLink>> GetRequestsAsync()
    {
        var all = await _links.GetAllAsync();
        return all.OrderByDescending(l => l.CreatedAt).ToList();
    }
}
