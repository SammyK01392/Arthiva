using Arthiva.Models;

namespace Arthiva.Services;

public interface IEmiService
{
    Task<List<EmiMaster>> GetAllAsync(bool includeCompleted = false);

    Task<EmiMaster?> GetByIdAsync(int id);

    Task<List<EmiPayment>> GetPaymentsAsync(int emiId);

    Task<int> CreateAsync(EmiMaster emi);

    Task<int> UpdateAsync(EmiMaster emi);

    Task<int> SoftDeleteAsync(int id);

    /// <summary>
    /// Records an EMI installment payment: increments PaidInstallment, marks
    /// the EMI Completed when fully paid, and — if the EMI has a linked
    /// AccountId — creates a matching Expense Transaction.
    /// </summary>
    Task<int> RecordPaymentAsync(EmiPayment payment);
}
