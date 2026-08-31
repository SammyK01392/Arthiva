using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class ContactListPage : ContentPage
{
    private readonly ContactListViewModel _viewModel;

    public ContactListPage(ContactListViewModel viewModel)
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
