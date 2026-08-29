using Arthiva.Models;

namespace Arthiva.Services;

public interface IBudgetService
{
    Task<List<Budget>> GetByMonthAsync(int month, int year);

    Task<Budget?> GetByIdAsync(int id);

    Task<Budget?> GetByCategoryAndMonthAsync(int categoryId, int month, int year);

    Task<int> CreateAsync(Budget budget);

    Task<int> UpdateAsync(Budget budget);

    Task<int> SoftDeleteAsync(int id);

    /// <summary>
    /// Recomputes SpentAmount from actual Expense transactions in that
    /// category/month/year. Returns true if AlertPercentage threshold is crossed.
    /// </summary>
    Task<bool> RecalculateSpentAsync(int budgetId, ITransactionService transactionService);
}
