using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class ContactEditPage : ContentPage
{
    private readonly ContactEditViewModel _viewModel;

    public ContactEditPage(ContactEditViewModel viewModel)
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
