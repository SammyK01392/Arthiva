using Arthiva.Models;

namespace Arthiva.Services;

public interface IBillService
{
    Task<List<Bill>> GetAllAsync();

    Task<List<Bill>> GetUpcomingAsync(int withinDays = 7);

    Task<List<Bill>> GetOverdueAsync();

    Task<Bill?> GetByIdAsync(int id);

    Task<List<BillPayment>> GetPaymentsAsync(int billId);

    Task<int> CreateAsync(Bill bill);

    Task<int> UpdateAsync(Bill bill);

    Task<int> SoftDeleteAsync(int id);

    /// <summary>
    /// Records a bill payment, marks the bill Paid, rolls NextDueDate forward
    /// for recurring bills, and — if accountId is given — creates a matching
    /// Expense Transaction.
    /// </summary>
    Task<int> RecordPaymentAsync(BillPayment payment, int? accountId = null);
}
