using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class BudgetListPage : ContentPage
{
    private readonly BudgetListViewModel _viewModel;

    public BudgetListPage(BudgetListViewModel viewModel)
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
