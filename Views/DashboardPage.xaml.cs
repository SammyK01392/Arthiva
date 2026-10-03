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

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.TotalBalance))
                MainThread.BeginInvokeOnMainThread(UpdateBalanceLabel);
        };
    }

    private void UpdateBalanceLabel()
    {
        BalanceAmountLabel.Text = _isBalanceHidden
            ? "₹ • • • • •"
            : $"₹{_viewModel.TotalBalance:N2}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadCommand.ExecuteAsync(null);
        UpdateBalanceLabel();

        if (!_hasAnimatedOnLoad)
        {
            _hasAnimatedOnLoad = true;
            _ = RunEntranceAnimationsAsync();
        }
    }

    // Entrance animations
    private async Task RunEntranceAnimationsAsync()
    {
        const uint fast = 180;
        const uint slower = 220;

        await Task.WhenAll(
            AnimateInAsync(GreetingSection, slower),
            AnimateInAsync(BalanceCard, slower));

        await AnimateInAsync(QuickActionsSection, fast);

        var summaryCards = new VisualElement[] { CardIncome, CardExpense, CardSavings, CardNetBalance };
        await Task.WhenAll(summaryCards.Select(c => AnimateInAsync(c, fast)));

        await Task.WhenAll(
            AnimateInAsync(BorrowLendCard, fast),
            AnimateInAsync(RecentTransactionsCard, fast),
            AnimateInAsync(BillsCard, fast),
            AnimateInAsync(EmisCard, fast));
    }

    private static Task AnimateInAsync(VisualElement element, uint duration)
    {
        return Task.WhenAll(
            element.FadeTo(1, duration, Easing.CubicOut),
            element.TranslateTo(0, 0, duration, Easing.CubicOut));
    }

    // Eye icon
    private async void OnEyeIconTapped(object sender, TappedEventArgs e)
    {
        _isBalanceHidden = !_isBalanceHidden;

        await BalanceAmountLabel.FadeTo(0, 120, Easing.CubicIn);
        UpdateBalanceLabel();
        EyeIcon.Opacity = _isBalanceHidden ? 0.5 : 0.9;
        await BalanceAmountLabel.FadeTo(1, 180, Easing.CubicOut);
    }

    // Tap feedback
    private async Task AnimateButtonTapAsync(VisualElement element)
    {
        await element.ScaleTo(0.95, 80, Easing.CubicOut);
        await element.ScaleTo(1.0, 100, Easing.CubicIn);
    }

    // NEW: avatar tap -> profile
    private async void OnProfileTapped(object sender, TappedEventArgs e)
    {
        if (sender is VisualElement el) await AnimateButtonTapAsync(el);
        await Shell.Current.GoToAsync(nameof(UserProfilePage));
    }

    // Navigation
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
        await AnimateButtonTapAsync(ViewAccountsButton);
        await Shell.Current.GoToAsync("///AccountListPage");
    }

    private async void OnBorrowLendTapped(object sender, TappedEventArgs e)
    {
        await AnimateButtonTapAsync(BorrowLendCard);
        await Shell.Current.GoToAsync(nameof(BorrowLendListPage));
    }

    // Quick actions
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