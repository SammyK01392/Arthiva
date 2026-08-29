using Arthiva.Models;

namespace Arthiva.Services;

public interface IAccountService
{
    Task<List<Account>> GetAllAsync(bool includeInactive = false);

    Task<Account?> GetByIdAsync(int id);

    Task<Account?> GetDefaultAccountAsync();

    Task<decimal> GetTotalBalanceAsync();

    Task<int> CreateAsync(Account account);

    Task<int> UpdateAsync(Account account);

    Task<int> SoftDeleteAsync(int id);

    Task SetDefaultAsync(int accountId);

    /// <summary>
    /// Adjusts an account's CurrentBalance by the given delta (+ve or -ve).
    /// Used internally by TransactionService, EmiService, BillService, etc.
    /// so balance always stays in sync with recorded money movements.
    /// </summary>
    Task AdjustBalanceAsync(int accountId, decimal delta);
}
