using SQLite;

namespace MoneySpend.Models;

public static class SettlementStatus
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// Local mirror of ONE repayment event (full or partial) on a shared request.
/// Either person can propose ("I paid" / "I received"); the OTHER person must confirm.
/// Only a confirmed settlement is written to the books (as a BorrowLendTransaction) on both sides.
/// Doubles as UI cache, idempotency ledger and home of the private account choice.
/// Included in the Drive backup.
/// </summary>
public class SharedSettlementLink
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Globally unique (32 hex chars) = Firebase node key. Also stamped on the BorrowLendTransaction.</summary>
    [Indexed(Unique = true), MaxLength(40)]
    public string SharedSettlementId { get; set; } = string.Empty;

    [Indexed, MaxLength(40)]
    public string SharedRequestId { get; set; } = string.Empty;

    public bool ProposedByMe { get; set; }

    /// <summary>True when I am the one who pays (the borrower) on the parent request.</summary>
    public bool IAmPayer { get; set; }

    [MaxLength(60)]
    public string OtherName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public DateTime SettlementDate { get; set; } = DateTime.Now;

    [Indexed, MaxLength(12)]
    public string Status { get; set; } = SettlementStatus.Pending;

    public long Version { get; set; }

    /// <summary>PRIVATE: account chosen on THIS device. null = no account entry.</summary>
    public int? AccountId { get; set; }

    /// <summary>The confirmed payment has been written to the local books.</summary>
    public bool Applied { get; set; }

    /// <summary>Confirmed remotely but couldn't be written locally (e.g. it exceeded the outstanding balance).</summary>
    public bool Conflict { get; set; }

    public int? BorrowLendTransactionId { get; set; }

    /// <summary>The create / index fan-out still has to be (re)tried.</summary>
    public bool FanoutPending { get; set; }

    /// <summary>Offline outbox: Confirmed / Rejected / Cancelled waiting to be sent.</summary>
    [MaxLength(12)]
    public string? PendingAction { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── UI helpers (not persisted) ─────────────────────────────

    [Ignore]
    public bool IsIncomingPending =>
        !ProposedByMe && Status == SettlementStatus.Pending && PendingAction is null;

    [Ignore]
    public bool IsOutgoingPending =>
        ProposedByMe && Status == SettlementStatus.Pending && PendingAction is null;

    [Ignore]
    public string DateText => SettlementDate.ToString("dd MMM yyyy");

    [Ignore]
    public string Headline => (ProposedByMe, IAmPayer) switch
    {
        (true, true) => $"You paid {OtherName} ₹{Amount:N2}",
        (true, false) => $"You received ₹{Amount:N2} from {OtherName}",
        (false, true) => $"{OtherName} says they received ₹{Amount:N2} from you",
        (false, false) => $"{OtherName} says they paid you ₹{Amount:N2}"
    };

    [Ignore]
    public string StatusText => PendingAction is not null
        ? $"{PendingAction} - will be sent when you're online"
        : Conflict
            ? "Confirmed, but couldn't be recorded (more than the balance)"
            : Status switch
            {
                SettlementStatus.Pending => ProposedByMe
                    ? $"Waiting for {OtherName} to confirm"
                    : "Waiting for your confirmation",
                SettlementStatus.Confirmed => "Confirmed",
                SettlementStatus.Rejected => "Rejected",
                SettlementStatus.Cancelled => "Cancelled",
                _ => Status
            };
}
