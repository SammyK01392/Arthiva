using SQLite;

namespace Arthiva.Models;

public class EmiMaster
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? LoanProvider { get; set; }

    /// <summary>
    /// Total Loan Amount
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Monthly EMI Amount
    /// </summary>
    public decimal EmiAmount { get; set; }

    /// <summary>
    /// Interest Rate (%)
    /// </summary>
    public decimal InterestRate { get; set; }

    public int TotalInstallment { get; set; }

    public int PaidInstallment { get; set; }

    /// <summary>
    /// Remaining EMI Count
    /// </summary>
    public int RemainingInstallment => TotalInstallment - PaidInstallment;

    /// <summary>
    /// Due day of every month (1-31)
    /// </summary>
    public int DueDay { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Linked account used for EMI payment
    /// </summary>
    public int? AccountId { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "Active";

    /// <summary>
    /// Loan Number / Agreement Number
    /// </summary>
    [MaxLength(100)]
    public string? LoanReferenceNo { get; set; }

    /// <summary>
    /// Auto reminder before due date
    /// </summary>
    public bool ReminderEnabled { get; set; } = true;

    /// <summary>
    /// Reminder before due date (days)
    /// </summary>
    public int ReminderBeforeDays { get; set; } = 3;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
