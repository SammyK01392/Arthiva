using SQLite;

namespace MoneySpend.Models;

/// <summary>
/// One friend's portion of a SplitExpense. The money they owe lives in the
/// linked BorrowLend record (PendingAmount / settle history), so there is no
/// second ledger to keep in sync.
/// </summary>
public class SplitShare
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int SplitExpenseId { get; set; }

    [Indexed]
    public int ContactId { get; set; }

    public decimal ShareAmount { get; set; }

    [Indexed]
    public int BorrowLendId { get; set; }
}
