using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class NotificationListPage : ContentPage
{
    private readonly NotificationListViewModel _viewModel;

    public NotificationListPage(NotificationListViewModel viewModel)
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
