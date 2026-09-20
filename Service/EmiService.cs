using Arthiva.Models;

namespace Arthiva.Services;

public class EmiService : IEmiService
{
    private readonly IGenericRepository<EmiMaster> _repo;
    private readonly IGenericRepository<EmiPayment> _paymentRepo;
    private readonly ITransactionService _transactionService;

    public EmiService(
        IGenericRepository<EmiMaster> repo,
        IGenericRepository<EmiPayment> paymentRepo,
        ITransactionService transactionService)
    {
        _repo = repo;
        _paymentRepo = paymentRepo;
        _transactionService = transactionService;
    }

    public async Task<List<EmiMaster>> GetAllAsync(bool includeCompleted = false)
    {
        var all = await _repo.FindAsync(e => !e.IsDeleted);
        if (!includeCompleted)
            all = all.Where(e => e.Status != "Completed").ToList();

        return all.OrderBy(e => e.DueDay).ToList();
    }

    public Task<EmiMaster?> GetByIdAsync(int id)
        => _repo.GetByIdAsync(id);

    public async Task<List<EmiPayment>> GetPaymentsAsync(int emiId)
    {
        var payments = await _paymentRepo.FindAsync(p => p.EmiId == emiId);
        return payments.OrderByDescending(p => p.PaymentDate).ToList();
    }

    public Task<int> CreateAsync(EmiMaster emi)
    {
        emi.CreatedAt = DateTime.UtcNow;
        emi.UpdatedAt = DateTime.UtcNow;
        return _repo.AddAsync(emi);
    }

    public Task<int> UpdateAsync(EmiMaster emi)
    {
        emi.UpdatedAt = DateTime.UtcNow;
        return _repo.UpdateAsync(emi);
    }

    public async Task<int> SoftDeleteAsync(int id)
    {
        var emi = await _repo.GetByIdAsync(id);
        if (emi is null) return 0;

        emi.IsDeleted = true;
        emi.UpdatedAt = DateTime.UtcNow;
        return await _repo.UpdateAsync(emi);
    }

    public async Task<int> RecordPaymentAsync(EmiPayment payment)
    {
        var emi = await _repo.GetByIdAsync(payment.EmiId);
        if (emi is null) return 0;

        if (payment.InstallmentNo <= 0)
            payment.InstallmentNo = emi.PaidInstallment + 1;

        payment.CreatedAt = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;
        var result = await _paymentRepo.AddAsync(payment);

        emi.PaidInstallment++;
        if (emi.PaidInstallment >= emi.TotalInstallment)
            emi.Status = "Completed";

        emi.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(emi);

        if (emi.AccountId.HasValue)
        {
            var createdTxn = new Transaction
            {
                AccountId = emi.AccountId.Value,
                Amount = payment.Amount,
                TransactionType = "Expense",
                TransactionDate = payment.PaymentDate,
                Description = $"{emi.Title} - Installment #{payment.InstallmentNo}",
                SourceType = "EMI",
                SourceReferenceId = emi.Id
            };
            await _transactionService.AddTransactionAsync(createdTxn);

            payment.TransactionId = createdTxn.Id;
            payment.UpdatedAt = DateTime.UtcNow;
            await _paymentRepo.UpdateAsync(payment);
        }

        return result;
    }

    public async Task<List<EmiInstallmentScheduleItem>> GetScheduleAsync(int emiId)
    {
        var emi = await _repo.GetByIdAsync(emiId);
        if (emi is null) return new List<EmiInstallmentScheduleItem>();

        var payments = await _paymentRepo.FindAsync(p => p.EmiId == emiId);
        // Map installment number -> payment (falls back to paid order if InstallmentNo wasn't set)
        var paymentsByInstallment = payments
            .OrderBy(p => p.PaymentDate)
            .Select((p, idx) => new { Payment = p, InstallmentNo = p.InstallmentNo > 0 ? p.InstallmentNo : idx + 1 })
            .ToDictionary(x => x.InstallmentNo, x => x.Payment);

        var today = DateTime.Today;
        var schedule = new List<EmiInstallmentScheduleItem>();

        for (int i = 1; i <= emi.TotalInstallment; i++)
        {
            var dueDate = GetDueDate(emi.StartDate, emi.DueDay, i);
            paymentsByInstallment.TryGetValue(i, out var payment);

            var item = new EmiInstallmentScheduleItem
            {
                InstallmentNo = i,
                DueDate = dueDate,
                ExpectedAmount = emi.EmiAmount,
            };

            if (payment is not null)
            {
                item.Status = "Paid";
                item.PaidAmount = payment.Amount;
                item.PaymentDate = payment.PaymentDate;
                item.PaymentId = payment.Id;
            }
            else
            {
                item.Status = dueDate.Date < today ? "Overdue" : "Pending";
            }

            schedule.Add(item);
        }

        return schedule;
    }

    /// <summary>
    /// Due date for installment N = StartDate's month + (N-1) months, with the
    /// day set to DueDay — clamped to the last day of that month (e.g. DueDay=31
    /// in February becomes the 28th/29th).
    /// </summary>
    private static DateTime GetDueDate(DateTime startDate, int dueDay, int installmentNo)
    {
        var target = startDate.AddMonths(installmentNo - 1);
        var lastDayOfMonth = DateTime.DaysInMonth(target.Year, target.Month);
        var day = Math.Min(dueDay, lastDayOfMonth);
        return new DateTime(target.Year, target.Month, day);
    }

    public async Task<EmiPayment?> GetPaymentByIdAsync(int paymentId)
    => await _paymentRepo.GetByIdAsync(paymentId);

    public async Task<decimal> GetPaidAmountAsync(int emiId)
    {
        var payments = await _paymentRepo.FindAsync(p => p.EmiId == emiId);
        return payments.Sum(p => p.Amount);
    }

    public async Task<int> UpdatePaymentAsync(EmiPayment payment)
    {
        var existing = await _paymentRepo.GetByIdAsync(payment.Id);
        if (existing is null) return 0;

        existing.Amount = payment.Amount;
        existing.PaymentDate = payment.PaymentDate;
        existing.PenaltyAmount = payment.PenaltyAmount;
        existing.Remarks = payment.Remarks;
        existing.UpdatedAt = DateTime.UtcNow;

        var result = await _paymentRepo.UpdateAsync(existing);

        if (existing.TransactionId.HasValue)
        {
            var txn = await _transactionService.GetByIdAsync(existing.TransactionId.Value);
            if (txn is not null)
            {
                txn.Amount = existing.Amount;
                txn.TransactionDate = existing.PaymentDate;
                await _transactionService.UpdateTransactionAsync(txn);
            }
        }

        return result;
    }

    public async Task<int> DeletePaymentAsync(int paymentId)
    {
        var payment = await _paymentRepo.GetByIdAsync(paymentId);
        if (payment is null) return 0;

        var emi = await _repo.GetByIdAsync(payment.EmiId);

        if (payment.TransactionId.HasValue)
            await _transactionService.DeleteTransactionAsync(payment.TransactionId.Value);

        var result = await _paymentRepo.DeleteAsync(payment); // <-- pass the entity, not payment.Id

        if (emi is not null)
        {
            emi.PaidInstallment = Math.Max(0, emi.PaidInstallment - 1);
            if (emi.Status == "Completed" && emi.PaidInstallment < emi.TotalInstallment)
                emi.Status = "Active";

            emi.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(emi);
        }

        return result;
    }
}
