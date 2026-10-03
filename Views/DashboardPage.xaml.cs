using MoneySpend.ViewModels;

namespace MoneySpend.Views;

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

        // CHANGED: balance badalte hi label turant update ho (auto-refresh ke liye)
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.TotalBalance))
                MainThread.BeginInvokeOnMainThread(UpdateBalanceLabel);
        };
    }

    // CHANGED: label update ek hi jagah se, hidden/visible dono respect karta hai
    private void UpdateBalanceLabel()
    {
        BalanceAmountLabel.Text = _isBalanceHidden
            ? "₹ • • • • •"
            : $"₹{_viewModel.TotalBalance:N2}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Load (and AWAIT) the data first. Every figure on screen is
        // correct before anything becomes visible or animates — no more
        // stale numbers flashing while a long animation plays out.
        await _viewModel.LoadCommand.ExecuteAsync(null);

        UpdateBalanceLabel(); // CHANGED

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

        // 2b. Quick Actions strip — right after the balance, before
        // anything else, since it's the primary "do something" surface.
        await AnimateInAsync(QuickActionsSection, fast);

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

        UpdateBalanceLabel(); // CHANGED: same helper, duplicate code hata diya
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

    // ============================================================
    // QUICK ACTIONS — one tap, straight to the right form.
    // AddEditTransactionPage is a pushed route (Routing.RegisterRoute
    // in AppShell), NOT a TabBar item — only Home/Transactions/Accounts/
    // More are tabs — so this is a plain relative GoToAsync, same as
    // TransactionListViewModel's own GoToAddCommand uses. The "Type"
    // query param pre-selects Income/Expense there. Borrow/Lend/EMI/
    // Saving/Budget use the same routes their own "Add" buttons already
    // use (see e.g. BorrowLendListViewModel.GoToAddAsync, EmiListViewModel.GoToAddAsync).
    // ============================================================
    private async void OnQuickAddIncomeTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        await Shell.Current.GoToAsync($"{nameof(AddEditTransactionPage)}?Type=Income");
    }

    private async void OnQuickAddExpenseTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        await Shell.Current.GoToAsync($"{nameof(AddEditTransactionPage)}?Type=Expense");
    }

    private async void OnQuickBorrowTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        var route = nameof(BorrowLendEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?Type=Borrow");
    }

    private async void OnQuickLendTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        var route = nameof(BorrowLendEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?Type=Lend");
    }

    private async void OnQuickEmiTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        await Shell.Current.GoToAsync(nameof(EmiEditViewModel).Replace("ViewModel", "Page"));
    }

    private async void OnQuickSavingTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        await Shell.Current.GoToAsync(nameof(SavingGoalEditViewModel).Replace("ViewModel", "Page"));
    }

    private async void OnQuickBudgetTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        await Shell.Current.GoToAsync(nameof(BudgetEditViewModel).Replace("ViewModel", "Page"));
    }
}