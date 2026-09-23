using Arthiva.ViewModels;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

        _viewModel.LoadCommand.Execute(null);

        if (!_hasAnimatedOnLoad)
        {
            _hasAnimatedOnLoad = true;
            // Slight delay so layout is measured before animating
            await Task.Delay(80);
            await RunEntranceAnimationsAsync();
        }
    }

    // ============================================================
    // ENTRANCE ANIMATIONS — staggered fade + slide from bottom
    // ============================================================
    private async Task RunEntranceAnimationsAsync()
    {
        // 1. Greeting (first to appear)
        _ = GreetingSection.FadeTo(1, 350, Easing.CubicOut);
        await GreetingSection.TranslateTo(0, 0, 400, Easing.CubicOut);

        // 2. Balance Card
        _ = BalanceCard.FadeTo(1, 400, Easing.CubicOut);
        await BalanceCard.TranslateTo(0, 0, 450, Easing.CubicOut);

        // 3. Summary cards — cascade
        var summaryCards = new VisualElement[] { CardIncome, CardExpense, CardSavings, CardNetBalance };
        foreach (var card in summaryCards)
        {
            _ = card.FadeTo(1, 300, Easing.CubicOut);
            _ = card.TranslateTo(0, 0, 350, Easing.CubicOut);
            await Task.Delay(70);
        }

        // 4. Borrow Lend
        _ = BorrowLendCard.FadeTo(1, 350, Easing.CubicOut);
        await BorrowLendCard.TranslateTo(0, 0, 400, Easing.CubicOut);

        // 5. Recent Transactions
        _ = RecentTransactionsCard.FadeTo(1, 350, Easing.CubicOut);
        _ = RecentTransactionsCard.TranslateTo(0, 0, 400, Easing.CubicOut);

        // 6. Bills + EMIs side by side
        _ = BillsCard.FadeTo(1, 350, Easing.CubicOut);
        _ = EmisCard.FadeTo(1, 350, Easing.CubicOut);
        await Task.WhenAll(
            BillsCard.TranslateTo(0, 0, 400, Easing.CubicOut),
            EmisCard.TranslateTo(0, 0, 400, Easing.CubicOut)
        );

        // 7. Start count-up balance animation
        _ = AnimateBalanceCountUpAsync();
    }

    // ============================================================
    // COUNT-UP ANIMATION — balance from 0 → actual
    // ============================================================
    private async Task AnimateBalanceCountUpAsync()
    {
        var target = _viewModel.TotalBalance;
        if (target == 0)
        {
            BalanceAmountLabel.Text = "₹0.00";
            return;
        }

        const int steps = 30;
        const int duration = 900;
        var stepDelay = duration / steps;

        for (int i = 1; i <= steps; i++)
        {
            // Ease-out curve: starts fast, slows at end
            double progress = 1 - Math.Pow(1 - (i / (double)steps), 3);
            var current = (decimal)((double)target * progress);

            BalanceAmountLabel.Text = $"₹{current:N2}";
            await Task.Delay(stepDelay);
        }

        BalanceAmountLabel.Text = $"₹{target:N2}";
    }

    // ============================================================
    // EYE ICON — toggle balance visibility with fade
    // ============================================================
    private async void OnEyeIconTapped(object sender, TappedEventArgs e)
    {
        _isBalanceHidden = !_isBalanceHidden;

        await BalanceAmountLabel.FadeTo(0, 120, Easing.CubicIn);

        if (_isBalanceHidden)
        {
            BalanceAmountLabel.Text = "₹ • • • • •";
            EyeIcon.Opacity = 0.5;
        }
        else
        {
            BalanceAmountLabel.Text = $"₹{_viewModel.TotalBalance:N2}";
            EyeIcon.Opacity = 0.9;
        }

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