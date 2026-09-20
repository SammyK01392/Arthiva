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
    Task<int> RecordPaymentAsync(EmiPayment payment);
    Task<EmiPayment?> GetPaymentByIdAsync(int paymentId);
    Task<int> UpdatePaymentAsync(EmiPayment payment);
    Task<int> DeletePaymentAsync(int paymentId);
    Task<decimal> GetPaidAmountAsync(int emiId);

    /// <summary>
    /// Builds the month-by-month installment schedule for an EMI,
    /// matching recorded payments to installment numbers and marking
    /// unpaid past-due installments as Overdue.
    /// </summary>
    Task<List<EmiInstallmentScheduleItem>> GetScheduleAsync(int emiId);
}