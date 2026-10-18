using MoneySpend.Controls;
using MoneySpend.ViewModels;
// Agar MoneySpend.Models me bhi TransactionType enum hai to naam conflict na ho:
using PopupType = MoneySpend.Controls.TransactionType;

namespace MoneySpend.Views;

public partial class AddEditTransactionPage : ContentPage
{
    private readonly AddEditTransactionViewModel _viewModel;

    public AddEditTransactionPage(AddEditTransactionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.ShowSuccessAsync = ShowSuccessAsync;   // VM isko save ke baad await karega
        _viewModel.AppearingCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.ShowSuccessAsync = null;
    }

    private Task ShowSuccessAsync(decimal amount)
    {
        // Abhi form me sirf Income/Expense hai. Baad me Borrow/Lend/EMI/Bill/Split
        // aaye to yahin switch badha do.
        var type = string.Equals(_viewModel.TransactionType?.ToString(), "Income",
                                 StringComparison.OrdinalIgnoreCase)
            ? PopupType.Income
            : PopupType.Expense;

        var mode = _viewModel.IsEditMode ? TransactionMode.Edit : TransactionMode.Create;

        return MainThread.InvokeOnMainThreadAsync(() =>
            SuccessPopup.ShowAsync(type, amount, mode, autoCloseMs: 1800));
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
