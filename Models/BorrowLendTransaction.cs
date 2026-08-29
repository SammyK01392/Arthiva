using SQLite;

namespace Arthiva.Models;

public class BorrowLendTransaction
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int BorrowLendId { get; set; }

    public decimal Amount { get; set; }

    [Indexed]
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Borrow, Lend, Return, Receive, PartialReturn
    /// </summary>
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Linked Transaction table reference
    /// </summary>
    public int? TransactionId { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>
    /// UPI, Cash, Bank Transfer etc.
    /// </summary>
    [MaxLength(50)]
    public string? PaymentMethod { get; set; }

    [MaxLength(100)]
    public string? ReferenceNo { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}