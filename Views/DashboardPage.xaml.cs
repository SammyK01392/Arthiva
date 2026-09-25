using Arthiva.ViewModels;

namespace Arthiva.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    private bool _isBalanceHidden = false;
    private bool _hasAnimatedOnLoad = false;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Load (and AWAIT) the data first. Every figure on screen is
        // correct before anything becomes visible or animates — no more
        // stale numbers flashing while a long animation plays out.
        await _viewModel.LoadCommand.ExecuteAsync(null);

        BalanceAmountLabel.Text = _isBalanceHidden
            ? "₹ • • • • •"
            : $"₹{_viewModel.TotalBalance:N2}";

        if (!_hasAnimatedOnLoad)
        {
            _hasAnimatedOnLoad = true;
            // Fire-and-forget: purely cosmetic now, doesn't gate correctness.
            _ = RunEntranceAnimationsAsync();
        }
    }

    // ============================================================
    // ENTRANCE ANIMATIONS — fast, parallel fade + slide from bottom.
    // Data is already loaded and correct by the time this runs, so
    // this is now pure polish, not something the user waits on.
    // ============================================================
    private async Task RunEntranceAnimationsAsync()
    {
        const uint fast = 180;
        const uint slower = 220;

        // 1 + 2. Greeting and Balance card together
        await Task.WhenAll(
            AnimateInAsync(GreetingSection, slower),
            AnimateInAsync(BalanceCard, slower)
        );

        // 3. Summary cards — all together, tiny stagger for polish only
        var summaryCards = new VisualElement[] { CardIncome, CardExpense, CardSavings, CardNetBalance };
        await Task.WhenAll(summaryCards.Select(c => AnimateInAsync(c, fast)));

        // 4-6. Everything else together
        await Task.WhenAll(
            AnimateInAsync(BorrowLendCard, fast),
            AnimateInAsync(RecentTransactionsCard, fast),
            AnimateInAsync(BillsCard, fast),
            AnimateInAsync(EmisCard, fast)
        );
    }

    private static Task AnimateInAsync(VisualElement element, uint duration)
    {
        return Task.WhenAll(
            element.FadeTo(1, duration, Easing.CubicOut),
            element.TranslateTo(0, 0, duration, Easing.CubicOut)
        );
    }

    // ============================================================
    // EYE ICON — toggle balance visibility with fade
    // ============================================================
    private async void OnEyeIconTapped(object sender, TappedEventArgs e)
    {
        _isBalanceHidden = !_isBalanceHidden;

        await BalanceAmountLabel.FadeTo(0, 120, Easing.CubicIn);

        BalanceAmountLabel.Text = _isBalanceHidden
            ? "₹ • • • • •"
            : $"₹{_viewModel.TotalBalance:N2}";
        EyeIcon.Opacity = _isBalanceHidden ? 0.5 : 0.9;

        await BalanceAmountLabel.FadeTo(1, 180, Easing.CubicOut);
    }

    // ============================================================
    // BUTTON SCALE FEEDBACK
    // ============================================================
    private async Task AnimateButtonTapAsync(VisualElement element)
    {
        await element.ScaleTo(0.95, 80, Easing.CubicOut);
        await element.ScaleTo(1.0, 100, Easing.CubicIn);
    }

    // ============================================================
    // NAVIGATION HANDLERS
    // ============================================================
    private async void OnRecentTransactionsViewAllTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        await Shell.Current.GoToAsync("///TransactionListPage");
    }

    private async void OnUpcomingBillsViewAllTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        await Shell.Current.GoToAsync(nameof(BillListPage));
    }

    private async void OnActiveEmisViewAllTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        await Shell.Current.GoToAsync(nameof(EmiListPage));
    }

    private async void OnViewAccountsTapped(object sender, TappedEventArgs e)
    {
        // Scale the actual button (sender may not be the Border directly)
        await AnimateButtonTapAsync(ViewAccountsButton);
        await Shell.Current.GoToAsync("///AccountListPage");
    }

    private async void OnBorrowLendTapped(object sender, TappedEventArgs e)
    {
        await AnimateButtonTapAsync(BorrowLendCard);
        await Shell.Current.GoToAsync(nameof(BorrowLendListPage));
    }
}