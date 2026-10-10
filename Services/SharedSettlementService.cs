using MoneySpend.Data;
using MoneySpend.Models;

namespace MoneySpend.Services;

public interface ISharedSettlementService
{
    /// <summary>I paid / I received `amount` on this shared request. The other person must confirm.</summary>
    Task<SharedRequestResult> ProposeAsync(string requestId, decimal amount, DateTime date, int? accountId);

    /// <summary>Same, starting from the local BorrowLend record (used by Record transaction and Split Settle / Pay).</summary>
    Task<SharedRequestResult> ProposeForBorrowLendAsync(int borrowLendId, decimal amount, DateTime date, int? accountId);

    /// <summary>accountId = PRIVATE choice kept on this device only (null = no account entry).</summary>
    Task<SharedRequestResult> ConfirmAsync(string settlementId, int? accountId);
    Task<SharedRequestResult> RejectAsync(string settlementId);
    Task<SharedRequestResult> CancelAsync(string settlementId);

    Task<List<SharedSettlementLink>> GetAllAsync();

    /// <summary>Pull this request's settlements, write newly confirmed ones to the books, flip the request to Settled when fully paid.</summary>
    Task SyncRequestAsync(SharedRequestLink request, string uid);

    /// <summary>Offline outbox: queued creates, index repairs, queued confirm / reject / cancel.</summary>
    Task FlushPendingAsync(string uid);
}

public class SharedSettlementService : ISharedSettlementService
{
    private enum SendOutcome { Sent, Queued, Denied }

    private sealed class SettlementDto
    {
        public decimal Amount { get; set; }
        public string? ProposedBy { get; set; }
        public string? Status { get; set; }
        public long Version { get; set; }
        public long Date { get; set; }
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
    }

    private sealed class RequestHeadDto
    {
        public string? Status { get; set; }
        public long Version { get; set; }
    }

    private const string QueuedOfflineMessage =
        "You're offline. Your response will be sent automatically when you're back online.";

    private readonly IFirebaseRtdbClient _rtdb;
    private readonly IFirebaseAuthService _auth;
    private readonly IBorrowLendService _borrowLend;
    private readonly INotificationService _notifications;
    private readonly IGenericRepository<SharedRequestLink> _requestLinks;
    private readonly IGenericRepository<SharedSettlementLink> _links;
    private readonly IGenericRepository<BorrowLendTransaction> _txnRepo;
    private readonly IGenericRepository<BorrowLend> _blRepo;

    private readonly SemaphoreSlim _applyLock = new(1, 1);

