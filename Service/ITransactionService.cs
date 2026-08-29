using Arthiva.Models;

namespace Arthiva.Services;

public interface ITransactionService
{
    Task<List<Transaction>> GetAllAsync();

    Task<List<Transaction>> GetByAccountAsync(int accountId);

    Task<List<Transaction>> GetByCategoryAsync(int categoryId);

    Task<List<Transaction>> GetByDateRangeAsync(DateTime from, DateTime to);

    Task<Transaction?> GetByIdAsync(int id);

    /// <summary>
    /// Adds a transaction AND applies its effect on the linked account's balance.
    /// Income adds to balance, Expense subtracts.
    /// </summary>
    Task<int> AddTransactionAsync(Transaction transaction);

    /// <summary>
    /// Updates a transaction. Reverses the old balance effect and re-applies
    /// the new one (handles amount, type, or account changes correctly).
    /// </summary>
    Task<int> UpdateTransactionAsync(Transaction transaction);

    /// <summary>
    /// Soft-deletes a transaction and reverses its effect on the account balance.
    /// </summary>
    Task<int> DeleteTransactionAsync(int id);

    Task<decimal> GetTotalByTypeAsync(string transactionType, DateTime from, DateTime to);
}
