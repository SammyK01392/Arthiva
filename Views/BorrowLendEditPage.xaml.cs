using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class BorrowLendEditPage : ContentPage
{
    private readonly BorrowLendEditViewModel _viewModel;

    public BorrowLendEditPage(BorrowLendEditViewModel viewModel)
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
