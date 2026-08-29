using Arthiva.Models;

namespace Arthiva.Services;

public class AccountService : IAccountService
{
    private readonly IGenericRepository<Account> _repo;

    public AccountService(IGenericRepository<Account> repo)
    {
        _repo = repo;
    }

    public async Task<List<Account>> GetAllAsync(bool includeInactive = false)
    {
        var accounts = await _repo.FindAsync(a => !a.IsDeleted);
        return includeInactive
            ? accounts
            : accounts.Where(a => a.IsActive).ToList();
    }

    public Task<Account?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public async Task<Account?> GetDefaultAccountAsync()
    {
        var accounts = await _repo.FindAsync(a => !a.IsDeleted && a.IsDefault);
        return accounts.FirstOrDefault();
    }

    public async Task<decimal> GetTotalBalanceAsync()
    {
        var accounts = await GetAllAsync();
        return accounts.Sum(a => a.CurrentBalance);
    }

    public async Task<int> CreateAsync(Account account)
    {
        account.CreatedAt = DateTime.UtcNow;
        account.UpdatedAt = DateTime.UtcNow;
        account.CurrentBalance = account.OpeningBalance;

        if (account.IsDefault)
            await ClearExistingDefaultAsync();

        return await _repo.AddAsync(account);
    }

    public async Task<int> UpdateAsync(Account account)
    {
        account.UpdatedAt = DateTime.UtcNow;

        if (account.IsDefault)
            await ClearExistingDefaultAsync(account.Id);

        return await _repo.UpdateAsync(account);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var account = await _repo.GetByIdAsync(id);
        if (account is null) return 0;

        account.IsDeleted = true;
        account.IsActive = false;
        account.UpdatedAt = DateTime.UtcNow;

        return await _repo.UpdateAsync(account);
    }

    public async Task SetDefaultAsync(int accountId)
    {
        await ClearExistingDefaultAsync(accountId);

        var account = await _repo.GetByIdAsync(accountId);
        if (account is null) return;

        account.IsDefault = true;
        account.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(account);
    }

    public async Task AdjustBalanceAsync(int accountId, decimal delta)
    {
        var account = await _repo.GetByIdAsync(accountId);
        if (account is null) return;

        account.CurrentBalance += delta;
        account.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(account);
    }

    private async Task ClearExistingDefaultAsync(int? exceptId = null)
    {
        var defaults = await _repo.FindAsync(a => a.IsDefault && !a.IsDeleted);
        foreach (var acc in defaults.Where(a => a.Id != exceptId))
        {
            acc.IsDefault = false;
            acc.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(acc);
        }
    }
}
