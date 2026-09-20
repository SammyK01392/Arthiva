namespace Arthiva.Models;

public class EmiInstallmentScheduleItem
{
    public int InstallmentNo { get; set; }

    public DateTime DueDate { get; set; }

    /// <summary>Expected EMI amount for this installment (EmiMaster.EmiAmount at schedule time).</summary>
    public decimal ExpectedAmount { get; set; }

    /// <summary>Paid, Pending, Overdue</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Set when Status == Paid</summary>
    public decimal? PaidAmount { get; set; }

    public DateTime? PaymentDate { get; set; }

    public int? PaymentId { get; set; }
}