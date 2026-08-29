using Arthiva.Models;

namespace Arthiva.Services;

public interface IBorrowLendService
{
    /// <summary>type = "Borrow" / "Lend" / null for both</summary>
    Task<List<BorrowLend>> GetAllAsync(string? type = null, bool includeClosed = false);

    Task<BorrowLend?> GetByIdAsync(int id);

    Task<List<BorrowLendTransaction>> GetTransactionsAsync(int borrowLendId);

    /// <summary>
    /// Creates the initial Borrow/Lend record. PendingAmount is set equal to
    /// TotalAmount. If accountId is given, a linked Transaction is also created
    /// (Lend => Expense, since money leaves your account; Borrow => Income).
    /// </summary>
    Task<int> CreateAsync(BorrowLend borrowLend, int? accountId = null);

    Task<int> UpdateAsync(BorrowLend borrowLend);

    Task<int> SoftDeleteAsync(int id);

    /// <summary>
    /// Records a follow-up movement against an existing Borrow/Lend record:
    /// Return / Receive / PartialReturn reduce PendingAmount; an extra
    /// Borrow / Lend increases both PendingAmount and TotalAmount.
    /// Auto-closes the record when PendingAmount reaches zero.
    /// If accountId is given, also creates a matching linked Transaction.
    /// </summary>
    Task<int> RecordTransactionAsync(BorrowLendTransaction txn, int? accountId = null);
}
