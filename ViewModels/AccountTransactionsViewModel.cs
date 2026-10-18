using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

[QueryProperty(nameof(AccountId), "AccountId")]
public partial class AccountTransactionsViewModel : BaseViewModel
{
    private readonly ITransactionService _transactionService;
    private readonly IAccountService _accountService;
    private readonly AutoRefresh _autoRefresh;

    private int _accountId;

    public string AccountId
    {
        // Shell kabhi "" set karta hai, isliye TryParse
        set => _accountId = int.TryParse(value, out var id) ? id : 0;
    }

    public ObservableCollection<Transaction> Transactions { get; } = new();

    [ObservableProperty] private Account? account;
    [ObservableProperty] private decimal totalIncome;
    [ObservableProperty] private decimal totalExpense;

    public AccountTransactionsViewModel(
        ITransactionService transactionService,
        IAccountService accountService)
    {
        _transactionService = transactionService;
        _accountService = accountService;
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    private async Task ReloadAsync()
    {
        try
        {
            if (_accountId <= 0) return;

            Account = await _accountService.GetByIdAsync(_accountId);
            Title = Account?.Name ?? "Account";

            var all = await _transactionService.GetAllAsync();
            var mine = all
                .Where(t => t.AccountId == _accountId)
                .OrderByDescending(t => t.TransactionDate)
                .ToList();

            TotalIncome = mine.Where(t => t.IsIncome).Sum(t => t.Amount);
            TotalExpense = mine.Where(t => t.IsExpense).Sum(t => t.Amount);

            Transactions.Clear();
            foreach (var t in mine)
                Transactions.Add(t);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(ReloadAsync);
        _autoRefresh.Enabled = true;
    }

    [RelayCommand]
    private async Task EditAccountAsync()
    {
        var route = nameof(AccountEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?AccountId={_accountId}");
    }

    [RelayCommand]
    private static async Task GoToEditTransactionAsync(Transaction transaction)
    {
        var route = nameof(AddEditTransactionViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?TransactionId={transaction.Id}");
    }
}