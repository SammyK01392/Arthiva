using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class BillListPage : ContentPage
{
    private readonly BillListViewModel _viewModel;

    public BillListPage(BillListViewModel viewModel)
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
