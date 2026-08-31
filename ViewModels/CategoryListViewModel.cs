using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

public partial class CategoryListViewModel : BaseViewModel
{
    private readonly ICategoryService _categoryService;

    public ObservableCollection<Category> IncomeCategories { get; } = new();

    public ObservableCollection<Category> ExpenseCategories { get; } = new();

    public CategoryListViewModel(ICategoryService categoryService)
    {
        _categoryService = categoryService;
        Title = "Categories";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            var income = await _categoryService.GetByTypeAsync("Income");
            IncomeCategories.Clear();
            foreach (var c in income)
                IncomeCategories.Add(c);

            var expense = await _categoryService.GetByTypeAsync("Expense");
            ExpenseCategories.Clear();
            foreach (var c in expense)
                ExpenseCategories.Add(c);
        });
    }

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(nameof(CategoryEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private async Task ToggleFavoriteAsync(Category category)
    {
        await ExecuteAsync(async () =>
        {
            await _categoryService.ToggleFavoriteAsync(category.Id);
            category.IsFavorite = !category.IsFavorite;
        });
    }

    [RelayCommand]
    private async Task DeleteAsync(Category category)
    {
        await ExecuteAsync(async () =>
        {
            await _categoryService.SoftDeleteAsync(category.Id);
            IncomeCategories.Remove(category);
            ExpenseCategories.Remove(category);
        });
    }
}
