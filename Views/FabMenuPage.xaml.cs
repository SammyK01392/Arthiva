using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class FabMenuPage : ContentPage
{
    private bool _closing;

    public FabMenuPage()
    {
        InitializeComponent();
    }

    private VisualElement[] Items =>
        new VisualElement[] { ItemIncome, ItemExpense, ItemBorrow, ItemLend };

    private double SheetOffset => Sheet.Height > 0 ? Sheet.Height : 420;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            Backdrop.Opacity = 0;
            Sheet.TranslationY = 420; // sheet neeche se aayegi
            foreach (var item in Items)
            {
                item.Opacity = 0;
                item.Scale = 0.88;
            }

            var cards = Items.Select(async (item, i) =>
            {
                await Task.Delay(120 + i * 55);
                await Task.WhenAll(
                    item.FadeTo(1, 200, Easing.CubicOut),
                    item.ScaleTo(1, 240, Easing.SpringOut));
            });

            await Task.WhenAll(
                Backdrop.FadeTo(1, 200),
                Sheet.TranslateTo(0, 0, 280, Easing.CubicOut),
                Task.WhenAll(cards));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FabMenu] {ex}");
        }
    }

    private async Task CloseAsync()
    {
        try
        {
            await Task.WhenAll(
                Backdrop.FadeTo(0, 180),
                Sheet.TranslateTo(0, SheetOffset, 220, Easing.CubicIn));
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