    public SharedSettlementService(
        IFirebaseRtdbClient rtdb,
        IFirebaseAuthService auth,
        IBorrowLendService borrowLend,
        INotificationService notifications,
        IGenericRepository<SharedRequestLink> requestLinks,
        IGenericRepository<SharedSettlementLink> links,
        IGenericRepository<BorrowLendTransaction> txnRepo,
        IGenericRepository<BorrowLend> blRepo)
    {
        _rtdb = rtdb;
        _auth = auth;
        _borrowLend = borrowLend;
        _notifications = notifications;
        _requestLinks = requestLinks;
        _links = links;
        _txnRepo = txnRepo;
        _blRepo = blRepo;
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    private static SharedRequestResult Fail(string message) => new(false, message);

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static bool IsTransient(FirebaseRtdbException ex)
        => ex.Kind is FirebaseErrorKind.Offline or FirebaseErrorKind.Server;

    private static string Describe(FirebaseRtdbException ex) => ex.Kind switch
    {
        FirebaseErrorKind.Offline => "You're offline. Please try again when you're connected.",
        FirebaseErrorKind.Unauthorized => "Please login first.",
        FirebaseErrorKind.PermissionDenied => "This isn't allowed any more (the payment or request may have changed).",
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

    private async Task<SharedSettlementLink?> GetLinkAsync(string settlementId)
        => (await _links.FindAsync(l => l.SharedSettlementId == settlementId)).FirstOrDefault();

    private async Task<SharedRequestLink?> GetRequestAsync(string requestId)
        => (await _requestLinks.FindAsync(l => l.SharedRequestId == requestId)).FirstOrDefault();

    private Task FanoutAsync(string uid, string otherUid, string requestId)
        => _rtdb.UpdateAsync(new Dictionary<string, object?>
        {
            // The streams listen to these index entries, so bumping them tells BOTH users (all their devices) to pull.
            [$"userRequests/{uid}/{requestId}"] = FirebaseRtdbClient.ServerTimestamp,
            [$"userRequests/{otherUid}/{requestId}"] = FirebaseRtdbClient.ServerTimestamp
        });

    public async Task<List<SharedSettlementLink>> GetAllAsync()
    {
        var all = await _links.GetAllAsync();
        return all.OrderByDescending(l => l.CreatedAt).ToList();
    }

    // ─────────────────────────────────────────────
    //  Propose
    // ─────────────────────────────────────────────
    public Task<SharedRequestResult> ProposeForBorrowLendAsync(
        int borrowLendId, decimal amount, DateTime date, int? accountId)
        => RunAsync(async () =>
        {
            var bl = await _blRepo.GetByIdAsync(borrowLendId);
            if (bl is null || string.IsNullOrEmpty(bl.SharedRequestId))
                return Fail("This record isn't shared.");

            return await ProposeAsync(bl.SharedRequestId, amount, date, accountId);
        });

    public Task<SharedRequestResult> ProposeAsync(string requestId, decimal amount, DateTime date, int? accountId)
        => RunAsync(async () =>
        {
            amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0) return Fail("Enter a valid amount.");

            var uid = await RequireUidAsync();
            var request = await GetRequestAsync(requestId);
            if (request is null) return Fail("Request not found.");

            if (request.Status == SharedRequestStatus.Pending)
                return Fail($"{request.OtherName} hasn't accepted this request yet. Record the payment after they accept, or withdraw the request.");
            if (request.Status != SharedRequestStatus.Accepted || request.BorrowLendId is null)
                return Fail($"This request is {request.Status.ToLowerInvariant()}; payments can't be added.");

            // Client-side total check (the rules can't sum): confirmed + still-pending payments must stay within the amount.
            var committed = (await _links.FindAsync(l =>
                    l.SharedRequestId == requestId
                    && (l.Status == SettlementStatus.Confirmed || l.Status == SettlementStatus.Pending)))
                .Sum(l => l.Amount);

            var available = request.Amount - committed;
            if (amount > available)
            {
                return Fail(available <= 0
                    ? "Nothing left to settle, or earlier payments are still waiting for confirmation."
                    : $"Amount can't exceed ₹{available:N2} (some payments are still waiting for confirmation).");
            }

            var link = new SharedSettlementLink
            {
                SharedSettlementId = NewId(),
                SharedRequestId = requestId,
                ProposedByMe = true,
                IAmPayer = request.IAmPayer,
                OtherName = request.OtherName,
                Amount = amount,
                SettlementDate = date,
                Status = SettlementStatus.Pending,
                Version = 1,
                AccountId = accountId,
                FanoutPending = true
            };
            await _links.AddAsync(link);

            return await TrySendAsync(link, request, uid) switch
            {
                SendOutcome.Sent => new SharedRequestResult(true,
                    $"Sent to {request.OtherName} for confirmation. It is recorded once they confirm."),
                SendOutcome.Queued => new SharedRequestResult(true,
                    "You're offline. It will be sent automatically when you're back online."),
                _ => Fail("Couldn't send the payment. The request may have changed.")
            };
        });

    private async Task<SendOutcome> TrySendAsync(SharedSettlementLink link, SharedRequestLink request, string uid)
    {
        var path = $"settlements/{link.SharedRequestId}/{link.SharedSettlementId}";

        // Step 1: create the settlement node.
        try
        {
            await _rtdb.PutAsync(path, new
            {
                amount = link.Amount,
                proposedBy = uid,
                status = SettlementStatus.Pending,
                version = 1,
                date = new DateTimeOffset(link.SettlementDate).ToUnixTimeMilliseconds(),
                createdAt = FirebaseRtdbClient.ServerTimestamp,
                updatedAt = FirebaseRtdbClient.ServerTimestamp
            });
        }
        catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.PermissionDenied)
        {
            // A retry of a create that already succeeded is denied (node exists). Fine if it's ours.
            try
            {
                var existing = await _rtdb.GetAsync<SettlementDto>(path);
                if (existing?.ProposedBy != uid) throw;
            }
            catch (FirebaseRtdbException inner) when (IsTransient(inner))
            {
                return SendOutcome.Queued;
            }
            catch (FirebaseRtdbException)
            {
                // Genuinely not allowed (request no longer Accepted, …).
                var denied = await GetLinkAsync(link.SharedSettlementId) ?? link;
                denied.Status = SettlementStatus.Cancelled;
                denied.FanoutPending = false;
                denied.UpdatedAt = DateTime.UtcNow;
                await _links.UpdateAsync(denied);
                return SendOutcome.Denied;
            }
        }
        catch (FirebaseRtdbException ex) when (IsTransient(ex))
        {
            return SendOutcome.Queued;
        }

        // Step 2: tell both users' streams. Retried by the flush if this fails.
        try
        {
            await FanoutAsync(uid, request.OtherUid, request.SharedRequestId);
        }
        catch (FirebaseRtdbException ex) when (IsTransient(ex))
        {
            return SendOutcome.Queued;
        }

        var fresh = await GetLinkAsync(link.SharedSettlementId) ?? link;
        fresh.FanoutPending = false;
        fresh.UpdatedAt = DateTime.UtcNow;
        await _links.UpdateAsync(fresh);
        return SendOutcome.Sent;
    }

