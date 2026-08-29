using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class AccountListPage : ContentPage
{
    private readonly AccountListViewModel _viewModel;

    public AccountListPage(AccountListViewModel viewModel)
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
