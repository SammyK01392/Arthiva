using MoneySpend.Controls;
using MoneySpend.ViewModels;
// MoneySpend.Models me bhi TransactionType ho sakta hai — alias se conflict nahi hoga
using PopupType = MoneySpend.Controls.TransactionType;

namespace MoneySpend.Views;

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
        _viewModel.ShowSuccessAsync = ShowSuccessAsync;   // VM save ke baad isko await karega
        _viewModel.AppearingCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ShowSuccessAsync = null;
    }

    private Task ShowSuccessAsync(decimal billAmount)
    {
        var mode = _viewModel.IsEditMode ? TransactionMode.Edit : TransactionMode.Create;

        return MainThread.InvokeOnMainThreadAsync(() =>
            SuccessPopup.ShowAsync(PopupType.Bill, billAmount, mode, autoCloseMs: 1800));
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
