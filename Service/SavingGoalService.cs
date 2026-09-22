using Arthiva.Models;

namespace Arthiva.Services;

public class SavingGoalService : ISavingGoalService
{
    private readonly IGenericRepository<SavingGoal> _repo;
    private readonly IGenericRepository<GoalTransaction> _txnRepo;
    private readonly ITransactionService _transactionService;

    public SavingGoalService(
        IGenericRepository<SavingGoal> repo,
        IGenericRepository<GoalTransaction> txnRepo,
        ITransactionService transactionService)
    {
        _repo = repo;
        _txnRepo = txnRepo;
        _transactionService = transactionService;
    }

    public async Task<List<SavingGoal>> GetAllAsync(bool includeCompleted = false)
    {
        var goals = await _repo.FindAsync(g => !g.IsDeleted);
        if (!includeCompleted)
            goals = goals.Where(g => !g.IsCompleted).ToList();

        return goals.ToList();
    }

    public Task<SavingGoal?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public async Task<List<GoalTransaction>> GetTransactionsAsync(int goalId)
    {
        var txns = await _txnRepo.FindAsync(t => t.GoalId == goalId);
        return txns.OrderByDescending(t => t.TransactionDate).ToList();
    }

    public Task<int> CreateAsync(SavingGoal goal)
    {
        goal.CreatedAt = DateTime.UtcNow;
        goal.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(goal);
    }

    public Task<int> UpdateAsync(SavingGoal goal)
    {
        goal.UpdatedAt = DateTime.UtcNow;
        return _repo.UpdateAsync(goal);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var goal = await _repo.GetByIdAsync(id);
        if (goal is null) return 0;

        goal.IsDeleted = true;
        goal.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(goal);
    }

    public async Task<int> ContributeAsync(int goalId, decimal amount, int? accountId = null, string? notes = null, DateTime? transactionDate = null)
        => await RecordGoalTransactionAsync(goalId, amount, "Contribution", accountId, notes, transactionDate);

    public async Task<int> WithdrawAsync(int goalId, decimal amount, int? accountId = null, string? notes = null, DateTime? transactionDate = null)
        => await RecordGoalTransactionAsync(goalId, amount, "Withdrawal", accountId, notes, transactionDate);

    private async Task<int> RecordGoalTransactionAsync(
        int goalId, decimal amount, string movementType, int? accountId, string? notes, DateTime? transactionDate)
    {
        var goal = await _repo.GetByIdAsync(goalId);
        if (goal is null) return 0;

        if (movementType == "Contribution")
        {
            goal.SavedAmount += amount;
        }
        else
        {
            goal.SavedAmount -= amount;
            if (goal.SavedAmount < 0) goal.SavedAmount = 0;
        }

        goal.IsCompleted = goal.SavedAmount >= goal.TargetAmount;
        if (goal.IsCompleted) goal.Status = "Completed";

        goal.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(goal);

        // Uses the date the person actually chose, falling back to now
        // only if none was supplied (e.g. called from elsewhere without one).
        var effectiveDate = transactionDate ?? DateTime.UtcNow;

        var goalTxn = new GoalTransaction
        {
            GoalId = goalId,
            Amount = amount,
            TransactionType = movementType,
            TransactionDate = effectiveDate,
            Notes = notes,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var result = await _txnRepo.AddAsync(goalTxn);

        if (accountId.HasValue)
        {
            var transactionType = movementType == "Contribution" ? "Expense" : "Income";
            var createdTxn = new Transaction
            {
                AccountId = accountId.Value,
                Amount = amount,
                TransactionType = transactionType,
                TransactionDate = goalTxn.TransactionDate,
                Description = $"{goal.Name} - {movementType}",
                SourceType = "Goal",
                SourceReferenceId = goal.Id
            };
            await _transactionService.AddTransactionAsync(createdTxn);

            goalTxn.TransactionId = createdTxn.Id;
            goalTxn.UpdatedAt = DateTime.UtcNow;
            await _txnRepo.UpdateAsync(goalTxn);
        }

        return result;
    }
}