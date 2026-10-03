using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class FabMenuPage : ContentPage
{
    private bool _closing;

    public FabMenuPage()
    {
        InitializeComponent();
    }

    private double SheetOffset => Sheet.Height > 0 ? Sheet.Height + 40 : 700;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await Task.Delay(30); // layout measure hone do
            Backdrop.Opacity = 0;
            Sheet.TranslationY = SheetOffset;

            var anim = Task.WhenAll(
                Backdrop.FadeTo(1, 200),
                Sheet.TranslateTo(0, 0, 260, Easing.CubicOut));

            await Task.WhenAny(anim, Task.Delay(700));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FabMenu] {ex}");
        }
        finally
        {
            Backdrop.Opacity = 1; // kuch bhi ho, final state
            Sheet.TranslationY = 0;
        }
    }

    private async Task CloseAsync()
    {
        try
        {
            await Task.WhenAny(
                Task.WhenAll(
                    Backdrop.FadeTo(0, 160),
                    Sheet.TranslateTo(0, SheetOffset, 200, Easing.CubicIn)),
                Task.Delay(500));
        }
        catch { }

        await Navigation.PopModalAsync(false);
    }

    private async Task PressAsync(VisualElement el)
    {
        try
        {
            await el.ScaleTo(0.95, 70, Easing.CubicOut);
            await el.ScaleTo(1, 90, Easing.CubicIn);
        }
        catch { }
    }

    private async Task CloseAndGoAsync(VisualElement card, string route)
    {
        if (_closing) return;
        _closing = true;
        try
        {
            await PressAsync(card);
            await CloseAsync();
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FabMenu] {ex}");
            _closing = false;
        }
    }

    private async void OnBackdropTapped(object sender, TappedEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        try { await CloseAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FabMenu] {ex}"); }
    }

    private void OnIncomeTapped(object sender, TappedEventArgs e)
        => _ = CloseAndGoAsync(ItemIncome, $"{nameof(AddEditTransactionPage)}?Type=Income");

    private void OnExpenseTapped(object sender, TappedEventArgs e)
        => _ = CloseAndGoAsync(ItemExpense, $"{nameof(AddEditTransactionPage)}?Type=Expense");

    private void OnBorrowTapped(object sender, TappedEventArgs e)
    {
        var route = nameof(BorrowLendEditViewModel).Replace("ViewModel", "Page");
        _ = CloseAndGoAsync(ItemBorrow, $"{route}?Type=Borrow");
    }

    private void OnLendTapped(object sender, TappedEventArgs e)
    {
        var route = nameof(BorrowLendEditViewModel).Replace("ViewModel", "Page");
        _ = CloseAndGoAsync(ItemLend, $"{route}?Type=Lend");
    }
}