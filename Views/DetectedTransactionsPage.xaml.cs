using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class DetectedTransactionsPage : ContentPage
{
    private readonly DetectedTransactionsViewModel _viewModel;

    public DetectedTransactionsPage(DetectedTransactionsViewModel viewModel)
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
