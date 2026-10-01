using MoneySpend.Models;

namespace MoneySpend.Services;

public class TransactionService : ITransactionService
{
    private readonly IGenericRepository<Transaction> _repo;
    private readonly IAccountService _accountService;

    public TransactionService(IGenericRepository<Transaction> repo, IAccountService accountService)
    {
        _repo = repo;
        _accountService = accountService;
    }

    public async Task<List<Transaction>> GetAllAsync()
    {
        var transactions = await _repo.FindAsync(t => !t.IsDeleted);
        return transactions.OrderByDescending(t => t.TransactionDate).ToList();
    }

    public async Task<List<Transaction>> GetByAccountAsync(int accountId)
    {
        var transactions = await _repo.FindAsync(t => !t.IsDeleted && t.AccountId == accountId);
        return transactions.OrderByDescending(t => t.TransactionDate).ToList();
    }

    public async Task<List<Transaction>> GetByCategoryAsync(int categoryId)
    {
        var transactions = await _repo.FindAsync(t => !t.IsDeleted && t.CategoryId == categoryId);
        return transactions.OrderByDescending(t => t.TransactionDate).ToList();
    }

    public async Task<List<Transaction>> GetByDateRangeAsync(DateTime from, DateTime to)
    {
        var transactions = await _repo.FindAsync(t =>
            !t.IsDeleted && t.TransactionDate >= from && t.TransactionDate <= to);
        return transactions.OrderByDescending(t => t.TransactionDate).ToList();
    }

    public Task<Transaction?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public async Task<int> AddTransactionAsync(Transaction transaction)
    {
        transaction.CreatedAt = DateTime.UtcNow;
        transaction.UpdatedAt = DateTime.UtcNow;

        // Cloud sync bookkeeping. SyncId must never change once assigned —
        // it's the Firestore document id for this record's whole lifetime.
        if (string.IsNullOrWhiteSpace(transaction.SyncId))
            transaction.SyncId = Guid.NewGuid().ToString();

        transaction.SyncStatus = SyncStatus.Pending;

        var result = await _repo.AddAsync(transaction);

        var delta = SignedAmount(transaction);
        await _accountService.AdjustBalanceAsync(transaction.AccountId, delta);

        return result;
    }

    public async Task<int> UpdateTransactionAsync(Transaction transaction)
    {
        var existing = await _repo.GetByIdAsync(transaction.Id);
        if (existing is null) return 0;

        // Reverse the old transaction's effect on its (possibly old) account.
        var reverseDelta = -SignedAmount(existing);
        await _accountService.AdjustBalanceAsync(existing.AccountId, reverseDelta);

        // Preserve the original SyncId even if the caller passed in a
        // Transaction object that never had it populated — the Firestore
        // document id must stay stable across edits.
        if (string.IsNullOrWhiteSpace(transaction.SyncId))
            transaction.SyncId = existing.SyncId ?? Guid.NewGuid().ToString();

        transaction.UpdatedAt = DateTime.UtcNow;
        transaction.SyncStatus = SyncStatus.Pending;

        var result = await _repo.UpdateAsync(transaction);

        // Apply the new transaction's effect on its (possibly new) account.
        var applyDelta = SignedAmount(transaction);
        await _accountService.AdjustBalanceAsync(transaction.AccountId, applyDelta);

        return result;
    }

    public async Task<int> DeleteTransactionAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        if (existing is null) return 0;

        existing.IsDeleted = true;
        existing.UpdatedAt = DateTime.UtcNow;

        // A soft-delete is itself a change that must propagate to the
        // cloud, so it needs to be picked up by the sync engine too.
        existing.SyncStatus = SyncStatus.Pending;

        var result = await _repo.UpdateAsync(existing);

        var reverseDelta = -SignedAmount(existing);
        await _accountService.AdjustBalanceAsync(existing.AccountId, reverseDelta);

        return result;
    }

    public async Task<decimal> GetTotalByTypeAsync(string transactionType, DateTime from, DateTime to)
    {
        var transactions = await _repo.FindAsync(t =>
            !t.IsDeleted &&
            t.TransactionType == transactionType &&
            t.SourceType != "BorrowLend" &&
            t.TransactionDate >= from &&
            t.TransactionDate <= to);

        return transactions.Sum(t => t.Amount);
    }

    /// <summary>
    /// Income => +Amount, Expense => -Amount. Transfers are intentionally left
    /// out here — handle transfers as two linked transactions (debit + credit)
    /// on two accounts if/when that feature is added.
    /// </summary>
    private static decimal SignedAmount(Transaction transaction)
        => transaction.TransactionType == "Income" ? transaction.Amount : -transaction.Amount;
}