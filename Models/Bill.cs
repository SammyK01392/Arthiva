using SQLite;

namespace Arthiva.Models;

public class Bill
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Provider { get; set; }

    /// <summary>
    /// Electricity, Mobile Recharge, Internet, Rent etc.
    /// </summary>
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    [Indexed]
    public DateTime DueDate { get; set; }

    /// <summary>
    /// Reminder before due date
    /// </summary>
    public int ReminderDays { get; set; } = 3;

    public bool IsRecurring { get; set; }

    /// <summary>
    /// Monthly, Quarterly, Yearly
    /// </summary>
    [MaxLength(50)]
    public string? Frequency { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    public bool ReminderEnabled { get; set; } = true;

    /// <summary>
    /// Last paid date
    /// </summary>
    public DateTime? LastPaymentDate { get; set; }

    /// <summary>
    /// Next due date for recurring bills
    /// </summary>
    public DateTime? NextDueDate { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
