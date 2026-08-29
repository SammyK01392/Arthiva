using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class BudgetEditPage : ContentPage
{
    private readonly BudgetEditViewModel _viewModel;

    public BudgetEditPage(BudgetEditViewModel viewModel)
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
