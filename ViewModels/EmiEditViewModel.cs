using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(EmiId), "EmiId")]
public partial class EmiEditViewModel : BaseViewModel
{
    private readonly IEmiService _emiService;
    private readonly IAccountService _accountService;

    private int _emiId;
    private EmiMaster? _existingEmi;

    public string EmiId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _emiId = id;
        }
    }

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private string emiTitle = string.Empty;

    [ObservableProperty]
    private string? loanProvider;

    [ObservableProperty]
    private decimal totalAmount;

    [ObservableProperty]
    private decimal emiAmount;

    [ObservableProperty]
    private decimal interestRate;

    [ObservableProperty]
    private int totalInstallment;

    [ObservableProperty]
    private int dueDay = 5;

    [ObservableProperty]
    private DateTime startDate = DateTime.Now;

    [ObservableProperty]
    private DateTime? endDate;

    [ObservableProperty]
    private string? loanReferenceNo;

    [ObservableProperty]
    private int reminderBeforeDays = 3;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private Account? selectedAccount;

    public ObservableCollection<Account> Accounts { get; } = new();

    public EmiEditViewModel(IEmiService emiService, IAccountService accountService)
    {
        _emiService = emiService;
        _accountService = accountService;
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            var accounts = await _accountService.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts)
                Accounts.Add(a);

            if (_emiId <= 0)
            {
                Title = "Add EMI";
                IsEditMode = false;
                SelectedAccount = Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();
                return;
            }

            _existingEmi = await _emiService.GetByIdAsync(_emiId);
            if (_existingEmi is null) return;

            Title = "Edit EMI";
            IsEditMode = true;
            EmiTitle = _existingEmi.Title;
            LoanProvider = _existingEmi.LoanProvider;
            TotalAmount = _existingEmi.TotalAmount;
            EmiAmount = _existingEmi.EmiAmount;
            InterestRate = _existingEmi.InterestRate;
            TotalInstallment = _existingEmi.TotalInstallment;
            DueDay = _existingEmi.DueDay;
            StartDate = _existingEmi.StartDate;
            EndDate = _existingEmi.EndDate;
            LoanReferenceNo = _existingEmi.LoanReferenceNo;
            ReminderBeforeDays = _existingEmi.ReminderBeforeDays;
            Notes = _existingEmi.Notes;
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == _existingEmi.AccountId);
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EmiTitle) || EmiAmount <= 0 || TotalInstallment <= 0)
        {
            ErrorMessage = "Enter title, EMI amount and total installments.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (IsEditMode && _existingEmi is not null)
            {
                _existingEmi.Title = EmiTitle;
                _existingEmi.LoanProvider = LoanProvider;
                _existingEmi.TotalAmount = TotalAmount;
                _existingEmi.EmiAmount = EmiAmount;
                _existingEmi.InterestRate = InterestRate;
                _existingEmi.TotalInstallment = TotalInstallment;
                _existingEmi.DueDay = DueDay;
                _existingEmi.StartDate = StartDate;
                _existingEmi.EndDate = EndDate;
                _existingEmi.LoanReferenceNo = LoanReferenceNo;
                _existingEmi.ReminderBeforeDays = ReminderBeforeDays;
                _existingEmi.Notes = Notes;
                _existingEmi.AccountId = SelectedAccount?.Id;

                await _emiService.UpdateAsync(_existingEmi);
            }
            else
            {
                var emi = new EmiMaster
                {
                    Title = EmiTitle,
                    LoanProvider = LoanProvider,
                    TotalAmount = TotalAmount,
                    EmiAmount = EmiAmount,
                    InterestRate = InterestRate,
                    TotalInstallment = TotalInstallment,
                    DueDay = DueDay,
                    StartDate = StartDate,
                    EndDate = EndDate,
                    LoanReferenceNo = LoanReferenceNo,
                    ReminderBeforeDays = ReminderBeforeDays,
                    Notes = Notes,
                    AccountId = SelectedAccount?.Id
                };
                await _emiService.CreateAsync(emi);
            }

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