    // ─────────────────────────────────────────────
    //  Confirm / Reject / Cancel
    // ─────────────────────────────────────────────
    public Task<SharedRequestResult> ConfirmAsync(string settlementId, int? accountId)
        => RunAsync(async () =>
        {
            var uid = await RequireUidAsync();
            var link = await GetLinkAsync(settlementId);

            if (link is null || link.ProposedByMe) return Fail("Payment not found.");
            if (link.Status != SettlementStatus.Pending)
                return Fail($"This payment is already {link.Status.ToLowerInvariant()}.");
            if (link.PendingAction is not null)
                return Fail("Your response is already waiting to be sent.");

            var request = await GetRequestAsync(link.SharedRequestId);
            if (request is null) return Fail("Request not found.");

            // Never confirm more than what is still owed.
            var confirmed = (await _links.FindAsync(l =>
                    l.SharedRequestId == link.SharedRequestId && l.Status == SettlementStatus.Confirmed))
                .Sum(l => l.Amount);
            if (link.Amount > request.Amount - confirmed)
                return Fail("This payment is more than what is still owed on the request, so it can't be confirmed.");

            // Private account choice first, so an offline / interrupted confirm still applies it correctly.
            link.AccountId = accountId;
            link.UpdatedAt = DateTime.UtcNow;
            await _links.UpdateAsync(link);

            try
            {
                var moved = await TransitionAsync(link, request, uid, SettlementStatus.Confirmed);
                if (!moved.Success) return moved;
            }
            catch (FirebaseRtdbException ex) when (IsTransient(ex))
            {
                return await QueueAsync(link, SettlementStatus.Confirmed, QueuedOfflineMessage);
            }

            await ApplyConfirmedAsync(link.SharedSettlementId);
            await TryMarkSettledAsync(request, uid, null);
            return new SharedRequestResult(true);
        });

    public Task<SharedRequestResult> RejectAsync(string settlementId)
        => TransitionPublicAsync(settlementId, proposerRequired: false, SettlementStatus.Rejected);

    public Task<SharedRequestResult> CancelAsync(string settlementId)
        => TransitionPublicAsync(settlementId, proposerRequired: true, SettlementStatus.Cancelled);

    private Task<SharedRequestResult> TransitionPublicAsync(string settlementId, bool proposerRequired, string newStatus)
        => RunAsync(async () =>
        {
            var uid = await RequireUidAsync();
            var link = await GetLinkAsync(settlementId);

            if (link is null || link.ProposedByMe != proposerRequired) return Fail("Payment not found.");
            if (link.Status != SettlementStatus.Pending)
                return Fail($"This payment is already {link.Status.ToLowerInvariant()}.");
            if (link.PendingAction is not null)
                return Fail("Your response is already waiting to be sent.");

            var request = await GetRequestAsync(link.SharedRequestId);
            if (request is null) return Fail("Request not found.");

            // The payment itself hasn't reached the server yet: queue the withdrawal behind it.
            if (link.ProposedByMe && link.FanoutPending)
                return await QueueAsync(link, newStatus, "Will be withdrawn as soon as you're back online.");

            try
            {
                return await TransitionAsync(link, request, uid, newStatus);
            }
            catch (FirebaseRtdbException ex) when (IsTransient(ex))
            {
                return await QueueAsync(link, newStatus, QueuedOfflineMessage);
            }
        });

