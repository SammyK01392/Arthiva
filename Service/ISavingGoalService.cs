using Arthiva.Models;

namespace Arthiva.Services;

public interface ISavingGoalService
{
    Task<List<SavingGoal>> GetAllAsync(bool includeCompleted = false);

    Task<SavingGoal?> GetByIdAsync(int id);

    Task<List<GoalTransaction>> GetTransactionsAsync(int goalId);

    Task<int> CreateAsync(SavingGoal goal);

    Task<int> UpdateAsync(SavingGoal goal);

    Task<int> SoftDeleteAsync(int id);

    /// <summary>
    /// Adds money to the goal. If accountId is given, also creates a linked
    /// Expense Transaction (money moves out of the account into the goal).
    /// transactionDate lets the person backdate a contribution they made
    /// earlier but are only recording now; defaults to now if omitted.
    /// </summary>
    Task<int> ContributeAsync(int goalId, decimal amount, int? accountId = null, string? notes = null, DateTime? transactionDate = null);

    /// <summary>
    /// Withdraws money from the goal. If accountId is given, also creates a
    /// linked Income Transaction (money moves back into the account).
    /// </summary>
    Task<int> WithdrawAsync(int goalId, decimal amount, int? accountId = null, string? notes = null, DateTime? transactionDate = null);
}