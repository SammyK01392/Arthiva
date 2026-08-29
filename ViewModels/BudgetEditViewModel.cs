using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(BudgetId), "BudgetId")]
public partial class BudgetEditViewModel : BaseViewModel
{
    private readonly IBudgetService _budgetService;
    private readonly ICategoryService _categoryService;

    private int _budgetId;
    private Budget? _existingBudget;

    public string BudgetId
    {
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
                _budgetId = id;
        }
    }

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private Category? selectedCategory;

    [ObservableProperty]
    private int month = DateTime.Now.Month;

    [ObservableProperty]
    private int year = DateTime.Now.Year;

    [ObservableProperty]
    private decimal budgetAmount;

    [ObservableProperty]
    private decimal alertPercentage = 80;

    [ObservableProperty]
    private bool notificationsEnabled = true;

    [ObservableProperty]
    private string? notes;

    public ObservableCollection<Category> ExpenseCategories { get; } = new();

    public List<int> Months { get; } = Enumerable.Range(1, 12).ToList();

    public BudgetEditViewModel(IBudgetService budgetService, ICategoryService categoryService)
    {
        _budgetService = budgetService;
        _categoryService = categoryService;
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        await ExecuteAsync(async () =>
        {
            var categories = await _categoryService.GetByTypeAsync("Expense");
            ExpenseCategories.Clear();
            foreach (var c in categories)
                ExpenseCategories.Add(c);

            if (_budgetId <= 0)
            {
                Title = "Add Budget";
                IsEditMode = false;
                return;
            }

            _existingBudget = await _budgetService.GetByIdAsync(_budgetId);
            if (_existingBudget is null) return;

            Title = "Edit Budget";
            IsEditMode = true;
            SelectedCategory = ExpenseCategories.FirstOrDefault(c => c.Id == _existingBudget.CategoryId);
            Month = _existingBudget.Month;
            Year = _existingBudget.Year;
            BudgetAmount = _existingBudget.BudgetAmount;
            AlertPercentage = _existingBudget.AlertPercentage;
            NotificationsEnabled = _existingBudget.NotificationsEnabled;
            Notes = _existingBudget.Notes;
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedCategory is null)
        {
            ErrorMessage = "Select a category.";
            return;
        }

        if (BudgetAmount <= 0)
        {
            ErrorMessage = "Enter a valid budget amount.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (IsEditMode && _existingBudget is not null)
            {
                _existingBudget.CategoryId = SelectedCategory.Id;
                _existingBudget.Month = Month;
                _existingBudget.Year = Year;
                _existingBudget.BudgetAmount = BudgetAmount;
                _existingBudget.AlertPercentage = AlertPercentage;
                _existingBudget.NotificationsEnabled = NotificationsEnabled;
                _existingBudget.Notes = Notes;

                await _budgetService.UpdateAsync(_existingBudget);
            }
            else
            {
                var budget = new Budget
                {
                    CategoryId = SelectedCategory.Id,
                    Month = Month,
                    Year = Year,
                    BudgetAmount = BudgetAmount,
                    AlertPercentage = AlertPercentage,
                    NotificationsEnabled = NotificationsEnabled,
                    Notes = Notes
                };
                await _budgetService.CreateAsync(budget);
            }

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
