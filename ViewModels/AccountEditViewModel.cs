using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(AccountId), "AccountId")]
public partial class AccountEditViewModel : BaseViewModel
{
    private readonly IAccountService _accountService;

    private int _accountId;
    private Account? _existingAccount;

    public string AccountId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _accountId = id;
        }
    }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string type = "Cash";

    [ObservableProperty]
    private decimal openingBalance;

    [ObservableProperty]
    private string? bankName;

    [ObservableProperty]
    private string? accountNumber;

    [ObservableProperty]
    private string? upiId;

    [ObservableProperty]
    private string? colorCode;

    [ObservableProperty]
    private bool isDefault;

    [ObservableProperty]
    private bool isEditMode;

    public List<string> AccountTypes { get; } = new() { "Cash", "Bank", "Wallet", "CreditCard" };

    public AccountEditViewModel(IAccountService accountService)
    {
        _accountService = accountService;
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        if (_accountId <= 0)
        {
            Title = "Add Account";
            IsEditMode = false;
            return;
        }

        await ExecuteAsync(async () =>
        {
            _existingAccount = await _accountService.GetByIdAsync(_accountId);
            if (_existingAccount is null) return;

            Title = "Edit Account";
            IsEditMode = true;
            Name = _existingAccount.Name;
            Type = _existingAccount.Type;
            OpeningBalance = _existingAccount.OpeningBalance;
            BankName = _existingAccount.BankName;
            AccountNumber = _existingAccount.AccountNumber;
            UpiId = _existingAccount.UpiId;
            ColorCode = _existingAccount.ColorCode;
            IsDefault = _existingAccount.IsDefault;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Account name is required.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (IsEditMode && _existingAccount is not null)
            {
                _existingAccount.Name = Name;
                _existingAccount.Type = Type;
                _existingAccount.BankName = BankName;
                _existingAccount.AccountNumber = AccountNumber;
                _existingAccount.UpiId = UpiId;
                _existingAccount.ColorCode = ColorCode;
                _existingAccount.IsDefault = IsDefault;

                await _accountService.UpdateAsync(_existingAccount);
            }
            else
            {
                var account = new Account
                {
                    Name = Name,
                    Type = Type,
                    OpeningBalance = OpeningBalance,
                    BankName = BankName,
                    AccountNumber = AccountNumber,
                    UpiId = UpiId,
                    ColorCode = ColorCode,
                    IsDefault = IsDefault
                };
                await _accountService.CreateAsync(account);
            }

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
