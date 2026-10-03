using MoneySpend.Services;
using MoneySpend.Views;
using Plugin.LocalNotification;
using INotificationService = MoneySpend.Services.INotificationService;
namespace MoneySpend;

public partial class AppShell : Shell
{
    // Bindable properties
    public static readonly BindableProperty UnreadNotificationCountProperty =
        BindableProperty.Create(nameof(UnreadNotificationCount), typeof(int), typeof(AppShell), 0);
    public int UnreadNotificationCount
    {
        get => (int)GetValue(UnreadNotificationCountProperty);
        set => SetValue(UnreadNotificationCountProperty, value);
    }

    public static readonly BindableProperty UserInitialsProperty =
        BindableProperty.Create(nameof(UserInitials), typeof(string), typeof(AppShell), "SK");
    public string UserInitials
    {
        get => (string)GetValue(UserInitialsProperty);
        set => SetValue(UserInitialsProperty, value);
    }

    public static readonly BindableProperty CurrentPageTitleProperty =
        BindableProperty.Create(nameof(CurrentPageTitle), typeof(string), typeof(AppShell), "Dashboard");
    public string CurrentPageTitle
    {
        get => (string)GetValue(CurrentPageTitleProperty);
        set => SetValue(CurrentPageTitleProperty, value);
    }

    // Services
    private readonly INotificationService _notificationService;
    private readonly IUserProfileService _userProfileService;
    private readonly AutoRefresh _unreadRefresh; // NEW: bell badge auto-refresh

    public AppShell(INotificationService notificationService, IUserProfileService userProfileService)
    {
        InitializeComponent();

        _notificationService = notificationService;
        _userProfileService = userProfileService;
        _unreadRefresh = new AutoRefresh(RefreshUnreadCountAsync); // NEW

        RegisterRoutes();

        Loaded += async (_, _) =>
        {
            await EnsureNotificationPermissionAsync();
            await RescheduleNotificationsAsync();
            await RefreshUnreadCountAsync();
            _unreadRefresh.Enabled = true; // NEW
            await LoadUserInitialsAsync();
            UpdateCurrentPageTitle();
        };

        Navigated += async (_, _) =>
        {
            await RefreshUnreadCountAsync();
            UpdateCurrentPageTitle();
        };
    }

    // NEW: "Add" tab par page nahi, popup khulta hai
    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        base.OnNavigating(args);

        var target = args.Target?.Location.OriginalString ?? string.Empty;
        if (args.CanCancel &&
            target.Contains("AddPlaceholderPage", StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel();
            MainThread.BeginInvokeOnMainThread(async () => await ShowAddMenuAsync());
        }
    }

    // NEW
    private async Task ShowAddMenuAsync()
    {
        if (Navigation.ModalStack.Count > 0) return; // double-tap guard
        await Navigation.PushModalAsync(new FabMenuPage(), false);
    }

    // Current page title
    private void UpdateCurrentPageTitle()
    {
        try
        {
            var currentPage = CurrentPage;
            if (currentPage == null)
            {
                CurrentPageTitle = "MoneySpend";
                return;
            }

            // Page ka apna Title pehle
            if (!string.IsNullOrWhiteSpace(currentPage.Title))
            {
                CurrentPageTitle = currentPage.Title;
                return;
            }

            // Fallback: page type se
            CurrentPageTitle = currentPage switch
            {
                DashboardPage => "Dashboard",
                TransactionListPage => "Transactions",
                AccountListPage => "Accounts",
                MorePage => "More",
                AccountEditPage => "Account",
                AddEditTransactionPage => "Transaction",
                BillListPage => "Bills",
                BillEditPage => "Bill",
                RecordBillPaymentPage => "Pay Bill",
                EmiListPage => "EMIs",
                EmiEditPage => "EMI",
                EmiDetailPage => "EMI Details",
                RecordEmiPaymentPage => "Pay EMI",
                BudgetListPage => "Budgets",
                BudgetEditPage => "Budget",
                BorrowLendListPage => "Borrow & Lend",
                BorrowLendEditPage => "Borrow & Lend",
                RecordBorrowLendTransactionPage => "Settle",
                SavingGoalListPage => "Saving Goals",
                SavingGoalEditPage => "Saving Goal",
                SavingGoalContributePage => "Contribute",
                ContactListPage => "Contacts",
                ContactEditPage => "Contact",
                ContactDetailPage => "Contact Details",
                CategoryListPage => "Categories",
                CategoryEditPage => "Category",
                UserProfilePage => "My Profile",
                NotificationListPage => "Notifications",
                BackupRestorePage => "Backup & Restore",
                LoginPage => "Cloud Backup",
                _ => "MoneySpend"
            };
        }
        catch
        {
            CurrentPageTitle = "MoneySpend";
        }
    }

