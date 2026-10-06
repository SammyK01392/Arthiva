using SQLite;

namespace MoneySpend.Models;

/// <summary>
/// One shared bill that YOU paid for (Phase 1). Your own share is a normal
/// Expense Transaction (MyTransactionId); every friend's share is a
/// BorrowLend "Lend" record (see SplitShare.BorrowLendId).
/// </summary>
public class SplitExpense
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Full bill amount that left your account.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Your own portion (0 if you paid only for others).</summary>
    public decimal MyShareAmount { get; set; }

    /// <summary>Equal / Exact / Percent</summary>
    [MaxLength(20)]
    public string SplitMethod { get; set; } = "Equal";

    [Indexed]
    public DateTime SplitDate { get; set; } = DateTime.Now;

    public int AccountId { get; set; }

    public int CategoryId { get; set; }

    /// <summary>Linked Expense Transaction for your share (null if share is 0).</summary>
    public int? MyTransactionId { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    // ── Phase 2 ──────────────────────────────────────────────

    /// <summary>Optional group this bill belongs to.</summary>
    [Indexed]
    public int? GroupId { get; set; }

    /// <summary>Who paid the bill. null = you.</summary>
    public int? PaidByContactId { get; set; }

    /// <summary>
    /// When a FRIEND paid: the Borrow record for YOUR share (no account entry is made
    /// when the split is created — cash-basis; the expense is booked when you pay them back).
    /// </summary>
    public int? MyBorrowLendId { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
