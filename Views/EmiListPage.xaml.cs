using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class EmiListPage : ContentPage
{
    private readonly EmiListViewModel _viewModel;

    public EmiListPage(EmiListViewModel viewModel)
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
