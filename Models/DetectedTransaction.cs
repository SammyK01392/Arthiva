using SQLite;

namespace MoneySpend.Models;

public enum DetectedStatus
{
    Pending = 0,
    Added = 1,
    Dismissed = 2
}

/// <summary>
/// A payment (money out) or receipt (money in) found in an SMS or imported from a statement.
/// Local-only (not synced). Becomes a real Transaction when it is added
/// (by the user, or automatically when Auto-add is ON / "Add all confident").
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

    /// <summary>Payee exactly as the SMS/statement said it (lowercase). Used to remember names/categories.</summary>
    [MaxLength(150)]
    public string? MatchKey { get; set; }

    /// <summary>Category remembered / guessed for this payee.</summary>
    public int? SuggestedCategoryId { get; set; }

    /// <summary>Name of that category (shown on the card).</summary>
    [MaxLength(60)]
    public string? CategoryHint { get; set; }

    /// <summary>UPI reference number (UTR) - used to avoid duplicates.</summary>
    [Indexed, MaxLength(100)]
    public string? ReferenceId { get; set; }

    /// <summary>Last 3-4 digits of the account/card seen in the SMS.</summary>
    [MaxLength(10)]
    public string? AccountHint { get; set; }

    /// <summary>Bank name seen next to the account number (e.g. "IDBI Bank"), when the statement shows it.</summary>
    [MaxLength(60)]
    public string? AccountName { get; set; }

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

    // ───────────── statement import ─────────────

    /// <summary>"Sms" or "Statement".</summary>
    [MaxLength(20)]
    public string Source { get; set; } = "Sms";

    /// <summary>File it came from (statement imports only).</summary>
    [MaxLength(150)]
    public string? SourceName { get; set; }

    /// <summary>Income, Expense, Emi, Borrow, Lend, ReturnPaid, ReturnReceived, Transfer.</summary>
    [MaxLength(20)]
    public string? SuggestedType { get; set; }

    /// <summary>High, Medium, Low. Low = needs review.</summary>
    [MaxLength(10)]
    public string Confidence { get; set; } = "Medium";

    [MaxLength(200)]
    public string? Reason { get; set; }

    // ───────────── UI only ─────────────

    [Ignore] public bool IsDebit => !IsCredit;

    [Ignore] public bool IsFromStatement => Source == "Statement";

    [Ignore] public bool IsHigh => Confidence == "High";
    [Ignore] public bool IsMedium => Confidence == "Medium";
    [Ignore] public bool IsLow => Confidence == "Low";

    [Ignore] public bool HasReason => IsFromStatement && !string.IsNullOrWhiteSpace(Reason);

    [Ignore] public bool HasCategoryHint => !string.IsNullOrWhiteSpace(CategoryHint);

    [Ignore] public string CategoryLine => $"Category: {CategoryHint}";

    /// <summary>Borrow / Lend / Return / Transfer rows cannot create a Borrow-Lend record yet.</summary>
    [Ignore]
    public bool IsBorrowLendType =>
        SuggestedType is "Borrow" or "Lend" or "ReturnPaid" or "ReturnReceived" or "Transfer";

    /// <summary>A payment to / from a person: becomes a Borrow/Lend record (and a Contact if new).</summary>
    [Ignore]
    public bool IsPersonRow =>
        SuggestedType is "Borrow" or "Lend" or "ReturnPaid" or "ReturnReceived";

    /// <summary>Safe to add in one go ("Add all confident").</summary>
    [Ignore]
    public bool IsAutoAddable =>
        IsHigh && (SuggestedType is null or "Income" or "Expense" or "Emi");

    [Ignore]
    public string TypeLabel => SuggestedType switch
    {
        "ReturnPaid" => "You returned money",
        "ReturnReceived" => "Money returned to you",
        "Emi" => "EMI",
        "Transfer" => "Own transfer?",
        null => string.Empty,
        var t => t
    };

    [Ignore]
    public string DisplayPayee => string.IsNullOrWhiteSpace(Payee) ? "Unknown payee" : Payee!;

    [Ignore]
    public string AddLabel => IsPersonRow
        ? "Review & add"
        : SuggestedType == "Transfer"
            ? "Review"
            : IsCredit ? "Add as income" : "Add as expense";

    [Ignore]
    public string Subtitle
    {
        get
        {
            var parts = new List<string> { IsCredit ? "Received" : "Paid", Method ?? "Bank" };

            if (AccountHint == "GIFT")
                parts.Add("Gift Card");
            else if (!string.IsNullOrWhiteSpace(AccountHint))
                parts.Add(string.IsNullOrWhiteSpace(AccountName) ? $"A/c ••{AccountHint}" : $"{AccountName} ••{AccountHint}");

            if (IsFromStatement && !string.IsNullOrWhiteSpace(SourceName))
                parts.Add(SourceName!);

            return string.Join(" • ", parts);
        }
    }
}
