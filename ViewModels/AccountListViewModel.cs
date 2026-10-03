using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class AccountListViewModel : BaseViewModel
{
    private readonly IAccountService _accountService;

    // CHANGED: auto-refresh field
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<Account> Accounts { get; } = new();

    public AccountListViewModel(IAccountService accountService)
    {
        _accountService = accountService;
        Title = "Accounts";

        // CHANGED
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    // CHANGED: asli load logic yahan (silent, spinner nahi)
    private async Task ReloadAsync()
    {
        var accounts = await _accountService.GetAllAsync();
        Accounts.Clear();
        foreach (var a in accounts)
            Accounts.Add(a);
    }

    // CHANGED: command ab ReloadAsync use karta hai
    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(ReloadAsync);
        _autoRefresh.Enabled = true;
    }

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(nameof(AccountEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task GoToEditAsync(Account account)
    {
        var route = nameof(AccountEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?AccountId={account.Id}");
    }

    [RelayCommand]
    private async Task SetDefaultAsync(Account account)
    {
        await ExecuteAsync(async () =>
        {
            await _accountService.SetDefaultAsync(account.Id);
            // CHANGED: nested LoadAsync hata diya (IsBusy ki wajah se skip hota tha).
            // Repository ka publish list ko khud refresh kar dega.
        });
    }

    [RelayCommand]
    private async Task DeleteAsync(Account account)
    {
        await ExecuteAsync(async () =>
        {
            await _accountService.SoftDeleteAsync(account.Id);
            Accounts.Remove(account);
        });
    }
}