using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

[QueryProperty(nameof(TransactionId), "TransactionId")]
[QueryProperty(nameof(RequestedType), "Type")]
public partial class AddEditTransactionViewModel : BaseViewModel
{
    private readonly ITransactionService _transactionService;
    private readonly IAccountService _accountService;
    private readonly ICategoryService _categoryService;

    private int _transactionId;
    private Transaction? _existingTransaction;

    /// <summary>
    /// NEW: Page isko set karta hai (OnAppearing me). Save kamyab hone ke baad
    /// VM isko await karta hai — popup band hone ke baad hi navigation hota hai.
    /// Null ho to popup skip ho jata hai.
    /// </summary>
    public Func<decimal, Task>? ShowSuccessAsync { get; set; }

    // Set by Quick Actions (Dashboard) / the Transactions speed-dial when they
    // navigate straight to "Add Income" or "Add Expense" instead of the
    // generic Add tab. Same Shell quirk as TransactionId below: Shell resets
    // this to "" (not null) when navigating to the tab without a "Type"
    // query param, so a plain tab tap still falls back to the "Expense" default.
    private string? _requestedType;

    public string RequestedType
    {
        set => _requestedType = value is "Income" or "Expense" ? value : null;
    }

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

    // Ab UI me use nahi hota (popup ne le liya). Kisi aur jagah bind na ho to delete kar sakte ho.
    [ObservableProperty]
    private string? successMessage;

    public ObservableCollection<Account> Accounts { get; } = new();

    public ObservableCollection<Category> Categories { get; } = new();

    public List<string> TransactionTypes { get; } = new() { "Income", "Expense" };

    public List<string> PaymentMethods { get; } = new() { "Cash", "UPI", "Bank Transfer", "Card" };

    partial void OnTransactionTypeChanged(string value)
    {
        if (_suppressCategoryAutoReload) return;
        _ = SafeLoadCategoriesAsync();
    }

    private async Task SafeLoadCategoriesAsync()
    {
        try
        {
            await LoadCategoriesAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
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
                TransactionType = _requestedType ?? "Expense";
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

        var wasEdit = IsEditMode && _existingTransaction is not null;
        var savedAmount = Amount;
        var saved = false;

        // STEP 1: sirf save. IsBusy tab tak true rehta hai jab tak DB write poora na ho.
        await ExecuteAsync(async () =>
        {
            if (wasEdit)
            {
                _existingTransaction!.Amount = Amount;
                _existingTransaction.TransactionType = TransactionType;
                _existingTransaction.TransactionDate = TransactionDate;
                _existingTransaction.Description = Description;
                _existingTransaction.Payee = Payee;
                _existingTransaction.PaymentMethod = PaymentMethod;
                _existingTransaction.AccountId = SelectedAccount.Id;
                _existingTransaction.CategoryId = SelectedCategory.Id;

                // DB write + balance adjust poora await hone ke baad hi aage badhega
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

            saved = true;
        });

        // Save fail hua (ExecuteAsync ne ErrorMessage set kar diya) -> yahin ruk jao
        if (!saved) return;

        // STEP 2: success popup — popup band hone tak wait. Fail ho to bhi navigation na ruke.
        try
        {
            if (ShowSuccessAsync is not null)
                await ShowSuccessAsync(savedAmount);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Success popup failed: {ex}");
        }

        // STEP 3: navigation (popup ke baad)
        if (wasEdit)
        {
            // This instance was pushed on top of the list page — pop back to it.
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            // Pushed on top of whichever tab we came from — jump to the
            // Transactions tab (resets the nav stack) so the user lands
            // straight on the list and immediately sees the new entry.
            if (Shell.Current.Navigation.NavigationStack.Count > 1)
                await Shell.Current.Navigation.PopToRootAsync(false);
            await Shell.Current.GoToAsync($"//{nameof(Views.TransactionListPage)}");
        }
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}