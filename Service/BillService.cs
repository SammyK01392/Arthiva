using Arthiva.Models;

namespace Arthiva.Services;

public class BillService : IBillService
{
    private readonly IGenericRepository<Bill> _repo;
    private readonly IGenericRepository<BillPayment> _paymentRepo;
    private readonly ITransactionService _transactionService;

    public BillService(
        IGenericRepository<Bill> repo,
        IGenericRepository<BillPayment> paymentRepo,
        ITransactionService transactionService)
    {
        _repo = repo;
        _paymentRepo = paymentRepo;
        _transactionService = transactionService;
    }

    public async Task<List<Bill>> GetAllAsync()
    {
        var bills = await _repo.FindAsync(b => !b.IsDeleted);
        return bills.OrderBy(b => b.DueDate).ToList();
    }

    public async Task<List<Bill>> GetUpcomingAsync(int withinDays = 7)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(withinDays);
        var bills = await _repo.FindAsync(b =>
            !b.IsDeleted && b.Status != "Paid" && b.DueDate <= cutoff);
        return bills.OrderBy(b => b.DueDate).ToList();
    }

    public async Task<List<Bill>> GetOverdueAsync()
    {
        var today = DateTime.UtcNow.Date;
        var bills = await _repo.FindAsync(b =>
            !b.IsDeleted && b.Status != "Paid" && b.DueDate < today);
        return bills.OrderBy(b => b.DueDate).ToList();
    }

    public Task<Bill?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public async Task<List<BillPayment>> GetPaymentsAsync(int billId)
    {
        var payments = await _paymentRepo.FindAsync(p => !p.IsDeleted && p.BillId == billId);
        return payments.OrderByDescending(p => p.PaymentDate).ToList();
    }

    public Task<int> CreateAsync(Bill bill)
    {
        bill.CreatedAt = DateTime.UtcNow;
        bill.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(bill);
    }

    public Task<int> UpdateAsync(Bill bill)
    {
        bill.UpdatedAt = DateTime.UtcNow;
        return _repo.UpdateAsync(bill);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var bill = await _repo.GetByIdAsync(id);
        if (bill is null) return 0;

        bill.IsDeleted = true;
        bill.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(bill);
    }

    public async Task<int> RecordPaymentAsync(BillPayment payment, int? accountId = null)
    {
        var bill = await _repo.GetByIdAsync(payment.BillId);
        if (bill is null) return 0;

        payment.CreatedAt = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;
        var result = await _paymentRepo.AddAsync(payment);

        bill.LastPaymentDate = payment.PaymentDate;
        bill.Status = "Paid";

        if (bill.IsRecurring)
        {
            bill.NextDueDate = CalculateNextDueDate(bill.DueDate, bill.Frequency);
            bill.DueDate = bill.NextDueDate.Value;
            bill.Status = "Pending"; // reset for the next cycle
        }

        bill.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(bill);

        if (accountId.HasValue)
        {
            var createdTxn = new Transaction
            {
                AccountId = accountId.Value,
                Amount = payment.Amount,
                TransactionType = "Expense",
                TransactionDate = payment.PaymentDate,
                Description = $"{bill.Name} - Bill Payment",
                PaymentMethod = payment.PaymentMethod,
                SourceType = "Bill",
                SourceReferenceId = bill.Id
            };
            await _transactionService.AddTransactionAsync(createdTxn);

            payment.TransactionId = createdTxn.Id;
            payment.UpdatedAt = DateTime.UtcNow;
            await _paymentRepo.UpdateAsync(payment);
        }

        return result;
    }

    private static DateTime CalculateNextDueDate(DateTime currentDueDate, string? frequency)
        => frequency switch
        {
            "Quarterly" => currentDueDate.AddMonths(3),
            "Yearly" => currentDueDate.AddYears(1),
            _ => currentDueDate.AddMonths(1) // default: Monthly
        };
}