    private async Task<SharedRequestResult> TransitionAsync(
        SharedSettlementLink link, SharedRequestLink request, string uid, string newStatus)
    {
        var path = $"settlements/{link.SharedRequestId}/{link.SharedSettlementId}";

        var dto = await _rtdb.GetAsync<SettlementDto>(path);
        if (dto is null) return Fail("This payment no longer exists.");

        if (dto.Status != SettlementStatus.Pending)
            return await ExplainLostRaceAsync(request, link.SharedSettlementId, dto, uid);

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
            var current = await _rtdb.GetAsync<SettlementDto>(path);
            if (current is null) return Fail("This payment no longer exists.");
            return await ExplainLostRaceAsync(request, link.SharedSettlementId, current, uid);
        }

        var fanoutFailed = false;
        try { await FanoutAsync(uid, request.OtherUid, request.SharedRequestId); }
        catch (FirebaseRtdbException ex) when (IsTransient(ex)) { fanoutFailed = true; }

        var fresh = await GetLinkAsync(link.SharedSettlementId) ?? link;
        fresh.Status = newStatus;
        fresh.Version = newVersion;
        if (fanoutFailed) fresh.FanoutPending = true;
        fresh.UpdatedAt = DateTime.UtcNow;
        await _links.UpdateAsync(fresh);

