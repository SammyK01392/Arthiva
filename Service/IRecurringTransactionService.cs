using Arthiva.Models;

namespace Arthiva.Services;

public interface IRecurringTransactionService
{
    Task<List<RecurringTransaction>> GetAllAsync(bool includeInactive = false);

    Task<RecurringTransaction?> GetByIdAsync(int id);

    /// <summary>Rules whose NextRunDate has arrived and are due to fire.</summary>
    Task<List<RecurringTransaction>> GetDueAsync(DateTime? asOf = null);

    Task<int> CreateAsync(RecurringTransaction rule);

    Task<int> UpdateAsync(RecurringTransaction rule);

    Task<int> DeactivateAsync(int id);

    Task<int> SoftDeleteAsync(int id);

    /// <summary>
    /// Creates the actual Transaction for a due rule (via TransactionService,
    /// so the account balance updates too) and rolls NextRunDate forward.
    /// </summary>
    Task<int> ExecuteAsync(int ruleId);

    /// <summary>Convenience: executes every currently-due rule.</summary>
    Task<int> ExecuteAllDueAsync();
}
