using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

public partial class AccountListViewModel : BaseViewModel
{
    private readonly IAccountService _accountService;

    public ObservableCollection<Account> Accounts { get; } = new();

    public AccountListViewModel(IAccountService accountService)
    {
        _accountService = accountService;
        Title = "Accounts";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var accounts = await _accountService.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts)
                Accounts.Add(a);
        });
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
            await LoadAsync();
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
