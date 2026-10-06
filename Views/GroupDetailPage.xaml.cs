using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class GroupDetailPage : ContentPage
{
    private readonly GroupDetailViewModel _viewModel;

    public GroupDetailPage(GroupDetailViewModel viewModel)
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
