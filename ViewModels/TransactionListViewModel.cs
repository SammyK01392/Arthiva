using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

public partial class TransactionListViewModel : BaseViewModel
{
    private readonly ITransactionService _transactionService;

    private List<Transaction> _allTransactions = new();

    public ObservableCollection<Transaction> Transactions { get; } = new();

    [ObservableProperty]
    private string selectedFilter = "All"; // All / Income / Expense / Borrow / Lend

    // Regular income / expense totals
    [ObservableProperty] private decimal totalIncome;
    [ObservableProperty] private decimal totalExpense;

    // Borrow / Lend totals (separate)
    [ObservableProperty] private decimal borrowTotal;
    [ObservableProperty] private decimal lendTotal;

    [ObservableProperty] private int incomeCount;
    [ObservableProperty] private int expenseCount;
    [ObservableProperty] private int borrowCount;
    [ObservableProperty] private int lendCount;

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();

    public List<string> FilterOptions { get; } =
        new() { "All", "Income", "Expense", "Borrow", "Lend" };

    public TransactionListViewModel(ITransactionService transactionService)
    {
        _transactionService = transactionService;
        Title = "Transactions";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            _allTransactions = await _transactionService.GetAllAsync();
            RecalculateTotals();
            ApplyFilter();
        });
    }

    // ─────────────────────────────────────────────────────────
    //  Classification helpers — SourceType + TransactionType
    // ─────────────────────────────────────────────────────────
    //  Regular Income     : Type = "Income"  & SourceType != "BorrowLend"
    //  Regular Expense    : Type = "Expense" & SourceType != "BorrowLend"
    //  Borrow             : SourceType = "BorrowLend" & Type = "Income"   (money aa raha)
    //  Lend               : SourceType = "BorrowLend" & Type = "Expense"  (money ja raha)
    // ─────────────────────────────────────────────────────────

    private static bool IsBorrowLend(Transaction t)
        => string.Equals(t.SourceType, "BorrowLend", StringComparison.OrdinalIgnoreCase);

    private static bool IsIncome(Transaction t)
        => !IsBorrowLend(t)
        && string.Equals(t.TransactionType, "Income", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpense(Transaction t)
        => !IsBorrowLend(t)
        && string.Equals(t.TransactionType, "Expense", StringComparison.OrdinalIgnoreCase);

    private static bool IsBorrow(Transaction t)
        => IsBorrowLend(t)
        && string.Equals(t.TransactionType, "Income", StringComparison.OrdinalIgnoreCase);

    private static bool IsLend(Transaction t)
        => IsBorrowLend(t)
        && string.Equals(t.TransactionType, "Expense", StringComparison.OrdinalIgnoreCase);

    private void RecalculateTotals()
    {
        TotalIncome = _allTransactions.Where(IsIncome).Sum(t => t.Amount);
        TotalExpense = _allTransactions.Where(IsExpense).Sum(t => t.Amount);
        IncomeCount = _allTransactions.Count(IsIncome);
        ExpenseCount = _allTransactions.Count(IsExpense);

        BorrowTotal = _allTransactions.Where(IsBorrow).Sum(t => t.Amount);
        LendTotal = _allTransactions.Where(IsLend).Sum(t => t.Amount);
        BorrowCount = _allTransactions.Count(IsBorrow);
        LendCount = _allTransactions.Count(IsLend);
    }

    private void ApplyFilter()
    {
        var filtered = SelectedFilter switch
        {
            "Income" => _allTransactions.Where(IsIncome),
            "Expense" => _allTransactions.Where(IsExpense),
            "Borrow" => _allTransactions.Where(IsBorrow),
            "Lend" => _allTransactions.Where(IsLend),
            _ => _allTransactions.AsEnumerable()
        };

        Transactions.Clear();
        foreach (var t in filtered)
            Transactions.Add(t);
    }

    [RelayCommand]
    private void SetFilter(string filter) => SelectedFilter = filter;

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(
            nameof(AddEditTransactionViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task GoToEditAsync(Transaction transaction)
    {
        var route = nameof(AddEditTransactionViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?TransactionId={transaction.Id}");
    }

    [RelayCommand]
    private async Task DeleteAsync(Transaction transaction)
    {
        await ExecuteAsync(async () =>
        {
            await _transactionService.DeleteTransactionAsync(transaction.Id);
            _allTransactions.Remove(transaction);
            Transactions.Remove(transaction);
            RecalculateTotals();
        });
    }
}