using Arthiva.Services;
using Arthiva.Views;

namespace Arthiva;

public partial class AppShell : Shell
{
    // ═══════════════════════════════════════════════════
    //  BINDABLE PROPERTIES
    // ═══════════════════════════════════════════════════

    public static readonly BindableProperty UnreadNotificationCountProperty =
        BindableProperty.Create(
            nameof(UnreadNotificationCount),
            typeof(int),
            typeof(AppShell),
            0);

    public int UnreadNotificationCount
    {
        get => (int)GetValue(UnreadNotificationCountProperty);
        set => SetValue(UnreadNotificationCountProperty, value);
    }

    public static readonly BindableProperty UserInitialsProperty =
        BindableProperty.Create(
            nameof(UserInitials),
            typeof(string),
            typeof(AppShell),
            "SK"); // Default fallback

    public string UserInitials
    {
        get => (string)GetValue(UserInitialsProperty);
        set => SetValue(UserInitialsProperty, value);
    }

    // ⬇️ NAYA: Current page title for premium title bar
    public static readonly BindableProperty CurrentPageTitleProperty =
        BindableProperty.Create(
            nameof(CurrentPageTitle),
            typeof(string),
            typeof(AppShell),
            "Dashboard");

    public string CurrentPageTitle
    {
        get => (string)GetValue(CurrentPageTitleProperty);
        set => SetValue(CurrentPageTitleProperty, value);
    }

    // ═══════════════════════════════════════════════════
    //  SERVICES
    // ═══════════════════════════════════════════════════

    private readonly INotificationService _notificationService;
    private readonly IUserProfileService _userProfileService;

    public AppShell(
        INotificationService notificationService,
        IUserProfileService userProfileService)
    {
        InitializeComponent();

        _notificationService = notificationService;
        _userProfileService = userProfileService;

        RegisterRoutes();

        Loaded += async (_, _) =>
        {
            await RefreshUnreadCountAsync();
            await LoadUserInitialsAsync();
            UpdatePageTitle(Current?.CurrentState?.Location?.OriginalString);
        };

        Navigated += async (_, e) =>
        {
            await RefreshUnreadCountAsync();
            UpdatePageTitle(e.Current?.Location?.OriginalString);
        };
    }

    // ═══════════════════════════════════════════════════
    //  PAGE TITLE (route → premium title)
    // ═══════════════════════════════════════════════════

    private void UpdatePageTitle(string? route)
    {
        if (string.IsNullOrEmpty(route)) return;

        CurrentPageTitle = route switch
        {
            var r when r.Contains("DashboardPage") => "Dashboard",
            var r when r.Contains("TransactionListPage") => "Transactions",
            var r when r.Contains("AccountListPage") => "Accounts",
            var r when r.Contains("MorePage") => "More",
            var r when r.Contains("BillListPage") => "Bills",
            var r when r.Contains("BillEditPage") => "Edit Bill",
            var r when r.Contains("RecordBillPaymentPage") => "Pay Bill",
            var r when r.Contains("EmiListPage") => "EMIs",
            var r when r.Contains("EmiEditPage") => "Edit EMI",
            var r when r.Contains("EmiDetailPage") => "EMI Details",
            var r when r.Contains("RecordEmiPaymentPage") => "Pay EMI",
            var r when r.Contains("BudgetListPage") => "Budgets",
            var r when r.Contains("BudgetEditPage") => "Edit Budget",
            var r when r.Contains("BorrowLendListPage") => "Borrow & Lend",
            var r when r.Contains("BorrowLendEditPage") => "Edit Record",
            var r when r.Contains("RecordBorrowLendTransactionPage") => "Settle",
            var r when r.Contains("SavingGoalListPage") => "Saving Goals",
            var r when r.Contains("SavingGoalEditPage") => "Edit Goal",
            var r when r.Contains("SavingGoalContributePage") => "Contribute",
            var r when r.Contains("ContactListPage") => "Contacts",
            var r when r.Contains("ContactEditPage") => "Edit Contact",
            var r when r.Contains("ContactDetailPage") => "Contact Details",
            var r when r.Contains("CategoryListPage") => "Categories",
            var r when r.Contains("CategoryEditPage") => "Edit Category",
            var r when r.Contains("UserProfilePage") => "My Profile",
            var r when r.Contains("AddEditTransactionPage") => "Transaction",
            var r when r.Contains("NotificationListPage") => "Notifications",
            _ => "Arthiva"
        };
    }

