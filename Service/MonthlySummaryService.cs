using Arthiva.Models;

namespace Arthiva.Services;

public class MonthlySummaryService : IMonthlySummaryService
{
    private readonly IGenericRepository<MonthlySummary> _repo;

    public MonthlySummaryService(IGenericRepository<MonthlySummary> repo)
    {
        _repo = repo;
    }

    public async Task<MonthlySummary?> GetAsync(int month, int year)
    {
        var results = await _repo.FindAsync(s => s.Month == month && s.Year == year);
        return results.FirstOrDefault();
    }

    public async Task<List<MonthlySummary>> GetRecentAsync(int count = 12)
    {
        var all = await _repo.GetAllAsync();
        return all
            .OrderByDescending(s => s.Year)
            .ThenByDescending(s => s.Month)
            .Take(count)
            .ToList();
    }

    public async Task<MonthlySummary> GenerateAsync(
        int month,
        int year,
        ITransactionService transactionService,
        IEmiService emiService,
        IBillService billService,
        IBorrowLendService borrowLendService)
    {
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1).AddTicks(-1);

        var transactions = await transactionService.GetByDateRangeAsync(from, to);

        var income = transactions.Where(t => t.TransactionType == "Income").Sum(t => t.Amount);
        var expense = transactions.Where(t => t.TransactionType == "Expense").Sum(t => t.Amount);

        var emiTotal = transactions.Where(t => t.SourceType == "EMI").Sum(t => t.Amount);
        var billTotal = transactions.Where(t => t.SourceType == "Bill").Sum(t => t.Amount);

        decimal borrowTotal = 0, lendTotal = 0;
        var borrowLendTxns = transactions.Where(t => t.SourceType == "BorrowLend").ToList();
        foreach (var t in borrowLendTxns)
        {
            if (t.SourceReferenceId is null) continue;
            var record = await borrowLendService.GetByIdAsync(t.SourceReferenceId.Value);
            if (record is null) continue;

            if (record.Type == "Borrow") borrowTotal += t.Amount;
            else if (record.Type == "Lend") lendTotal += t.Amount;
        }

        var existing = await GetAsync(month, year);
        var summary = existing ?? new MonthlySummary
        {
            Month = month,
            Year = year,
            CreatedAt = DateTime.UtcNow
        };

        summary.Income = income;
        summary.Expense = expense;
        summary.Saving = income - expense;
        summary.EmiTotal = emiTotal;
        summary.BillTotal = billTotal;
        summary.BorrowTotal = borrowTotal;
        summary.LendTotal = lendTotal;
        summary.TransactionCount = transactions.Count;
        summary.ClosingBalance = income - expense;
        summary.IsGenerated = true;
        summary.UpdatedAt = DateTime.UtcNow;

        if (existing is null)
            await _repo.AddAsync(summary);
        else
            await _repo.UpdateAsync(summary);

        return summary;
    }
}
