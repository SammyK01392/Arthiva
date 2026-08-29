using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

/// <summary>
/// Display wrapper combining a Budget with its category's name, so the list
/// page can bind CategoryName directly instead of calling a lookup method
/// from XAML (which isn't supported by data binding).
/// </summary>
public class BudgetListItem
{
    public Budget Budget { get; init; } = null!;
    public string CategoryName { get; init; } = "Unknown";
}

public partial class BudgetListViewModel : BaseViewModel
{
    private readonly IBudgetService _budgetService;
    private readonly ICategoryService _categoryService;
    private readonly ITransactionService _transactionService;

    public ObservableCollection<BudgetListItem> Budgets { get; } = new();

    [ObservableProperty]
    private int month = DateTime.Now.Month;

    [ObservableProperty]
    private int year = DateTime.Now.Year;

    public string MonthYearLabel => new DateTime(Year, Month, 1).ToString("MMMM yyyy");

    public BudgetListViewModel(
        IBudgetService budgetService,
        ICategoryService categoryService,
        ITransactionService transactionService)
    {
        _budgetService = budgetService;
        _categoryService = categoryService;
        _transactionService = transactionService;
        Title = "Budgets";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var categories = await _categoryService.GetAllAsync();
            var categoryLookup = categories.ToDictionary(c => c.Id, c => c);

            var budgets = await _budgetService.GetByMonthAsync(Month, Year);

            // Keep SpentAmount fresh against actual transactions before displaying.
            foreach (var budget in budgets)
                await _budgetService.RecalculateSpentAsync(budget.Id, _transactionService);

            Budgets.Clear();
            foreach (var b in budgets)
            {
                var name = categoryLookup.TryGetValue(b.CategoryId, out var category) ? category.Name : "Unknown";
                Budgets.Add(new BudgetListItem { Budget = b, CategoryName = name });
            }

            OnPropertyChanged(nameof(MonthYearLabel));
        });
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        var date = new DateTime(Year, Month, 1).AddMonths(-1);
        Month = date.Month;
        Year = date.Year;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        var date = new DateTime(Year, Month, 1).AddMonths(1);
        Month = date.Month;
        Year = date.Year;
        await LoadAsync();
    }

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(nameof(BudgetEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task GoToEditAsync(BudgetListItem item)
    {
        var route = nameof(BudgetEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?BudgetId={item.Budget.Id}");
    }

    [RelayCommand]
    private async Task DeleteAsync(BudgetListItem item)
    {
        await ExecuteAsync(async () =>
        {
            await _budgetService.SoftDeleteAsync(item.Budget.Id);
            Budgets.Remove(item);
        });
    }
}