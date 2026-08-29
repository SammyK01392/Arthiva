using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class BorrowLendListPage : ContentPage
{
    private readonly BorrowLendListViewModel _viewModel;

    public BorrowLendListPage(BorrowLendListViewModel viewModel)
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
