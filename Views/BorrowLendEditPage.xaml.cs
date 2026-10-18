using MoneySpend.Controls;
using MoneySpend.ViewModels;
// MoneySpend.Models me bhi TransactionType ho sakta hai — alias se conflict nahi hoga
using PopupType = MoneySpend.Controls.TransactionType;

namespace MoneySpend.Views;

public partial class BorrowLendEditPage : ContentPage
{
    private readonly BorrowLendEditViewModel _viewModel;

    public BorrowLendEditPage(BorrowLendEditViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ShowSuccessAsync = ShowSuccessAsync;   // VM save ke baad isko await karega
        _viewModel.AppearingCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ShowSuccessAsync = null;
    }

    private Task ShowSuccessAsync(decimal amount, string? subtitle)
    {
        var type = _viewModel.Type == "Borrow" ? PopupType.Borrow : PopupType.Lend;

        return MainThread.InvokeOnMainThreadAsync(() =>
            SuccessPopup.ShowAsync(type, amount, TransactionMode.Create,
                                   autoCloseMs: 1800, subtitle: subtitle));
    }

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
