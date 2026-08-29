using SQLite;

namespace Arthiva.Models;

public class BorrowLend
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ContactId { get; set; }

    /// <summary>
    /// Borrow / Lend
    /// </summary>
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Original Amount
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Remaining Amount
    /// </summary>
    public decimal PendingAmount { get; set; }

    [Indexed]
    public DateTime GivenDate { get; set; } = DateTime.UtcNow;

    [Indexed]
    public DateTime? DueDate { get; set; }

    /// <summary>
    /// Pending, Completed, Overdue
    /// </summary>
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    [MaxLength(100)]
    public string? ReferenceNo { get; set; }

    [MaxLength(50)]
    public string? PaymentMethod { get; set; }

    /// <summary>
    /// Reminder notification enabled
    /// </summary>
    public bool ReminderEnabled { get; set; } = true;

    /// <summary>
    /// Reminder before due date
    /// </summary>
    public int ReminderBeforeDays { get; set; } = 3;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsClosed { get; set; } = false;

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}