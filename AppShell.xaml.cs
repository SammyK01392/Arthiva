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
            "SK");

    public string UserInitials
    {
        get => (string)GetValue(UserInitialsProperty);
        set => SetValue(UserInitialsProperty, value);
    }


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


    // ═══════════════════════════════════════════════════
    //  CONSTRUCTOR
    // ═══════════════════════════════════════════════════

    public AppShell(
        INotificationService notificationService,
        IUserProfileService userProfileService)
    {
        InitializeComponent();

        _notificationService = notificationService;
        _userProfileService = userProfileService;

        RegisterRoutes();


        // ═══════════════════════════════════════════════
        // SHELL LOADED
        // ═══════════════════════════════════════════════

        Loaded += async (_, _) =>
        {
            await RefreshUnreadCountAsync();
            await LoadUserInitialsAsync();

            UpdateCurrentPageTitle();
        };


        // ═══════════════════════════════════════════════
        // NAVIGATION CHANGED
        // ═══════════════════════════════════════════════

        Navigated += async (_, _) =>
        {
            await RefreshUnreadCountAsync();

            // CurrentPage is now the actual visible page.
            UpdateCurrentPageTitle();
        };
    }


    // ═══════════════════════════════════════════════════
    //  CURRENT PAGE TITLE
    // ═══════════════════════════════════════════════════

    private void UpdateCurrentPageTitle()
    {
        try
        {
            var currentPage = CurrentPage;

            if (currentPage == null)
            {
                CurrentPageTitle = "Arthiva";
                return;
            }


            // ═══════════════════════════════════════════
            // FIRST PRIORITY:
            // Use the Title directly from ContentPage
            //
            // Example:
            // <ContentPage Title="Bills">
            // ═══════════════════════════════════════════

            if (!string.IsNullOrWhiteSpace(currentPage.Title))
            {
                CurrentPageTitle = currentPage.Title;
                return;
            }


            // ═══════════════════════════════════════════
            // FALLBACK:
            // Determine title using the actual page type
            // ═══════════════════════════════════════════

            CurrentPageTitle = currentPage switch
            {
                // ───────────────────────────────────────
                // Main Tabs
                // ───────────────────────────────────────

                DashboardPage => "Dashboard",

                TransactionListPage => "Transactions",

                AccountListPage => "Accounts",

                MorePage => "More",


                // ───────────────────────────────────────
                // Accounts
                // ───────────────────────────────────────

                AccountEditPage => "Account",


                // ───────────────────────────────────────
                // Transactions
                // ───────────────────────────────────────

                AddEditTransactionPage => "Transaction",


                // ───────────────────────────────────────
                // Bills
                // ───────────────────────────────────────

                BillListPage => "Bills",

                BillEditPage => "Bill",

                RecordBillPaymentPage => "Pay Bill",


                // ───────────────────────────────────────
                // EMI
                // ───────────────────────────────────────

                EmiListPage => "EMIs",

                EmiEditPage => "EMI",

                EmiDetailPage => "EMI Details",

                RecordEmiPaymentPage => "Pay EMI",


                // ───────────────────────────────────────
                // Budgets
                // ───────────────────────────────────────

                BudgetListPage => "Budgets",

                BudgetEditPage => "Budget",


                // ───────────────────────────────────────
                // Borrow / Lend
                // ───────────────────────────────────────

                BorrowLendListPage => "Borrow & Lend",

                BorrowLendEditPage => "Borrow & Lend",

                RecordBorrowLendTransactionPage => "Settle",


                // ───────────────────────────────────────
                // Saving Goals
                // ───────────────────────────────────────

                SavingGoalListPage => "Saving Goals",

                SavingGoalEditPage => "Saving Goal",

                SavingGoalContributePage => "Contribute",


                // ───────────────────────────────────────
                // Contacts
                // ───────────────────────────────────────

                ContactListPage => "Contacts",

                ContactEditPage => "Contact",

                ContactDetailPage => "Contact Details",


                // ───────────────────────────────────────
                // Categories
                // ───────────────────────────────────────

                CategoryListPage => "Categories",

                CategoryEditPage => "Category",


                // ───────────────────────────────────────
                // Profile
                // ───────────────────────────────────────

                UserProfilePage => "My Profile",


                // ───────────────────────────────────────
                // Notifications
                // ───────────────────────────────────────

                NotificationListPage => "Notifications",


                // ───────────────────────────────────────
                // Default
                // ───────────────────────────────────────

                _ => "Arthiva"
            };
        }
        catch
        {
            CurrentPageTitle = "Arthiva";
        }
    }


    // ═══════════════════════════════════════════════════
    //  USER INITIALS
    // ═══════════════════════════════════════════════════

    private async Task LoadUserInitialsAsync()
    {
        try
        {
            var profile = await _userProfileService.GetProfileAsync();

            if (profile != null &&
                !string.IsNullOrWhiteSpace(profile.FullName))
            {
                var names = profile.FullName.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

                if (names.Length > 1)
                {
                    UserInitials =
                        $"{names[0][0]}{names[^1][0]}".ToUpper();
                }
                else if (names.Length == 1)
                {
                    UserInitials =
                        names[0][0].ToString().ToUpper();
                }
            }
        }
        catch
        {
            // Profile database may not be ready yet.
        }
    }


    // ═══════════════════════════════════════════════════
    //  NOTIFICATIONS
    // ═══════════════════════════════════════════════════

    private async Task RefreshUnreadCountAsync()
    {
        try
        {
            var unread =
                await _notificationService.GetUnreadAsync();

            UnreadNotificationCount = unread.Count;
        }
        catch
        {
            // Database may not be initialized yet.
        }
    }


    private async void OnBellTapped(
        object? sender,
        TappedEventArgs e)
    {
        await GoToAsync(nameof(NotificationListPage));
    }


    // ═══════════════════════════════════════════════════
    //  ROUTES
    // ═══════════════════════════════════════════════════

    private static void RegisterRoutes()
    {
        // ═══════════════════════════════════════════════
        // ACCOUNTS
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(AccountEditPage),
            typeof(AccountEditPage));


        // ═══════════════════════════════════════════════
        // CATEGORIES
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(CategoryListPage),
            typeof(CategoryListPage));

        Routing.RegisterRoute(
            nameof(CategoryEditPage),
            typeof(CategoryEditPage));


        // ═══════════════════════════════════════════════
        // BILLS
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(BillListPage),
            typeof(BillListPage));

        Routing.RegisterRoute(
            nameof(BillEditPage),
            typeof(BillEditPage));

        Routing.RegisterRoute(
            nameof(RecordBillPaymentPage),
            typeof(RecordBillPaymentPage));


        // ═══════════════════════════════════════════════
        // EMI
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(EmiListPage),
            typeof(EmiListPage));

        Routing.RegisterRoute(
            nameof(EmiEditPage),
            typeof(EmiEditPage));

        Routing.RegisterRoute(
            nameof(RecordEmiPaymentPage),
            typeof(RecordEmiPaymentPage));

        Routing.RegisterRoute(
            nameof(EmiDetailPage),
            typeof(EmiDetailPage));


        // ═══════════════════════════════════════════════
        // BORROW / LEND
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(BorrowLendListPage),
            typeof(BorrowLendListPage));

        Routing.RegisterRoute(
            nameof(BorrowLendEditPage),
            typeof(BorrowLendEditPage));

        Routing.RegisterRoute(
            nameof(RecordBorrowLendTransactionPage),
            typeof(RecordBorrowLendTransactionPage));


        // ═══════════════════════════════════════════════
        // SAVING GOALS
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(SavingGoalListPage),
            typeof(SavingGoalListPage));

        Routing.RegisterRoute(
            nameof(SavingGoalEditPage),
            typeof(SavingGoalEditPage));

        Routing.RegisterRoute(
            nameof(SavingGoalContributePage),
            typeof(SavingGoalContributePage));


        // ═══════════════════════════════════════════════
        // BUDGET
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(BudgetListPage),
            typeof(BudgetListPage));

        Routing.RegisterRoute(
            nameof(BudgetEditPage),
            typeof(BudgetEditPage));


        // ═══════════════════════════════════════════════
        // NOTIFICATIONS
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(NotificationListPage),
            typeof(NotificationListPage));


        // ═══════════════════════════════════════════════
        // CONTACTS
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(ContactListPage),
            typeof(ContactListPage));

        Routing.RegisterRoute(
            nameof(ContactEditPage),
            typeof(ContactEditPage));

        Routing.RegisterRoute(
            nameof(ContactDetailPage),
            typeof(ContactDetailPage));


        // ═══════════════════════════════════════════════
        // PROFILE
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(UserProfilePage),
            typeof(UserProfilePage));


        // ═══════════════════════════════════════════════
        // TRANSACTIONS
        // ═══════════════════════════════════════════════

        Routing.RegisterRoute(
            nameof(AddEditTransactionPage),
            typeof(AddEditTransactionPage));
    }
}