        link.Status = newStatus;
        link.Version = newVersion;
        return new SharedRequestResult(true);
    }

    private async Task<SharedRequestResult> ExplainLostRaceAsync(
        SharedRequestLink request, string settlementId, SettlementDto current, string uid)
    {
        await UpsertAsync(request, settlementId, current, uid);

        if (current.Status == SettlementStatus.Confirmed)
            await ApplyConfirmedAsync(settlementId);

        return Fail($"This payment was already {(current.Status ?? "changed").ToLowerInvariant()}.");
    }

    private async Task<SharedRequestResult> QueueAsync(SharedSettlementLink link, string action, string message)
    {
        var fresh = await GetLinkAsync(link.SharedSettlementId) ?? link;
        fresh.PendingAction = action;
        fresh.UpdatedAt = DateTime.UtcNow;
        await _links.UpdateAsync(fresh);
        return new SharedRequestResult(true, message);
    }

    // ─────────────────────────────────────────────
    //  Apply a CONFIRMED settlement to the books (IDEMPOTENT)
    // ─────────────────────────────────────────────
    private async Task ApplyConfirmedAsync(string settlementId)
    {
        await _applyLock.WaitAsync();
        try
        {
            var link = await GetLinkAsync(settlementId);
            if (link is null || link.Status != SettlementStatus.Confirmed || link.Applied) return;

            var request = await GetRequestAsync(link.SharedRequestId);
            if (request?.BorrowLendId is not int blId) return; // request not applied on this device yet → retried later

            var bl = await _blRepo.GetByIdAsync(blId);
            if (bl is null) return;

            // Safety net: a movement carrying this id already exists (crash after insert, Drive restore…).
            var existing = (await _txnRepo.FindAsync(t => t.SharedSettlementId == settlementId)).FirstOrDefault();
            if (existing is not null)
            {
                link.Applied = true;
                link.BorrowLendTransactionId = existing.Id;
                link.UpdatedAt = DateTime.UtcNow;
                await _links.UpdateAsync(link);
                return;
            }

            if (link.Amount > bl.PendingAmount)
            {
                await MarkConflictAsync(link, "A confirmed payment couldn't be recorded because it is more than the remaining balance.");
                return;
            }

            var iAmLender = !link.IAmPayer;
            var full = link.Amount >= bl.PendingAmount;
            var type = iAmLender
                ? (full ? "Receive" : "PartialReturn")
                : (full ? "Return" : "PartialReturn");

            var txn = new BorrowLendTransaction
            {
                BorrowLendId = bl.Id,
                Amount = link.Amount,
                Type = type,
                TransactionDate = link.SettlementDate,
                Notes = "Shared settlement",
                SharedSettlementId = settlementId // stored atomically with the movement
            };

            var result = await _borrowLend.RecordTransactionAsync(txn, link.AccountId);
            if (!result.Success)
            {
                await MarkConflictAsync(link, result.ErrorMessage ?? "A confirmed payment couldn't be recorded.");
                return;
            }

            link.Applied = true;
            link.BorrowLendTransactionId = txn.Id;
            link.UpdatedAt = DateTime.UtcNow;
            await _links.UpdateAsync(link);
        }
        finally { _applyLock.Release(); }
    }

    private async Task MarkConflictAsync(SharedSettlementLink link, string message)
    {
        link.Applied = true;   // don't retry forever
        link.Conflict = true;
        link.UpdatedAt = DateTime.UtcNow;
        await _links.UpdateAsync(link);
        await NotifyAsync("Payment needs attention", message);
    }

    // ─────────────────────────────────────────────
    //  Pull (called by SharedRequestService.Reconcile)
    // ─────────────────────────────────────────────
    public async Task SyncRequestAsync(SharedRequestLink request, string uid)
    {
        var map = await _rtdb.GetAsync<Dictionary<string, SettlementDto>>($"settlements/{request.SharedRequestId}");
        if (map is null || map.Count == 0) return;

        foreach (var entry in map)
        {
            var link = await UpsertAsync(request, entry.Key, entry.Value, uid);
            if (link is not null && link.Status == SettlementStatus.Confirmed && !link.Applied)
                await ApplyConfirmedAsync(link.SharedSettlementId);
        }

        await TryMarkSettledAsync(request, uid, map);
    }

    private async Task<SharedSettlementLink?> UpsertAsync(
        SharedRequestLink request, string id, SettlementDto dto, string uid)
    {
        var status = dto.Status ?? SettlementStatus.Pending;
        var existing = await GetLinkAsync(id);

        if (existing is null)
        {
            var created = new SharedSettlementLink
            {
                SharedSettlementId = id,
                SharedRequestId = request.SharedRequestId,
                ProposedByMe = dto.ProposedBy == uid,
                IAmPayer = request.IAmPayer,
                OtherName = request.OtherName,
                Amount = dto.Amount,
                SettlementDate = DateTimeOffset.FromUnixTimeMilliseconds(dto.Date).LocalDateTime,
                Status = status,
                Version = dto.Version
            };
            await _links.AddAsync(created);

            if (!created.ProposedByMe && status == SettlementStatus.Pending)
                await NotifyAsync("Payment to confirm", created.Headline);

            return created;
        }

        var changed = existing.Status != status;
        existing.Status = status;
        existing.Version = dto.Version;
        existing.UpdatedAt = DateTime.UtcNow;
        await _links.UpdateAsync(existing);

        if (changed && existing.ProposedByMe
            && status is SettlementStatus.Confirmed or SettlementStatus.Rejected)
        {
            await NotifyAsync(
                status == SettlementStatus.Confirmed ? "Payment confirmed" : "Payment rejected",
                $"{existing.OtherName} {status.ToLowerInvariant()} your payment of ₹{existing.Amount:N2}.");
        }

        return existing;
    }

    /// <summary>
    /// Only the creditor may flip a request to Settled (the rules say so). Done here whenever the
    /// confirmed payments add up to the full amount; the debtor sees it through the stream.
    /// </summary>
    private async Task TryMarkSettledAsync(
        SharedRequestLink request, string uid, Dictionary<string, SettlementDto>? remote)
    {
        try
        {
            var fresh = await GetRequestAsync(request.SharedRequestId);
            if (fresh is null || fresh.Status != SharedRequestStatus.Accepted) return;
            if (fresh.IAmPayer) return; // only the one who receives the money

            decimal confirmedTotal;
            if (remote is not null)
            {
                confirmedTotal = remote.Values
                    .Where(d => d.Status == SettlementStatus.Confirmed)
                    .Sum(d => d.Amount);
            }
            else
            {
                confirmedTotal = (await _links.FindAsync(l =>
                        l.SharedRequestId == fresh.SharedRequestId && l.Status == SettlementStatus.Confirmed))
                    .Sum(l => l.Amount);
            }

            if (confirmedTotal < fresh.Amount) return;

            var path = $"sharedRequests/{fresh.SharedRequestId}";
            var head = await _rtdb.GetAsync<RequestHeadDto>(path);
            if (head is null || head.Status != SharedRequestStatus.Accepted) return;

            var newVersion = head.Version + 1;
            await _rtdb.PatchAsync(path, new
            {
                status = SharedRequestStatus.Settled,
                version = newVersion,
                updatedAt = FirebaseRtdbClient.ServerTimestamp
            });

            try { await FanoutAsync(uid, fresh.OtherUid, fresh.SharedRequestId); }
            catch (FirebaseRtdbException ex) when (IsTransient(ex)) { }

            var latest = await GetRequestAsync(fresh.SharedRequestId) ?? fresh;
            latest.Status = SharedRequestStatus.Settled;
            latest.Version = newVersion;
            latest.UpdatedAt = DateTime.UtcNow;
            await _requestLinks.UpdateAsync(latest);
        }
        catch (FirebaseRtdbException)
        {
            // Best effort: the next reconcile tries again. The books are already correct either way.
        }
    }

    // ─────────────────────────────────────────────
    //  Offline outbox
    // ─────────────────────────────────────────────
    public async Task FlushPendingAsync(string uid)
    {
        // 1. Proposals that never reached Firebase.
        var creates = await _links.FindAsync(l =>
            l.ProposedByMe && l.FanoutPending && l.Status == SettlementStatus.Pending);

        foreach (var link in creates)
        {
            var request = await GetRequestAsync(link.SharedRequestId);
            if (request is null) continue;

            try { await TrySendAsync(link, request, uid); }
            catch (FirebaseRtdbException) { /* retried at next trigger */ }
        }

        // 2. Index repair after a status change whose fan-out failed.
        var repairs = await _links.FindAsync(l =>
            l.FanoutPending && !(l.ProposedByMe && l.Status == SettlementStatus.Pending));

        foreach (var link in repairs)
        {
            var request = await GetRequestAsync(link.SharedRequestId);
            if (request is null) continue;

            try
            {
                await FanoutAsync(uid, request.OtherUid, request.SharedRequestId);

                var fresh = await GetLinkAsync(link.SharedSettlementId) ?? link;
                fresh.FanoutPending = false;
                fresh.UpdatedAt = DateTime.UtcNow;
                await _links.UpdateAsync(fresh);
            }
            catch (FirebaseRtdbException) { /* retried at next trigger */ }
        }

        // 3. Queued confirm / reject / cancel (re-query: step 1 may just have created what a Cancel depends on).
        var actions = await _links.FindAsync(l =>
            l.PendingAction != null
            && !(l.ProposedByMe && l.FanoutPending && l.Status == SettlementStatus.Pending));

        foreach (var link in actions)
        {
            try { await ExecutePendingAsync(link, uid); }
            catch (FirebaseRtdbException) { /* retried at next trigger */ }
        }
    }

    private async Task ExecutePendingAsync(SharedSettlementLink link, string uid)
    {
        var action = link.PendingAction;
        if (action is null) return;

        var request = await GetRequestAsync(link.SharedRequestId);
        if (request is null) return;

        SharedRequestResult result;
        try
        {
            result = await TransitionAsync(link, request, uid, action);
        }
        catch (FirebaseRtdbException ex) when (IsTransient(ex))
        {
            return; // stays queued
        }

        var fresh = await GetLinkAsync(link.SharedSettlementId) ?? link;
        fresh.PendingAction = null;
        fresh.UpdatedAt = DateTime.UtcNow;
        await _links.UpdateAsync(fresh);

        var reached = fresh.Status == action;
        if (reached && action == SettlementStatus.Confirmed)
        {
            await ApplyConfirmedAsync(fresh.SharedSettlementId);
            await TryMarkSettledAsync(request, uid, null);
        }
        else if (!reached)
        {
            await NotifyAsync("Couldn't send your response", result.Message ?? "The payment changed in the meantime.");
        }
    }

    private async Task NotifyAsync(string title, string body)
    {
        try
        {
            await _notifications.CreateAsync(new Notification
            {
                Title = title,
                Body = body,
                Type = "SharedSettlement",
                ReminderDate = DateTime.Now,
                IsScheduled = false,
                ActionRoute = "SharedRequestListPage"
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "SharedSettlementService.Notify");
        }
    }
}
