using SQLite;

namespace MoneySpend.Models;

public enum DetectedStatus
{
    Pending = 0,
    Added = 1,
    Dismissed = 2
}

/// <summary>
/// A payment (money out) or receipt (money in) found in an SMS.
/// Local-only (not synced). Becomes a real Transaction when it is added
/// (by the user, or automatically when Auto-add is ON).
/// </summary>
public class DetectedTransaction
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public decimal Amount { get; set; }

    /// <summary>true = money came IN (Income), false = money went OUT (Expense).</summary>
    public bool IsCredit { get; set; }

    [MaxLength(100)]
    public string? Payee { get; set; }

    /// <summary>Payee exactly as the SMS said it (lowercase). Used to remember names/categories.</summary>
    [MaxLength(150)]
    public string? MatchKey { get; set; }

    /// <summary>Category remembered from an earlier entry with the same payee.</summary>
    public int? SuggestedCategoryId { get; set; }

    /// <summary>UPI reference number - used to avoid duplicates.</summary>
    [Indexed, MaxLength(100)]
    public string? ReferenceId { get; set; }

    /// <summary>Last 3-4 digits of the account/card seen in the SMS.</summary>
    [MaxLength(10)]
    public string? AccountHint { get; set; }

    [MaxLength(30)]
    public string? Method { get; set; }

    [MaxLength(50)]
    public string? Sender { get; set; }

    public string RawBody { get; set; } = string.Empty;

    [Indexed]
    public DateTime TransactionDate { get; set; } = DateTime.Now;

    [Indexed]
    public DetectedStatus Status { get; set; } = DetectedStatus.Pending;

    /// <summary>Set once it becomes a real Transaction.</summary>
    public int? TransactionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // ───────────── UI only ─────────────

    [Ignore] public bool IsDebit => !IsCredit;

    [Ignore]
    public string DisplayPayee => string.IsNullOrWhiteSpace(Payee) ? "Unknown payee" : Payee!;

    [Ignore]
    public string AddLabel => IsCredit ? "Add as income" : "Add as expense";

    [Ignore]
    public string Subtitle
    {
        get
        {
            var parts = new List<string> { IsCredit ? "Received" : "Paid", Method ?? "Bank" };

            if (!string.IsNullOrWhiteSpace(AccountHint))
                parts.Add($"A/c ••{AccountHint}");

            return string.Join(" • ", parts);
        }
    }
}
