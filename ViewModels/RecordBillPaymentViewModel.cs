using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(BillId), "BillId")]
public partial class RecordBillPaymentViewModel : BaseViewModel
{
    private readonly IBillService _billService;
    private readonly IAccountService _accountService;

    private int _billId;

    public string BillId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _billId = id;
        }
    }

    [ObservableProperty]
    private Bill? bill;

    [ObservableProperty]
    private decimal amount;

    [ObservableProperty]
    private DateTime paymentDate = DateTime.Now;

    [ObservableProperty]
    private string? paymentMethod = "UPI";

    [ObservableProperty]
    private string? referenceNo;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private Account? selectedAccount;

    public ObservableCollection<Account> Accounts { get; } = new();

    public List<string> PaymentMethods { get; } = new() { "Cash", "UPI", "Bank Transfer", "Card" };

    public RecordBillPaymentViewModel(IBillService billService, IAccountService accountService)
    {
        _billService = billService;
        _accountService = accountService;
        Title = "Record Payment";
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            Bill = await _billService.GetByIdAsync(_billId);
            if (Bill is not null)
                Amount = Bill.Amount;

            var accounts = await _accountService.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts)
                Accounts.Add(a);

            SelectedAccount = Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Bill is null || Amount <= 0)
        {
            ErrorMessage = "Enter a valid payment amount.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var payment = new BillPayment
            {
                BillId = Bill.Id,
                Amount = Amount,
                PaymentDate = PaymentDate,
                PaymentMethod = PaymentMethod,
                ReferenceNo = ReferenceNo,
                Notes = Notes
            };

            await _billService.RecordPaymentAsync(payment, SelectedAccount?.Id);
            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
