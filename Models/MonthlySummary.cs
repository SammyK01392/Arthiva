using SQLite;

namespace Arthiva.Models;

public class MonthlySummary
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int Month { get; set; }

    [Indexed]
    public int Year { get; set; }

    public decimal Income { get; set; }

    public decimal Expense { get; set; }

    public decimal Saving { get; set; }

    public decimal EmiTotal { get; set; }

    public decimal BillTotal { get; set; }

    public decimal BorrowTotal { get; set; }

    public decimal LendTotal { get; set; }

    /// <summary>
    /// Total number of transactions in month
    /// </summary>
    public int TransactionCount { get; set; }

    /// <summary>
    /// Month opening balance
    /// </summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>
    /// Month closing balance
    /// </summary>
    public decimal ClosingBalance { get; set; }

    /// <summary>
    /// Budget assigned for month
    /// </summary>
    public decimal BudgetAmount { get; set; }

    /// <summary>
    /// Actual budget used
    /// </summary>
    public decimal BudgetUsed { get; set; }

    public bool IsGenerated { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}