using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class EmiDetailPage : ContentPage
{
    private readonly EmiDetailViewModel _viewModel;

    public EmiDetailPage(EmiDetailViewModel viewModel)
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

    private void OnPaymentsTabClicked(object sender, EventArgs e)
    {
        PaymentsSection.IsVisible = true;
        ScheduleSection.IsVisible = false;
    }

    private void OnScheduleTabClicked(object sender, EventArgs e)
    {
        PaymentsSection.IsVisible = false;
        ScheduleSection.IsVisible = true;
    }
}