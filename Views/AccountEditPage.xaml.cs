using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class AccountEditPage : ContentPage
{
    private readonly AccountEditViewModel _viewModel;

    public AccountEditPage(AccountEditViewModel viewModel)
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
