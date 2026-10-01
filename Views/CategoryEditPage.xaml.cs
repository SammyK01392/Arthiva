using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class CategoryEditPage : ContentPage
{
    private readonly CategoryEditViewModel _viewModel;

    public CategoryEditPage(CategoryEditViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.AppearingCommand.Execute(null);
    }
}
