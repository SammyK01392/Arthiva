using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(BillId), "BillId")]
public partial class BillEditViewModel : BaseViewModel
{
    private readonly IBillService _billService;

    private int _billId;
    private Bill? _existingBill;

    public string BillId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _billId = id;
        }
    }

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? provider;

    [ObservableProperty]
    private string type = "Electricity";

    [ObservableProperty]
    private decimal amount;

    [ObservableProperty]
    private DateTime dueDate = DateTime.Now.AddDays(7);

    [ObservableProperty]
    private int reminderDays = 3;

    [ObservableProperty]
    private bool isRecurring;

    [ObservableProperty]
    private string frequency = "Monthly";

    [ObservableProperty]
    private bool reminderEnabled = true;

    [ObservableProperty]
    private string? notes;

    public List<string> BillTypes { get; } = new()
        { "Electricity", "Mobile Recharge", "Internet", "Rent", "Water", "Gas", "DTH", "Other" };

    public List<string> Frequencies { get; } = new() { "Monthly", "Quarterly", "Yearly" };

    public BillEditViewModel(IBillService billService)
    {
        _billService = billService;
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        if (_billId <= 0)
        {
            Title = "Add Bill";
            IsEditMode = false;
            return;
        }

        await ExecuteAsync(async () =>
        {
            _existingBill = await _billService.GetByIdAsync(_billId);
            if (_existingBill is null) return;

            Title = "Edit Bill";
            IsEditMode = true;
            Name = _existingBill.Name;
            Provider = _existingBill.Provider;
            Type = _existingBill.Type;
            Amount = _existingBill.Amount;
            DueDate = _existingBill.DueDate;
            ReminderDays = _existingBill.ReminderDays;
            IsRecurring = _existingBill.IsRecurring;
            Frequency = _existingBill.Frequency ?? "Monthly";
            ReminderEnabled = _existingBill.ReminderEnabled;
            Notes = _existingBill.Notes;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || Amount <= 0)
        {
            ErrorMessage = "Enter a bill name and a valid amount.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (IsEditMode && _existingBill is not null)
            {
                _existingBill.Name = Name;
                _existingBill.Provider = Provider;
                _existingBill.Type = Type;
                _existingBill.Amount = Amount;
                _existingBill.DueDate = DueDate;
                _existingBill.ReminderDays = ReminderDays;
                _existingBill.IsRecurring = IsRecurring;
                _existingBill.Frequency = IsRecurring ? Frequency : null;
                _existingBill.ReminderEnabled = ReminderEnabled;
                _existingBill.Notes = Notes;

                await _billService.UpdateAsync(_existingBill);
            }
            else
            {
                var bill = new Bill
                {
                    Name = Name,
                    Provider = Provider,
                    Type = Type,
                    Amount = Amount,
                    DueDate = DueDate,
                    ReminderDays = ReminderDays,
                    IsRecurring = IsRecurring,
                    Frequency = IsRecurring ? Frequency : null,
                    ReminderEnabled = ReminderEnabled,
                    Notes = Notes
                };
                await _billService.CreateAsync(bill);
            }

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
