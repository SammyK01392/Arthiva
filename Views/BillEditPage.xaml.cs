using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class BillEditPage : ContentPage
{
    private readonly BillEditViewModel _viewModel;

    public BillEditPage(BillEditViewModel viewModel)
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
