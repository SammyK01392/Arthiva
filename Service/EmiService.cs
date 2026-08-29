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
}
