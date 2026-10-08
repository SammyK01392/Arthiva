using SQLite;

namespace MoneySpend.Models;

public static class SharedRequestStatus
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Settled = "Settled";
}

/// <summary>Always from the SENDER's point of view. Lend/Split: sender is owed money. Borrow: sender owes money.</summary>
public static class SharedRequestType
{
    public const string Lend = "Lend";
    public const string Borrow = "Borrow";
    public const string Split = "Split";
}

public static class SharedRequestRole
{
    public const string Sender = "Sender";
    public const string Receiver = "Receiver";
}

/// <summary>
/// Local mirror of ONE shared Firebase request. It is at once:
///  - the UI cache (works offline),
///  - the idempotency ledger (BorrowLendId != null  ==  "already applied on this device"),
///  - the home of PRIVATE per-device choices (AccountId) that never go to Firebase.
/// Included in the Drive backup so a restore keeps the mapping.
/// </summary>
public class SharedRequestLink
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Globally unique id (32 hex chars) = the Firebase node key.</summary>
    [Indexed(Unique = true), MaxLength(40)]
    public string SharedRequestId { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Role { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Type { get; set; } = string.Empty;

    [Indexed, MaxLength(128)]
    public string OtherUid { get; set; } = string.Empty;

    [MaxLength(60)]
    public string OtherName { get; set; } = string.Empty;

    public int? ContactId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(80)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Local-time date of the underlying money movement.</summary>
    public DateTime RequestDate { get; set; } = DateTime.Now;

    /// <summary>Opaque id grouping the requests of one split (one request per friend).</summary>
    [MaxLength(64)]
    public string? ParentId { get; set; }

    [Indexed, MaxLength(12)]
    public string Status { get; set; } = SharedRequestStatus.Pending;

    /// <summary>Last remote version seen.</summary>
    public long Version { get; set; }

    /// <summary>Last value of our /userRequests entry seen (change detection for reconcile).</summary>
    public long IndexStamp { get; set; }

    /// <summary>PRIVATE: account chosen on THIS device. null = no account entry (the "Unassigned" case).</summary>
    public int? AccountId { get; set; }

    /// <summary>Local BorrowLend created/linked for this request. null = not applied on this device yet.</summary>
    public int? BorrowLendId { get; set; }

    /// <summary>The Firebase create / index fan-out still has to be (re)tried.</summary>
    public bool FanoutPending { get; set; }

    /// <summary>
    /// Offline outbox: a response (Accepted / Rejected / Cancelled) the user already gave but that
    /// hasn't reached Firebase yet. null = nothing queued. Replaying is safe: the rules reject it
    /// if the request changed in the meantime, and we then just sync the real state.
    /// </summary>
    [MaxLength(12)]
    public string? PendingAction { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── UI helpers (not persisted) ─────────────────────────────

    [Ignore]
    public bool IsQueued => PendingAction is not null;

    [Ignore]
    public bool IsIncomingPending =>
        Role == SharedRequestRole.Receiver && Status == SharedRequestStatus.Pending && PendingAction is null;

    [Ignore]
    public bool IsOutgoingPending =>
        Role == SharedRequestRole.Sender && Status == SharedRequestStatus.Pending && PendingAction is null;

    [Ignore]
    public string DateText => RequestDate.ToString("dd MMM yyyy");

    [Ignore]
    public string Headline => (Role, Type) switch
    {
        (SharedRequestRole.Receiver, SharedRequestType.Lend) => $"{OtherName} lent you ₹{Amount:N2}",
        (SharedRequestRole.Receiver, SharedRequestType.Borrow) => $"{OtherName} borrowed ₹{Amount:N2} from you",
        (SharedRequestRole.Receiver, SharedRequestType.Split) => $"{OtherName} paid a bill. Your share: ₹{Amount:N2}",
        (SharedRequestRole.Sender, SharedRequestType.Lend) => $"You lent {OtherName} ₹{Amount:N2}",
        (SharedRequestRole.Sender, SharedRequestType.Borrow) => $"You borrowed ₹{Amount:N2} from {OtherName}",
        (SharedRequestRole.Sender, SharedRequestType.Split) => $"{OtherName}'s share of a bill: ₹{Amount:N2}",
        _ => $"₹{Amount:N2} with {OtherName}"
    };

    [Ignore]
    public string StatusText => PendingAction is not null
        ? $"{PendingAction} - will be sent when you're online"
        : Status switch
        {
            SharedRequestStatus.Pending => Role == SharedRequestRole.Sender
                ? $"Waiting for {OtherName} to respond"
                : "Waiting for your response",
            SharedRequestStatus.Accepted => "Accepted",
            SharedRequestStatus.Rejected => "Rejected",
            SharedRequestStatus.Cancelled => "Cancelled",
            SharedRequestStatus.Settled => "Settled",
            _ => Status
        };
}
