using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class CategoryListPage : ContentPage
{
    private readonly CategoryListViewModel _viewModel;

    public CategoryListPage(CategoryListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
