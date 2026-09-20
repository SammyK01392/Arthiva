using Arthiva.Models;
using Arthiva.Services;
using Arthiva.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(EmiId), "EmiId")]
public partial class EmiDetailViewModel : BaseViewModel
{
    private readonly IEmiService _emiService;
    private int _emiId;

    public string EmiId
    {
        set { if (int.TryParse(value, out var id)) _emiId = id; }
    }

    [ObservableProperty] private EmiMaster? emi;
    [ObservableProperty] private decimal paidAmount;
    [ObservableProperty] private decimal pendingAmount;
    [ObservableProperty] private int overdueCount;
    [ObservableProperty] private string overdueText = string.Empty;

    public ObservableCollection<EmiPayment> Payments { get; } = new();
    public ObservableCollection<EmiInstallmentScheduleItem> Schedule { get; } = new();

    public EmiDetailViewModel(IEmiService emiService)
    {
        _emiService = emiService;
        Title = "EMI Details";
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            Emi = await _emiService.GetByIdAsync(_emiId);
            if (Emi is null) return;

            var payments = await _emiService.GetPaymentsAsync(_emiId);
            Payments.Clear();
            foreach (var p in payments) Payments.Add(p);

            PaidAmount = payments.Sum(p => p.Amount);
            PendingAmount = Emi.TotalAmount - PaidAmount;

            var schedule = await _emiService.GetScheduleAsync(_emiId);
            Schedule.Clear();
            foreach (var s in schedule) Schedule.Add(s);

            OverdueCount = schedule.Count(s => s.Status == "Overdue");
     
            OverdueText = $"{OverdueCount} installment(s) overdue";
        });
    }

    [RelayCommand]
    private async Task PayAsync()
        => await Shell.Current.GoToAsync($"{nameof(RecordEmiPaymentPage)}?EmiId={_emiId}");

    [RelayCommand]
    private async Task EditEmiAsync()
        => await Shell.Current.GoToAsync($"{nameof(EmiEditPage)}?EmiId={_emiId}");

    [RelayCommand]
    private async Task EditPaymentAsync(EmiPayment payment)
        => await Shell.Current.GoToAsync($"{nameof(RecordEmiPaymentPage)}?EmiId={_emiId}&PaymentId={payment.Id}");

    [RelayCommand]
    private async Task DeletePaymentAsync(EmiPayment payment)
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Delete Payment", "Remove this payment record? This cannot be undone.", "Delete", "Cancel");
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            await _emiService.DeletePaymentAsync(payment.Id);
            await AppearingAsync(); // refresh totals, status, and schedule
        });
    }

    /// <summary>Lets the user tap a Pending/Overdue schedule row to pay it directly.</summary>
    [RelayCommand]
    private async Task PayInstallmentAsync(EmiInstallmentScheduleItem item)
    {
        if (item.Status == "Paid") return;
        await Shell.Current.GoToAsync($"{nameof(RecordEmiPaymentPage)}?EmiId={_emiId}");
    }
}