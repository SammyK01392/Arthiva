using Arthiva.Models;

namespace Arthiva.Services;

public class RecurringTransactionService : IRecurringTransactionService
{
    private readonly IGenericRepository<RecurringTransaction> _repo;
    private readonly ITransactionService _transactionService;

    public RecurringTransactionService(
        IGenericRepository<RecurringTransaction> repo,
        ITransactionService transactionService)
    {
        _repo = repo;
        _transactionService = transactionService;
    }

    public async Task<List<RecurringTransaction>> GetAllAsync(bool includeInactive = false)
    {
        var all = await _repo.FindAsync(r => !r.IsDeleted);
        return includeInactive ? all : all.Where(r => r.IsActive).ToList();
    }

    public Task<RecurringTransaction?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public async Task<List<RecurringTransaction>> GetDueAsync(DateTime? asOf = null)
    {
        var cutoff = asOf ?? DateTime.UtcNow;
        var rules = await _repo.FindAsync(r =>
            !r.IsDeleted && r.IsActive && r.AutoCreateTransaction && r.NextRunDate <= cutoff);
        return rules.ToList();
    }

    public Task<int> CreateAsync(RecurringTransaction rule)
    {
        rule.CreatedAt = DateTime.UtcNow;
        rule.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(rule);
    }

    public Task<int> UpdateAsync(RecurringTransaction rule)
    {
        rule.UpdatedAt = DateTime.UtcNow;
        return _repo.UpdateAsync(rule);
    }

    public async Task<int> DeactivateAsync(int id)
    {
        var rule = await _repo.GetByIdAsync(id);
        if (rule is null) return 0;

        rule.IsActive = false;
        rule.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(rule);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var rule = await _repo.GetByIdAsync(id);
        if (rule is null) return 0;

        rule.IsDeleted = true;
        rule.IsActive = false;
        rule.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(rule);
    }

    public async Task<int> ExecuteAsync(int ruleId)
    {
        var rule = await _repo.GetByIdAsync(ruleId);
        if (rule is null || !rule.IsActive) return 0;

        var transaction = new Transaction
        {
            AccountId = rule.AccountId,
            CategoryId = rule.CategoryId,
            Amount = rule.Amount,
            TransactionType = rule.TransactionType,
            TransactionDate = rule.NextRunDate,
            Description = rule.Title,
            IsRecurring = true,
            SourceType = "RecurringTransaction",
            SourceReferenceId = rule.Id
        };
        await _transactionService.AddTransactionAsync(transaction);

        rule.LastExecutedDate = rule.NextRunDate;
        rule.NextRunDate = CalculateNextRunDate(rule.NextRunDate, rule.Frequency);

        if (rule.EndDate.HasValue && rule.NextRunDate > rule.EndDate.Value)
            rule.IsActive = false;

        rule.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(rule);
    }

    public async Task<int> ExecuteAllDueAsync()
    {
        var due = await GetDueAsync();
        var count = 0;
        foreach (var rule in due)
            count += await ExecuteAsync(rule.Id);

        return count;
    }

    private static DateTime CalculateNextRunDate(DateTime current, string frequency)
        => frequency switch
        {
            "Daily" => current.AddDays(1),
            "Weekly" => current.AddDays(7),
            "Yearly" => current.AddYears(1),
            _ => current.AddMonths(1) // default: Monthly
        };
}
