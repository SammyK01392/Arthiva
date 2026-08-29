using SQLite;

namespace Arthiva.Models;

public class EmiPayment
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int EmiId { get; set; }

    public decimal Amount { get; set; }

    public int InstallmentNo { get; set; }

    [Indexed]
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Transaction table reference
    /// </summary>
    public int? TransactionId { get; set; }

    /// <summary>
    /// Paid, Pending, Failed, Skipped
    /// </summary>
    [MaxLength(50)]
    public string Status { get; set; } = "Paid";

    /// <summary>
    /// Due date of installment
    /// </summary>
    public DateTime? DueDate { get; set; }

    /// <summary>
    /// Late fee if any
    /// </summary>
    public decimal PenaltyAmount { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}