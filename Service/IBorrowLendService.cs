using Arthiva.Models;

namespace Arthiva.Services;

/// <summary>Result of an operation that can fail validation (e.g. over-paying an outstanding balance).</summary>
public record BorrowLendResult(bool Success, string? ErrorMessage = null);

public interface IBorrowLendService
{
    /// <summary>type = "Borrow" / "Lend" / null for both</summary>
    Task<List<BorrowLend>> GetAllAsync(string? type = null, bool includeClosed = false);

    /// <summary>All Borrow/Lend records (open and closed) for one contact — used by the Contact Detail page.</summary>
    Task<List<BorrowLend>> GetByContactAsync(int contactId);

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
    /// Return / Receive / PartialReturn reduce the outstanding balance; an
    /// extra Borrow / Lend increases the total principal. PendingAmount is
    /// then recalculated from the full transaction ledger (not incrementally
    /// mutated), so Records/History/Summary always agree.
    ///
    /// Fails with an error message if a Return/Receive/PartialReturn would
    /// exceed the record's current outstanding balance — this is never
    /// silently clamped.
    /// </summary>
    Task<BorrowLendResult> RecordTransactionAsync(BorrowLendTransaction txn, int? accountId = null);

    /// <summary>
    /// Removes one historical movement (e.g. a mistakenly entered Return/Receive)
    /// and recalculates the parent record's PendingAmount/Status from what remains.
    /// Does not touch or reverse any linked account Transaction — delete that
    /// separately via ITransactionService if needed.
    /// </summary>
    Task<bool> DeleteTransactionAsync(int transactionId);

    /// <summary>
    /// Recomputes PendingAmount/Status for one record purely from its
    /// TotalAmount and the sum of its non-deleted Return/Receive/PartialReturn
    /// transactions. This is the single source of truth used everywhere
    /// (record list, contact detail summary, and history) — call after any
    /// edit to the record's transaction ledger.
    /// </summary>
    Task RecalculateAsync(int borrowLendId);
    /// <summary>
    /// Total amount others owe you — sum of PendingAmount across all open
    /// (not closed, not deleted) records where Type == "Lend".
    /// </summary>
    Task<decimal> GetTotalReceivableAsync();

    /// <summary>
    /// Total amount you owe others — sum of PendingAmount across all open
    /// (not closed, not deleted) records where Type == "Borrow".
    /// </summary>
    Task<decimal> GetTotalPayableAsync();
}