    // ═══════════════════════════════════════════════════
    //  USER INITIALS
    // ═══════════════════════════════════════════════════

    private async Task LoadUserInitialsAsync()
    {
        try
        {
            var profile = await _userProfileService.GetProfileAsync();
            if (profile != null && !string.IsNullOrWhiteSpace(profile.FullName))
            {
                var names = profile.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (names.Length > 1)
                    UserInitials = $"{names[0][0]}{names[^1][0]}".ToUpper();
                else if (names.Length == 1)
                    UserInitials = names[0][0].ToString().ToUpper();
            }
        }
        catch
        {
            // Profile database may not be ready yet
        }
    }

    // ═══════════════════════════════════════════════════
    //  NOTIFICATIONS
    // ═══════════════════════════════════════════════════

    private async Task RefreshUnreadCountAsync()
    {
        try
        {
            var unread = await _notificationService.GetUnreadAsync();
            UnreadNotificationCount = unread.Count;
        }
        catch
        {
            // Database may not be initialized
        }
    }

    private async void OnBellTapped(object? sender, TappedEventArgs e)
    {
        await GoToAsync(nameof(NotificationListPage));
    }

    // ═══════════════════════════════════════════════════
    //  ROUTES
    // ═══════════════════════════════════════════════════

    private static void RegisterRoutes()
    {
        Routing.RegisterRoute(nameof(AccountEditPage), typeof(AccountEditPage));

        // Categories
        Routing.RegisterRoute(nameof(CategoryListPage), typeof(CategoryListPage));
        Routing.RegisterRoute(nameof(CategoryEditPage), typeof(CategoryEditPage));

        // Bills
        Routing.RegisterRoute(nameof(BillListPage), typeof(BillListPage));
        Routing.RegisterRoute(nameof(BillEditPage), typeof(BillEditPage));
        Routing.RegisterRoute(nameof(RecordBillPaymentPage), typeof(RecordBillPaymentPage));

        // EMI
        Routing.RegisterRoute(nameof(EmiListPage), typeof(EmiListPage));
        Routing.RegisterRoute(nameof(EmiEditPage), typeof(EmiEditPage));
        Routing.RegisterRoute(nameof(RecordEmiPaymentPage), typeof(RecordEmiPaymentPage));
        Routing.RegisterRoute(nameof(EmiDetailPage), typeof(EmiDetailPage));

        // Borrow / Lend
        Routing.RegisterRoute(nameof(BorrowLendListPage), typeof(BorrowLendListPage));
        Routing.RegisterRoute(nameof(BorrowLendEditPage), typeof(BorrowLendEditPage));
        Routing.RegisterRoute(nameof(RecordBorrowLendTransactionPage), typeof(RecordBorrowLendTransactionPage));

        // Saving Goals
        Routing.RegisterRoute(nameof(SavingGoalListPage), typeof(SavingGoalListPage));
        Routing.RegisterRoute(nameof(SavingGoalEditPage), typeof(SavingGoalEditPage));
        Routing.RegisterRoute(nameof(SavingGoalContributePage), typeof(SavingGoalContributePage));

        // Budget
        Routing.RegisterRoute(nameof(BudgetListPage), typeof(BudgetListPage));
        Routing.RegisterRoute(nameof(BudgetEditPage), typeof(BudgetEditPage));

        // Notifications
        Routing.RegisterRoute(nameof(NotificationListPage), typeof(NotificationListPage));

        // Contacts
        Routing.RegisterRoute(nameof(ContactListPage), typeof(ContactListPage));
        Routing.RegisterRoute(nameof(ContactEditPage), typeof(ContactEditPage));
        Routing.RegisterRoute(nameof(ContactDetailPage), typeof(ContactDetailPage));

        // Profile
        Routing.RegisterRoute(nameof(UserProfilePage), typeof(UserProfilePage));

        // Transaction Add/Edit
        Routing.RegisterRoute(nameof(AddEditTransactionPage), typeof(AddEditTransactionPage));
    }
}