using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class SplitListPage : ContentPage
{
    private readonly SplitListViewModel _viewModel;

    public SplitListPage(SplitListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