    // User initials
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
        catch { /* profile DB ready na ho to ignore */ }
    }

    // Notifications
    private async Task RefreshUnreadCountAsync()
    {
        try
        {
            var unread = await _notificationService.GetUnreadAsync();
            UnreadNotificationCount = unread.Count;
        }
        catch { /* DB ready na ho to ignore */ }
    }

    private static async Task EnsureNotificationPermissionAsync()
    {
        try
        {
            var isGranted = await LocalNotificationCenter.Current.AreNotificationsEnabled();
            if (!isGranted)
                await LocalNotificationCenter.Current.RequestNotificationPermission();
        }
        catch { /* permission fail ho to app na ruke */ }
    }

    private async Task RescheduleNotificationsAsync()
    {
        try
        {
            // Reboot/force-stop ke baad alarms dobara set karo
            await _notificationService.RescheduleAllPendingAsync();
        }
        catch { /* first launch par DB ready na ho */ }
    }

    private async void OnBellTapped(object? sender, TappedEventArgs e)
        => await GoToAsync(nameof(NotificationListPage));

    // Routes
    private static void RegisterRoutes()
    {
        Routing.RegisterRoute(nameof(AccountEditPage), typeof(AccountEditPage));

        Routing.RegisterRoute(nameof(CategoryListPage), typeof(CategoryListPage));
        Routing.RegisterRoute(nameof(CategoryEditPage), typeof(CategoryEditPage));

        Routing.RegisterRoute(nameof(BillListPage), typeof(BillListPage));
        Routing.RegisterRoute(nameof(BillEditPage), typeof(BillEditPage));
        Routing.RegisterRoute(nameof(RecordBillPaymentPage), typeof(RecordBillPaymentPage));

        Routing.RegisterRoute(nameof(EmiListPage), typeof(EmiListPage));
        Routing.RegisterRoute(nameof(EmiEditPage), typeof(EmiEditPage));
        Routing.RegisterRoute(nameof(RecordEmiPaymentPage), typeof(RecordEmiPaymentPage));
        Routing.RegisterRoute(nameof(EmiDetailPage), typeof(EmiDetailPage));

        Routing.RegisterRoute(nameof(BorrowLendListPage), typeof(BorrowLendListPage));
        Routing.RegisterRoute(nameof(BorrowLendEditPage), typeof(BorrowLendEditPage));
        Routing.RegisterRoute(nameof(RecordBorrowLendTransactionPage), typeof(RecordBorrowLendTransactionPage));

        Routing.RegisterRoute(nameof(SavingGoalListPage), typeof(SavingGoalListPage));
        Routing.RegisterRoute(nameof(SavingGoalEditPage), typeof(SavingGoalEditPage));
        Routing.RegisterRoute(nameof(SavingGoalContributePage), typeof(SavingGoalContributePage));

        Routing.RegisterRoute(nameof(BudgetListPage), typeof(BudgetListPage));
        Routing.RegisterRoute(nameof(BudgetEditPage), typeof(BudgetEditPage));

        Routing.RegisterRoute(nameof(NotificationListPage), typeof(NotificationListPage));

        Routing.RegisterRoute(nameof(ContactListPage), typeof(ContactListPage));
        Routing.RegisterRoute(nameof(ContactEditPage), typeof(ContactEditPage));
        Routing.RegisterRoute(nameof(ContactDetailPage), typeof(ContactDetailPage));

        Routing.RegisterRoute(nameof(UserProfilePage), typeof(UserProfilePage));

        Routing.RegisterRoute(nameof(BackupRestorePage), typeof(BackupRestorePage));
        Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));

        Routing.RegisterRoute(nameof(AddEditTransactionPage), typeof(AddEditTransactionPage));
    }
}