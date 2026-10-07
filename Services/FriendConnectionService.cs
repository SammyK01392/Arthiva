using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MoneySpend.Data;
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public enum ConnectionStatus { Outgoing, Incoming, Active }

public sealed record FriendInvite(string Uid, string DisplayName, string Code);

public sealed record ConnectionInfo(
    string OtherUid,
    string DisplayName,
    ConnectionStatus Status,
    DateTime CreatedAtUtc,
    int? LinkedContactId);

public static class FriendCode
{
    // No I / O / 0 / 1 – matches the regex in database.rules.json.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly Regex Valid = new("^[A-HJ-NP-Z2-9]{8}$", RegexOptions.Compiled);

    public static string Generate()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    public static string Normalize(string? input)
        => (input ?? string.Empty).Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");

    public static bool IsValid(string normalized) => Valid.IsMatch(normalized);

    /// <summary>ABCD-EFGH for display / sharing.</summary>
    public static string Format(string code) => code.Length == 8 ? $"{code[..4]}-{code[4..]}" : code;
}

public interface IFriendConnectionService
{
    Task<string> GetOrCreateMyFriendCodeAsync();
    Task<string> RotateFriendCodeAsync();

    /// <summary>null = unknown/invalid code.</summary>
    Task<FriendInvite?> LookupCodeAsync(string code);

    Task SendConnectionRequestAsync(FriendInvite invite);
    Task AcceptConnectionAsync(string otherUid);

    /// <summary>Decline an incoming request, cancel an outgoing one, or disconnect.</summary>
    Task RemoveConnectionAsync(string otherUid);

    Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync();

    /// <summary>Map an existing local Contact to a Firebase uid (one contact per uid).</summary>
    Task LinkContactAsync(int contactId, string otherUid);

    /// <summary>Create a new local Contact for a connection and link it.</summary>
    Task<Contact> CreateLinkedContactAsync(string otherUid, string displayName);
}

public class FriendConnectionService : IFriendConnectionService
{
    private readonly IFirebaseRtdbClient _rtdb;
    private readonly IFirebaseAuthService _auth;
    private readonly MoneySpendDatabase _db;
    private readonly IUserProfileService _profile;

    public FriendConnectionService(IFirebaseRtdbClient rtdb, IFirebaseAuthService auth,
        MoneySpendDatabase db, IUserProfileService profile)
    {
        _rtdb = rtdb;
        _auth = auth;
        _db = db;
        _profile = profile;
    }

    private sealed class ConnectionDto
    {
        public string? Status { get; set; }
        public string? DisplayName { get; set; }
        public long CreatedAt { get; set; }
    }

    private sealed class CodeDto
    {
        public string? Uid { get; set; }
        public string? DisplayName { get; set; }
    }

    private async Task<string> RequireUidAsync()
        => await _auth.GetUidAsync() is { Length: > 0 } uid
            ? uid
            : throw new InvalidOperationException("Please login first.");

    private static ConnectionStatus? ParseStatus(string? s) => s switch
    {
        "outgoing" => ConnectionStatus.Outgoing,
        "incoming" => ConnectionStatus.Incoming,
        "active" => ConnectionStatus.Active,
        _ => null
    };

    // ───────────── Friend code ─────────────

    public async Task<string> GetOrCreateMyFriendCodeAsync()
    {
        var uid = await RequireUidAsync();

        var existing = await _rtdb.GetAsync<string>($"users/{uid}/friendCode");
        if (!string.IsNullOrEmpty(existing))
        {
            var owner = await _rtdb.GetAsync<CodeDto>($"friendCodes/{existing}");
            if (owner?.Uid == uid) return existing;
        }

        return await CreateCodeAsync(uid);
    }

    public async Task<string> RotateFriendCodeAsync()
    {
        var uid = await RequireUidAsync();

        var old = await _rtdb.GetAsync<string>($"users/{uid}/friendCode");
        if (!string.IsNullOrEmpty(old))
        {
            try { await _rtdb.DeleteAsync($"friendCodes/{old}"); }
            catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.PermissionDenied) { }
        }

