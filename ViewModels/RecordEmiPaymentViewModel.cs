using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(EmiId), "EmiId")]
public partial class RecordEmiPaymentViewModel : BaseViewModel
{
    private readonly IEmiService _emiService;

    private int _emiId;

    public string EmiId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _emiId = id;
        }
    }

    [ObservableProperty]
    private EmiMaster? emi;

    [ObservableProperty]
    private decimal amount;

    [ObservableProperty]
    private DateTime paymentDate = DateTime.Now;

    [ObservableProperty]
    private decimal penaltyAmount;

    [ObservableProperty]
    private string? remarks;

    public RecordEmiPaymentViewModel(IEmiService emiService)
    {
        _emiService = emiService;
        Title = "Record EMI Payment";
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            Emi = await _emiService.GetByIdAsync(_emiId);
            if (Emi is not null)
                Amount = Emi.EmiAmount;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Emi is null || Amount <= 0)
        {
            ErrorMessage = "Enter a valid payment amount.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var payment = new EmiPayment
            {
                EmiId = Emi.Id,
                Amount = Amount,
                PaymentDate = PaymentDate,
                DueDate = Emi.StartDate.AddMonths(Emi.PaidInstallment),
                PenaltyAmount = PenaltyAmount,
                Remarks = Remarks
            };

            await _emiService.RecordPaymentAsync(payment);
            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
