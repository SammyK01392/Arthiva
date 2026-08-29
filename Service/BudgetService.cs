using Arthiva.Models;

namespace Arthiva.Services;

public class BudgetService : IBudgetService
{
    private readonly IGenericRepository<Budget> _repo;

    public BudgetService(IGenericRepository<Budget> repo)
    {
        _repo = repo;
    }

    public async Task<List<Budget>> GetByMonthAsync(int month, int year)
    {
        var budgets = await _repo.FindAsync(b =>
            !b.IsDeleted && b.IsActive && b.Month == month && b.Year == year);
        return budgets.ToList();
    }

    public Task<Budget?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public async Task<Budget?> GetByCategoryAndMonthAsync(int categoryId, int month, int year)
    {
        var budgets = await _repo.FindAsync(b =>
            !b.IsDeleted && b.CategoryId == categoryId && b.Month == month && b.Year == year);
        return budgets.FirstOrDefault();
    }

    public Task<int> CreateAsync(Budget budget)
    {
        budget.CreatedAt = DateTime.UtcNow;
        budget.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(budget);
    }

    public Task<int> UpdateAsync(Budget budget)
    {
        budget.UpdatedAt = DateTime.UtcNow;
        return _repo.UpdateAsync(budget);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var budget = await _repo.GetByIdAsync(id);
        if (budget is null) return 0;

        budget.IsDeleted = true;
        budget.IsActive = false;
        budget.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(budget);
    }

    public async Task<bool> RecalculateSpentAsync(int budgetId, ITransactionService transactionService)
    {
        var budget = await _repo.GetByIdAsync(budgetId);
        if (budget is null) return false;

        var from = new DateTime(budget.Year, budget.Month, 1);
        var to = from.AddMonths(1).AddTicks(-1);

        var categoryTransactions = await transactionService.GetByCategoryAsync(budget.CategoryId);
        var spent = categoryTransactions
            .Where(t => t.TransactionType == "Expense" && t.TransactionDate >= from && t.TransactionDate <= to)
            .Sum(t => t.Amount);

        budget.SpentAmount = spent;
        budget.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(budget);

        if (budget.BudgetAmount <= 0) return false;

        var percentUsed = (spent / budget.BudgetAmount) * 100;
        return percentUsed >= budget.AlertPercentage;
    }
}
