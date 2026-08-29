using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class RecordBillPaymentPage : ContentPage
{
    private readonly RecordBillPaymentViewModel _viewModel;

    public RecordBillPaymentPage(RecordBillPaymentViewModel viewModel)
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
