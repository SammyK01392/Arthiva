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
    private string selectedFilter = "All"; // All / Income / Expense

    partial void OnSelectedFilterChanged(string value)
        => ApplyFilter();

    public List<string> FilterOptions { get; } = new() { "All", "Income", "Expense" };

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
            ApplyFilter();
        });
    }

    private void ApplyFilter()
    {
        var filtered = SelectedFilter == "All"
            ? _allTransactions
            : _allTransactions.Where(t => t.TransactionType == SelectedFilter).ToList();

        Transactions.Clear();
        foreach (var t in filtered)
            Transactions.Add(t);
    }

    [RelayCommand]
    private void SetFilter(string filter)
        => SelectedFilter = filter;

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(nameof(AddEditTransactionViewModel).Replace("ViewModel", "Page"));

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
        });
    }
}
