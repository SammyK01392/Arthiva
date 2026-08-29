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

    public string TransactionId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _transactionId = id;
        }
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

    public ObservableCollection<Account> Accounts { get; } = new();

    public ObservableCollection<Category> Categories { get; } = new();

    public List<string> TransactionTypes { get; } = new() { "Income", "Expense" };

    public List<string> PaymentMethods { get; } = new() { "Cash", "UPI", "Bank Transfer", "Card" };

    partial void OnTransactionTypeChanged(string value)
        => _ = LoadCategoriesAsync();

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
        => TransactionType = type;

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            var accounts = await _accountService.GetAllAsync();
            Accounts.Clear();
            foreach (var a in accounts)
                Accounts.Add(a);

            await LoadCategoriesAsync();

            if (_transactionId <= 0)
            {
                Title = "Add Transaction";
                IsEditMode = false;
                SelectedAccount = Accounts.FirstOrDefault(a => a.IsDefault) ?? Accounts.FirstOrDefault();
                return;
            }

            Title = "Edit Transaction";
            IsEditMode = true;

            _existingTransaction = await _transactionService.GetByIdAsync(_transactionId);
            if (_existingTransaction is null) return;

            Amount = _existingTransaction.Amount;
            TransactionType = _existingTransaction.TransactionType;
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
            }

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
