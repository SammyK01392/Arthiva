using Arthiva.Models;

namespace Arthiva.Services;

public class BorrowLendService : IBorrowLendService
{
    private readonly IGenericRepository<BorrowLend> _repo;
    private readonly IGenericRepository<BorrowLendTransaction> _txnRepo;
    private readonly ITransactionService _transactionService;

    private static readonly string[] ReturnMovementTypes = { "Return", "Receive", "PartialReturn" };

    public BorrowLendService(
        IGenericRepository<BorrowLend> repo,
        IGenericRepository<BorrowLendTransaction> txnRepo,
        ITransactionService transactionService)
    {
        _repo = repo;
        _txnRepo = txnRepo;
        _transactionService = transactionService;
    }

    public async Task<List<BorrowLend>> GetAllAsync(string? type = null, bool includeClosed = false)
    {
        var all = await _repo.FindAsync(b => !b.IsDeleted);

        if (!string.IsNullOrEmpty(type))
            all = all.Where(b => b.Type == type).ToList();

        if (!includeClosed)
            all = all.Where(b => !b.IsClosed).ToList();

        return all.OrderByDescending(b => b.GivenDate).ToList();
    }

    public async Task<List<BorrowLend>> GetByContactAsync(int contactId)
    {
        var records = await _repo.FindAsync(b => !b.IsDeleted && b.ContactId == contactId);
        return records.OrderByDescending(b => b.GivenDate).ToList();
    }

    public Task<BorrowLend?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public async Task<List<BorrowLendTransaction>> GetTransactionsAsync(int borrowLendId)
    {
        var txns = await _txnRepo.FindAsync(t => !t.IsDeleted && t.BorrowLendId == borrowLendId);
        return txns.OrderByDescending(t => t.TransactionDate).ToList();
    }

    public async Task<int> CreateAsync(BorrowLend borrowLend, int? accountId = null)
    {
        borrowLend.PendingAmount = borrowLend.TotalAmount;
        borrowLend.CreatedAt = DateTime.UtcNow;
        borrowLend.UpdatedAt = DateTime.UtcNow;

        var result = await _repo.AddAsync(borrowLend);

        if (accountId.HasValue)
        {
            // Lend = money leaving your account (Expense). Borrow = money entering (Income).
            var transactionType = borrowLend.Type == "Lend" ? "Expense" : "Income";

            await _transactionService.AddTransactionAsync(new Transaction
            {
                AccountId = accountId.Value,
                Amount = borrowLend.TotalAmount,
                TransactionType = transactionType,
                TransactionDate = borrowLend.GivenDate,
                Description = $"{borrowLend.Type} - initial",
                SourceType = "BorrowLend",
                SourceReferenceId = borrowLend.Id
            });
        }

        return result;
    }

    public Task<int> UpdateAsync(BorrowLend borrowLend)
    {
        borrowLend.UpdatedAt = DateTime.UtcNow;
        return _repo.UpdateAsync(borrowLend);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var record = await _repo.GetByIdAsync(id);
        if (record is null) return 0;

        record.IsDeleted = true;
        record.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(record);
    }

    public async Task<BorrowLendResult> RecordTransactionAsync(BorrowLendTransaction txn, int? accountId = null)
    {
        var borrowLend = await _repo.GetByIdAsync(txn.BorrowLendId);
        if (borrowLend is null)
            return new BorrowLendResult(false, "Record not found.");

        if (txn.Amount <= 0)
            return new BorrowLendResult(false, "Enter a valid amount.");

        var isReturnMovement = ReturnMovementTypes.Contains(txn.Type);

        // The core fix: never let a Return/Receive/PartialReturn exceed what's
        // actually still outstanding. Previously this was silently clamped to
        // zero, which is how a ₹1,000 "Receive" against a ₹500 Lend produced a
        // record that looked "Completed" with no error shown anywhere.
        if (isReturnMovement && txn.Amount > borrowLend.PendingAmount)
        {
            return new BorrowLendResult(false,
                $"Amount (₹{txn.Amount:N2}) can't exceed the outstanding balance of ₹{borrowLend.PendingAmount:N2}.");
        }

        if (!isReturnMovement)
        {
            // Extra Borrow/Lend against the same record increases the principal.
            borrowLend.TotalAmount += txn.Amount;
            borrowLend.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(borrowLend);
        }

        txn.CreatedAt = DateTime.UtcNow;
        txn.UpdatedAt = DateTime.UtcNow;
        await _txnRepo.AddAsync(txn);

        // Recompute from the full ledger rather than incrementing in place —
        // this is what keeps Records/History/Summary permanently consistent.
        await RecalculateAsync(borrowLend.Id);

        if (accountId.HasValue)
        {
            // Original record is a Lend => a return movement means money comes back (Income);
            // an extra Lend means more money goes out (Expense). Mirror logic for Borrow.
            string transactionType;
            if (borrowLend.Type == "Lend")
                transactionType = isReturnMovement ? "Income" : "Expense";
            else
                transactionType = isReturnMovement ? "Expense" : "Income";

            var createdTxn = new Transaction
            {
                AccountId = accountId.Value,
                Amount = txn.Amount,
                TransactionType = transactionType,
                TransactionDate = txn.TransactionDate,
                Description = $"{borrowLend.Type} - {txn.Type}",
                PaymentMethod = txn.PaymentMethod,
                SourceType = "BorrowLend",
                SourceReferenceId = borrowLend.Id
            };
            await _transactionService.AddTransactionAsync(createdTxn);

            txn.TransactionId = createdTxn.Id;
            txn.UpdatedAt = DateTime.UtcNow;
            await _txnRepo.UpdateAsync(txn);
        }

        return new BorrowLendResult(true);
    }

    public async Task<bool> DeleteTransactionAsync(int transactionId)
    {
        var txn = await _txnRepo.GetByIdAsync(transactionId);
        if (txn is null) return false;

        txn.IsDeleted = true;
        txn.UpdatedAt = DateTime.UtcNow;
        await _txnRepo.UpdateAsync(txn);

        await RecalculateAsync(txn.BorrowLendId);
        return true;
    }

    public async Task RecalculateAsync(int borrowLendId)
    {
        var borrowLend = await _repo.GetByIdAsync(borrowLendId);
        if (borrowLend is null) return;

        var txns = await _txnRepo.FindAsync(t => !t.IsDeleted && t.BorrowLendId == borrowLendId);

        // TotalAmount already reflects the original amount plus any extra
        // Borrow/Lend advances (grown at the time each was recorded — see
        // RecordTransactionAsync). PendingAmount is simply what's left of that
        // principal after every Return/Receive/PartialReturn recorded against it.
        var totalReturned = txns
            .Where(t => ReturnMovementTypes.Contains(t.Type))
            .Sum(t => t.Amount);

        borrowLend.PendingAmount = Math.Max(0, borrowLend.TotalAmount - totalReturned);

        if (borrowLend.PendingAmount <= 0)
        {
            borrowLend.Status = "Completed";
            borrowLend.IsClosed = true;
        }
        else if (borrowLend.DueDate.HasValue && borrowLend.DueDate.Value.Date < DateTime.UtcNow.Date)
        {
            borrowLend.Status = "Overdue";
            borrowLend.IsClosed = false;
        }
        else
        {
            borrowLend.Status = "Pending";
            borrowLend.IsClosed = false;
        }

        borrowLend.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(borrowLend);
    }
}
