using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class SharedRequestListPage : ContentPage
{
    private readonly SharedRequestListViewModel _viewModel;

    public SharedRequestListPage(SharedRequestListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
