using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class AccountTransactionsPage : ContentPage
{
    private readonly AccountTransactionsViewModel _viewModel;

    public AccountTransactionsPage(AccountTransactionsViewModel viewModel)
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