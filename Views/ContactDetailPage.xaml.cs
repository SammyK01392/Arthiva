using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class ContactDetailPage : ContentPage
{
    private readonly ContactDetailViewModel _viewModel;

    public ContactDetailPage(ContactDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.AppearingCommand.Execute(null);
    }
}
