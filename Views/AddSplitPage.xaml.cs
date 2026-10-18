using MoneySpend.Controls;
using MoneySpend.ViewModels;
// MoneySpend.Models me bhi TransactionType ho sakta hai — alias se conflict nahi hoga
using PopupType = MoneySpend.Controls.TransactionType;

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
        _viewModel.ShowSuccessAsync = ShowSuccessAsync;   // VM save ke baad isko await karega
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ShowSuccessAsync = null;
    }

    private Task ShowSuccessAsync(decimal totalAmount)
        => MainThread.InvokeOnMainThreadAsync(() =>
            SuccessPopup.ShowAsync(PopupType.Split, totalAmount,
                                   TransactionMode.Create, autoCloseMs: 1800));

    // Popup khula ho to Android back button pehle popup band kare
    protected override bool OnBackButtonPressed()
    {
        if (SuccessPopup.IsVisible)
        {
            _ = SuccessPopup.CloseAsync();
            return true;
        }
        return base.OnBackButtonPressed();
    }
}
