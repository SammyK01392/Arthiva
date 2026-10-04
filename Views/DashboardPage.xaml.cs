using MoneySpend.Services;
using MoneySpend.ViewModels;

namespace MoneySpend.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    private readonly IUserProfileService _userProfileService;
    private bool _isBalanceHidden = Preferences.Get("hide_balance_default", false);
    private bool _hasAnimatedOnLoad = false;

    public DashboardPage(DashboardViewModel viewModel, IUserProfileService userProfileService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _userProfileService = userProfileService;
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

    private async Task LoadAvatarAsync()
    {
        try
        {
            var profile = await _userProfileService.GetProfileAsync();
            var path = profile?.ProfileImage;
            var hasImage = !string.IsNullOrEmpty(path) && File.Exists(path);

            AvatarImage.Source = hasImage ? ImageSource.FromFile(path) : null;
            AvatarImage.IsVisible = hasImage;
            AvatarInitialLabel.IsVisible = !hasImage;
        }
        catch
        {
            // fail ho to initial letter hi dikhne do
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
            UpdateBalanceLabel();
            await LoadAvatarAsync();
        }
        catch
        {
            // load fail ho to bhi UI visible rehni chahiye
        }

        if (!_hasAnimatedOnLoad)
        {
            _hasAnimatedOnLoad = true;
            await RunEntranceAnimationsAsync();
        }
    }

    // Sab animated elements (Greeting + Balance sirf fade, translate nahi)
    private VisualElement[] AllAnimatedElements => new VisualElement[]
    {
        GreetingSection, BalanceCard,
        CardIncome, CardExpense, CardSavings, CardNetBalance,
        QuickActionsSection, BorrowLendCard, RecentTransactionsCard,
        BillsCard, EmisCard
    };

    // Entrance animations
    private async Task RunEntranceAnimationsAsync()
    {
        const uint fast = 180;
        const uint slower = 220;

        try
        {
            // Top section: sirf fade -> layout kabhi shift nahi hoga
            await Task.WhenAll(
                GreetingSection.FadeTo(1, slower, Easing.CubicOut),
                BalanceCard.FadeTo(1, slower, Easing.CubicOut));

            var summaryCards = new VisualElement[] { CardIncome, CardExpense, CardSavings, CardNetBalance };
            await Task.WhenAll(summaryCards.Select(c => AnimateInAsync(c, fast)));

            await AnimateInAsync(QuickActionsSection, fast);

            await Task.WhenAll(
                AnimateInAsync(BorrowLendCard, fast),
                AnimateInAsync(RecentTransactionsCard, fast),
                AnimateInAsync(BillsCard, fast),
                AnimateInAsync(EmisCard, fast));
        }
        catch
        {
            // animation fail ho to ignore
        }
        finally
        {
            // Safety: kuch bhi ho jaye, final state hamesha sahi rahe
            ResetAnimatedElements();
        }
    }

    private void ResetAnimatedElements()
    {
        foreach (var v in AllAnimatedElements)
        {
            v.CancelAnimations();
            v.Opacity = 1;
            v.TranslationY = 0;
        }
    }

    private static Task AnimateInAsync(VisualElement element, uint duration)
    {
        return Task.WhenAll(
            element.FadeTo(1, duration, Easing.CubicOut),
            element.TranslateTo(0, 0, duration, Easing.CubicOut));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Page se jaate waqt animation beech me ruk jaye to bhi state reset
        if (_hasAnimatedOnLoad) ResetAnimatedElements();
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

    // Avatar tap -> profile
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
