using Arthiva.Models;

namespace Arthiva.Services;

public interface IMonthlySummaryService
{
    Task<MonthlySummary?> GetAsync(int month, int year);

    Task<List<MonthlySummary>> GetRecentAsync(int count = 12);

    /// <summary>
    /// Aggregates Income/Expense/EMI/Bill/Borrow/Lend totals for the given
    /// month from the other services and creates or updates the
    /// MonthlySummary row for that period.
    /// </summary>
    Task<MonthlySummary> GenerateAsync(
        int month,
        int year,
        ITransactionService transactionService,
        IEmiService emiService,
        IBillService billService,
        IBorrowLendService borrowLendService);
}
