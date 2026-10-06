using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class GroupEditPage : ContentPage
{
    private readonly GroupEditViewModel _viewModel;

    public GroupEditPage(GroupEditViewModel viewModel)
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
