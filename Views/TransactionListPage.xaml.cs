using Arthiva.ViewModels;

namespace Arthiva.Views;

// Pure history — just shows transactions. Adding a transaction happens from
// the Dashboard's Quick Actions (Income/Expense), which jump straight to
// AddEditTransactionPage pre-set to the right type. This page intentionally
// has no add button of its own.
public partial class TransactionListPage : ContentPage
{
    private readonly TransactionListViewModel _viewModel;

    public TransactionListPage(TransactionListViewModel viewModel)
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
