using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Arthiva.Models;
using Arthiva.Services;

namespace Arthiva.ViewModels;

[QueryProperty(nameof(CategoryId), "CategoryId")]
public partial class CategoryEditViewModel : BaseViewModel
{
    private readonly ICategoryService _categoryService;

    private int _categoryId;
    private Category? _existingCategory;

    public string CategoryId
    {
        set => _categoryId = int.TryParse(value, out var id) ? id : 0;
    }

    [ObservableProperty]
    private bool isEditMode;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string type = "Expense"; // Income / Expense

    [ObservableProperty]
    private string? colorCode = "#F97316";

    [ObservableProperty]
    private string? description;

    public List<string> Types { get; } = new() { "Expense", "Income" };

    /// <summary>Quick preset swatches so the user isn't forced to type a hex code.</summary>
    public List<string> ColorPresets { get; } = new()
    {
        "#F97316", "#16A34A", "#DC2626", "#2196F3", "#9C27B0",
        "#FF9800", "#009688", "#795548", "#607D8B", "#E91E63"
    };

    public CategoryEditViewModel(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [RelayCommand]
    private async Task AppearingAsync()
    {
        if (_categoryId <= 0)
        {
            Title = "Add Category";
            IsEditMode = false;
            _existingCategory = null;
            Name = string.Empty;
            Type = "Expense";
            ColorCode = "#F97316";
            Description = null;
            ErrorMessage = null;
            return;
        }

        await ExecuteAsync(async () =>
        {
            _existingCategory = await _categoryService.GetByIdAsync(_categoryId);
            if (_existingCategory is null) return;

            Title = "Edit Category";
            IsEditMode = true;
            Name = _existingCategory.Name;
            Type = _existingCategory.Type;
            ColorCode = _existingCategory.Color ?? "#F97316";
            Description = _existingCategory.Description;
        });
    }

    [RelayCommand]
    private void SetColor(string color)
        => ColorCode = color;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Enter a category name.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            if (IsEditMode && _existingCategory is not null)
            {
                _existingCategory.Name = Name;
                _existingCategory.Type = Type;
                _existingCategory.Color = ColorCode;
                _existingCategory.Description = Description;

                await _categoryService.UpdateAsync(_existingCategory);
            }
            else
            {
                var category = new Category
                {
                    Name = Name,
                    Type = Type,
                    Color = ColorCode,
                    Description = Description
                };
                await _categoryService.CreateAsync(category);
            }

            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private static async Task CancelAsync()
        => await Shell.Current.GoToAsync("..");
}
