using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class EmiEditPage : ContentPage
{
    private readonly EmiEditViewModel _viewModel;

    public EmiEditPage(EmiEditViewModel viewModel)
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
