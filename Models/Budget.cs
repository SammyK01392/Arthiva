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

    /// <summary>
    /// "₹1,200.00 / ₹5,000.00" — precomputed to avoid MultiBinding, which
    /// renders invisible inside CollectionView on Windows/WinUI.
    /// </summary>
    public string AmountSummary => $"₹{SpentAmount:N2} / ₹{BudgetAmount:N2}";

    /// <summary>
    /// Spent/Budget ratio (0.0–1.0) for progress bar binding.
    /// </summary>
    public double ProgressRatio => BudgetAmount > 0
        ? Math.Clamp((double)(SpentAmount / BudgetAmount), 0d, 1d)
        : 0d;

    /// <summary>
    /// "₹3,800.00 remaining" — precomputed, no MultiBinding.
    /// </summary>
    public string RemainingText => $"₹{RemainingAmount:N2} remaining";
}