using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class TransactionListPage : ContentPage
{
    private readonly TransactionListViewModel _viewModel;

    public TransactionListPage(TransactionListViewModel viewModel)
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
