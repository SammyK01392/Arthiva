using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class RecordEmiPaymentPage : ContentPage
{
    private readonly RecordEmiPaymentViewModel _viewModel;

    public RecordEmiPaymentPage(RecordEmiPaymentViewModel viewModel)
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
