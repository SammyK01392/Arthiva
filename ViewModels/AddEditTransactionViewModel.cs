using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(TransactionId), "TransactionId")]
public partial class AddEditTransactionViewModel : BaseViewModel
{
    private readonly ITransactionService _transactionService;
    private readonly IAccountService _accountService;
    private readonly ICategoryService _categoryService;

    private int _transactionId;
    private Transaction? _existingTransaction;

    // Guards against OnTransactionTypeChanged firing a second, overlapping
    // LoadCategoriesAsync() call while AppearingAsync is already loading them
    // explicitly — the two concurrent calls were racing and could leave the
    // Categories collection empty (Clear() from one call landing after the
    // Add() calls from the other).
    private bool _suppressCategoryAutoReload;

    public string TransactionId
    {
        // Shell resets query properties to "" (not null) when navigating to a
        // route without that parameter — e.g. the "Add" tab. Treat that as "no id".
        set => _transactionId = int.TryParse(value, out var id) ? id : 0;
    }

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private decimal amount;

    [ObservableProperty]
    private string transactionType = "Expense"; // Income / Expense

    [ObservableProperty]
    private DateTime transactionDate = DateTime.Now;

    [ObservableProperty]
    private string? description;

    [ObservableProperty]
    private string? payee;

    [ObservableProperty]
    private string? paymentMethod;

    [ObservableProperty]
    private Account? selectedAccount;

    [ObservableProperty]
    private Category? selectedCategory;

    [ObservableProperty]
    private string? successMessage;

    public ObservableCollection<Account> Accounts { get; } = new();

    public ObservableCollection<Category> Categories { get; } = new();

    public List<string> TransactionTypes { get; } = new() { "Income", "Expense" };

    public List<string> PaymentMethods { get; } = new() { "Cash", "UPI", "Bank Transfer", "Card" };

    partial void OnTransactionTypeChanged(string value)
    {
        if (_suppressCategoryAutoReload) return;
        _ = LoadCategoriesAsync();
    }

    public AddEditTransactionViewModel(
        ITransactionService transactionService,
        IAccountService accountService,
        ICategoryService categoryService)
    {
        _transactionService = transactionService;
        _accountService = accountService;
        _categoryService = categoryService;
    }

    [RelayCommand]
    private void SetType(string type)
        => TransactionType = type; // interactive toggle — auto-reload via OnTransactionTypeChanged is fine here (only one caller)

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            var accounts = await _accountService.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts)
                Accounts.Add(a);

            if (_transactionId <= 0)
            {
                // Add mode — this page instance may be reused (it's a tab root),
                // so every field must be reset here, not just the ones tied to id.
                Title = "Add Transaction";
                IsEditMode = false;
                _existingTransaction = null;
                SuccessMessage = null;
                ErrorMessage = null;

                Amount = 0;

                _suppressCategoryAutoReload = true;
                TransactionType = "Expense";
                _suppressCategoryAutoReload = false;

                TransactionDate = DateTime.Now;
                Description = null;
                Payee = null;
                PaymentMethod = null;
                SelectedAccount = Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();

                await LoadCategoriesAsync();
                SelectedCategory = null;
                return;
            }

            Title = "Edit Transaction";
            IsEditMode = true;

            _existingTransaction = await _transactionService.GetByIdAsync(_transactionId);
            if (_existingTransaction is null) return;

            Amount = _existingTransaction.Amount;

            _suppressCategoryAutoReload = true;
            TransactionType = _existingTransaction.TransactionType;
            _suppressCategoryAutoReload = false;

            TransactionDate = _existingTransaction.TransactionDate;
            Description = _existingTransaction.Description;
            Payee = _existingTransaction.Payee;
            PaymentMethod = _existingTransaction.PaymentMethod;
            SelectedAccount = Accounts.FirstOrDefault(a => a.Id == _existingTransaction.AccountId);

            await LoadCategoriesAsync();
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == _existingTransaction.CategoryId);
        });
    }

    private async Task LoadCategoriesAsync()
    {
        var categories = await _categoryService.GetByTypeAsync(TransactionType);
        Categories.Clear();
        foreach (var c in categories)
            Categories.Add(c);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SuccessMessage = null;

        if (Amount <= 0)
        {
            ErrorMessage = "Enter a valid amount.";
            return;
        }

        if (SelectedAccount is null)
        {
            ErrorMessage = "Select an account.";
            return;
        }

        if (SelectedCategory is null)
        {
            ErrorMessage = "Select a category.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (IsEditMode && _existingTransaction is not null)
            {
                _existingTransaction.Amount = Amount;
                _existingTransaction.TransactionType = TransactionType;
                _existingTransaction.TransactionDate = TransactionDate;
                _existingTransaction.Description = Description;
                _existingTransaction.Payee = Payee;
                _existingTransaction.PaymentMethod = PaymentMethod;
                _existingTransaction.AccountId = SelectedAccount.Id;
                _existingTransaction.CategoryId = SelectedCategory.Id;

                await _transactionService.UpdateTransactionAsync(_existingTransaction);

                // This instance was pushed on top of the list page — pop back to it.
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                var transaction = new Transaction
                {
                    Amount = Amount,
                    TransactionType = TransactionType,
                    TransactionDate = TransactionDate,
                    Description = Description,
                    Payee = Payee,
                    PaymentMethod = PaymentMethod,
                    AccountId = SelectedAccount.Id,
                    CategoryId = SelectedCategory.Id
                };
                await _transactionService.AddTransactionAsync(transaction);

                // This instance IS the "Add" tab root — jump to the Transactions
                // tab so the user immediately sees the new entry in the list.
                await Shell.Current.GoToAsync($"//{nameof(Views.TransactionListPage)}");
            }
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}