        return await CreateCodeAsync(uid);
    }

    private async Task<string> CreateCodeAsync(string uid)
    {
        var name = await SharedIdentity.DisplayNameAsync(_profile);

        for (var i = 0; i < 8; i++)
        {
            var code = FriendCode.Generate();
            // Conditional create: if someone already owns this code we get 412, not an overwrite.
            if (await _rtdb.PutIfAbsentAsync($"friendCodes/{code}", new { uid, displayName = name }))
            {
                await _rtdb.PutAsync($"users/{uid}/friendCode", code);
                return code;
            }
        }
        throw new InvalidOperationException("Could not create a friend code. Please try again.");
    }

    public async Task<FriendInvite?> LookupCodeAsync(string code)
    {
        var normalized = FriendCode.Normalize(code);
        if (!FriendCode.IsValid(normalized)) return null;

        var dto = await _rtdb.GetAsync<CodeDto>($"friendCodes/{normalized}");
        if (dto?.Uid is not { Length: > 0 } uid) return null;

        return new FriendInvite(uid, SharedIdentity.Clamp(dto.DisplayName), normalized);
    }

    // ───────────── Connection handshake ─────────────
    // Each side stores its own view under /userConnections/{me}/{other}:
    //   outgoing = I sent the request, incoming = they sent it, active = both agreed.
    // Rules make it unforgeable: "active" on a side can only be set by that side's
    // owner (or by the other party accepting an 'outgoing' request).

    public async Task SendConnectionRequestAsync(FriendInvite invite)
    {
        var me = await RequireUidAsync();
        if (invite.Uid == me)
            throw new InvalidOperationException("This is your own friend code.");

        var existing = await _rtdb.GetAsync<ConnectionDto>($"userConnections/{me}/{invite.Uid}");
        if (existing is not null)
        {
            switch (ParseStatus(existing.Status))
            {
                case ConnectionStatus.Incoming:   // they already asked me – just accept
                    await AcceptConnectionAsync(invite.Uid);
                    return;
                case ConnectionStatus.Active:
                    throw new InvalidOperationException("You are already connected.");
                default:
                    throw new InvalidOperationException("Request already sent.");
            }
        }

        var myName = await SharedIdentity.DisplayNameAsync(_profile);
        try
        {
            await _rtdb.UpdateAsync(new Dictionary<string, object?>
            {
                [$"userConnections/{me}/{invite.Uid}"] = new
                {
                    status = "outgoing",
                    displayName = SharedIdentity.Clamp(invite.DisplayName),
                    createdAt = FirebaseRtdbClient.ServerTimestamp
                },
                [$"userConnections/{invite.Uid}/{me}"] = new
                {
                    status = "incoming",
                    displayName = myName,
                    createdAt = FirebaseRtdbClient.ServerTimestamp
                }
            });
        }
        catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.PermissionDenied)
        {
            // Other side already has an entry for me (e.g. a stale one) → the atomic write was rejected.
            throw new InvalidOperationException("Couldn't send the request. Ask them to remove the old connection first.");
        }
    }

    public async Task AcceptConnectionAsync(string otherUid)
    {
        var me = await RequireUidAsync();

        var mine = await _rtdb.GetAsync<ConnectionDto>($"userConnections/{me}/{otherUid}")
            ?? throw new InvalidOperationException("This request is no longer available.");

        switch (ParseStatus(mine.Status))
        {
            case ConnectionStatus.Active: return;
            case ConnectionStatus.Outgoing:
                throw new InvalidOperationException("Waiting for the other person to accept.");
        }

        try
        {
            await _rtdb.UpdateAsync(new Dictionary<string, object?>
            {
                [$"userConnections/{me}/{otherUid}/status"] = "active",
                [$"userConnections/{otherUid}/{me}/status"] = "active"
            });
        }
        catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.PermissionDenied)
        {
            // The sender cancelled in the meantime.
            await SafeDeleteAsync($"userConnections/{me}/{otherUid}");
            throw new InvalidOperationException("This request was cancelled by the sender.");
        }
    }

    public async Task RemoveConnectionAsync(string otherUid)
    {
        var me = await RequireUidAsync();

        await _rtdb.DeleteAsync($"userConnections/{me}/{otherUid}");
        // Best effort: also clear their view of me (rules allow it; fails harmlessly if already gone).
        await SafeDeleteAsync($"userConnections/{otherUid}/{me}");

        // The local mapping stays; whether a contact is "connected" is decided by
        // the live connection record, never by Contact.LinkedUid alone.
    }

    private async Task SafeDeleteAsync(string path)
    {
        try { await _rtdb.DeleteAsync(path); }
        catch (FirebaseRtdbException ex) when (ex.Kind is FirebaseErrorKind.PermissionDenied) { }
    }

    public async Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync()
    {
        var me = await RequireUidAsync();
        var map = await _rtdb.GetAsync<Dictionary<string, ConnectionDto>>($"userConnections/{me}");
        if (map is null || map.Count == 0) return Array.Empty<ConnectionInfo>();

        var linked = await _db.Database.Table<Contact>()
            .Where(c => c.LinkedUid != null && !c.IsDeleted)
            .ToListAsync();
        var byUid = linked.GroupBy(c => c.LinkedUid!).ToDictionary(g => g.Key, g => g.First().Id);

        var list = new List<ConnectionInfo>();
        foreach (var (otherUid, dto) in map)
        {
            if (ParseStatus(dto.Status) is not { } status) continue;
            list.Add(new ConnectionInfo(
                otherUid,
                SharedIdentity.Clamp(dto.DisplayName),
                status,
                DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime,
                byUid.TryGetValue(otherUid, out var id) ? id : null));
        }
        return list.OrderByDescending(c => c.CreatedAtUtc).ToList();
    }

    // ───────────── Contact ↔ uid mapping (local only) ─────────────

    public async Task LinkContactAsync(int contactId, string otherUid)
    {
        var db = _db.Database;

        var contact = await db.Table<Contact>().Where(c => c.Id == contactId).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Contact not found.");

        // One contact per uid: unlink any other contact that points at the same user.
        var others = await db.Table<Contact>().Where(c => c.LinkedUid == otherUid).ToListAsync();
        foreach (var o in others.Where(o => o.Id != contactId))
        {
            o.LinkedUid = null;
            o.UpdatedAt = DateTime.UtcNow;
            await db.UpdateAsync(o);
            DataChangeNotifier.Publish<Contact>();
        }

        contact.LinkedUid = otherUid;
        contact.UpdatedAt = DateTime.UtcNow;
        await db.UpdateAsync(contact);
        DataChangeNotifier.Publish<Contact>();
    }

    public async Task<Contact> CreateLinkedContactAsync(string otherUid, string displayName)
    {
        var contact = new Contact
        {
            Name = SharedIdentity.Clamp(displayName),
            ContactType = "Friend",
            LinkedUid = otherUid
        };
        await _db.Database.InsertAsync(contact);
        DataChangeNotifier.Publish<Contact>();
        return contact;
    }
}
