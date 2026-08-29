using SQLite;

namespace Arthiva.Models;

public class BillPayment
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int BillId { get; set; }

    public decimal Amount { get; set; }

    [Indexed]
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Linked Transaction Table Reference
    /// </summary>
    public int? TransactionId { get; set; }

    /// <summary>
    /// Paid, Pending, Failed, Cancelled
    /// </summary>
    [MaxLength(50)]
    public string Status { get; set; } = "Paid";

    [MaxLength(50)]
    public string? PaymentMethod { get; set; }

    [MaxLength(100)]
    public string? ReferenceNo { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
