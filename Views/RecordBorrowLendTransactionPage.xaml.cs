using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class RecordBorrowLendTransactionPage : ContentPage
{
    private readonly RecordBorrowLendTransactionViewModel _viewModel;

    public RecordBorrowLendTransactionPage(RecordBorrowLendTransactionViewModel viewModel)
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
