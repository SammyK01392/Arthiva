using SQLite;

namespace Arthiva.Models;

public class Transaction
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int AccountId { get; set; }

    [Indexed]
    public int CategoryId { get; set; }

    public decimal Amount { get; set; }

    [MaxLength(50)]
    public string TransactionType { get; set; } = string.Empty;

    [Indexed]
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(100)]
    public string? Payee { get; set; }

    [MaxLength(100)]
    public string? ReferenceId { get; set; }

    [MaxLength(50)]
    public string? PaymentMethod { get; set; }

    public bool IsRecurring { get; set; }

    /// <summary>
    /// EMI, Bill, BorrowLend etc. ka reference id
    /// </summary>
    public int? SourceReferenceId { get; set; }

    /// <summary>
    /// EMI, Bill, BorrowLend, Goal etc.
    /// </summary>
    [MaxLength(50)]
    public string? SourceType { get; set; }

    public string? AttachmentPath { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}