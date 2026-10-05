using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class AddSplitPage : ContentPage
{
    private readonly AddSplitViewModel _viewModel;

    public AddSplitPage(AddSplitViewModel viewModel)
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
