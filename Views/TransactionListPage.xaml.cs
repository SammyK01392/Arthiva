using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class TransactionListPage : ContentPage
{
    private readonly TransactionListViewModel _viewModel;

    public TransactionListPage(TransactionListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
        AnimateFabIn();
    }

    private async void AnimateFabIn()
    {
        FabButton.Scale = 0;
        FabButton.Opacity = 0;

        await Task.WhenAll(
            FabButton.ScaleTo(1, 400, Easing.SpringOut),
            FabButton.FadeTo(1, 300, Easing.CubicOut)
        );
    }

    private async void OnFabTapped(object sender, TappedEventArgs e)
    {
        await FabButton.ScaleTo(0.9, 80, Easing.CubicOut);
        await FabButton.ScaleTo(1.0, 120, Easing.CubicIn);

        await Shell.Current.GoToAsync(nameof(AddEditTransactionPage));
    }
}