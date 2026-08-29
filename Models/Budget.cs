using SQLite;

namespace Arthiva.Models;

public class Budget
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int CategoryId { get; set; }

    [Indexed]
    public int Month { get; set; }

    [Indexed]
    public int Year { get; set; }

    /// <summary>
    /// Planned Budget
    /// </summary>
    public decimal BudgetAmount { get; set; }

    /// <summary>
    /// Actual Expense
    /// </summary>
    public decimal SpentAmount { get; set; }

    /// <summary>
    /// Remaining Budget
    /// </summary>
    public decimal RemainingAmount => BudgetAmount - SpentAmount;

    /// <summary>
    /// Warning at 80%, 90%, etc.
    /// </summary>
    public decimal AlertPercentage { get; set; } = 80;

    public bool NotificationsEnabled { get; set; } = true;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}