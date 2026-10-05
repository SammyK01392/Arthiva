using MoneySpend.ViewModels;

namespace MoneySpend.Views;

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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
