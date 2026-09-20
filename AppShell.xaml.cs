using Arthiva.Services;
using Arthiva.Views;

namespace Arthiva;

public partial class AppShell : Shell
{
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

    private readonly INotificationService _notificationService;

    public AppShell(INotificationService notificationService)
    {
        InitializeComponent();

        _notificationService = notificationService;

        RegisterRoutes();

        Loaded += async (_, _) =>
            await RefreshUnreadCountAsync();

        Navigated += async (_, _) =>
            await RefreshUnreadCountAsync();
    }


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
            // during the first application frame.
        }
    }


    private async void OnBellTapped(
        object? sender,
        TappedEventArgs e)
    {
        await GoToAsync(nameof(NotificationListPage));
    }


    /// <summary>
    /// Registers pages that are opened through Shell navigation
    /// and are not direct bottom-tab pages.
    /// </summary>
    private static void RegisterRoutes()
    {
        Routing.RegisterRoute(
            nameof(AccountEditPage),
            typeof(AccountEditPage));


        // Categories

        Routing.RegisterRoute(
            nameof(CategoryListPage),
            typeof(CategoryListPage));

        Routing.RegisterRoute(
            nameof(CategoryEditPage),
            typeof(CategoryEditPage));


        // Bills

        Routing.RegisterRoute(
            nameof(BillListPage),
            typeof(BillListPage));

        Routing.RegisterRoute(
            nameof(BillEditPage),
            typeof(BillEditPage));

        Routing.RegisterRoute(
            nameof(RecordBillPaymentPage),
            typeof(RecordBillPaymentPage));


        // EMI

        Routing.RegisterRoute(
            nameof(EmiListPage),
            typeof(EmiListPage));

        Routing.RegisterRoute(
            nameof(EmiEditPage),
            typeof(EmiEditPage));

        Routing.RegisterRoute(
            nameof(RecordEmiPaymentPage),
            typeof(RecordEmiPaymentPage));
        Routing.RegisterRoute(nameof(EmiDetailPage), typeof(EmiDetailPage));

        // Borrow / Lend

        Routing.RegisterRoute(
            nameof(BorrowLendListPage),
            typeof(BorrowLendListPage));

        Routing.RegisterRoute(
            nameof(BorrowLendEditPage),
            typeof(BorrowLendEditPage));

        Routing.RegisterRoute(
            nameof(RecordBorrowLendTransactionPage),
            typeof(RecordBorrowLendTransactionPage));


        // Saving Goals

        Routing.RegisterRoute(
            nameof(SavingGoalListPage),
            typeof(SavingGoalListPage));

        Routing.RegisterRoute(
            nameof(SavingGoalEditPage),
            typeof(SavingGoalEditPage));

        Routing.RegisterRoute(
            nameof(SavingGoalContributePage),
            typeof(SavingGoalContributePage));


        // Budget

        Routing.RegisterRoute(
            nameof(BudgetListPage),
            typeof(BudgetListPage));

        Routing.RegisterRoute(
            nameof(BudgetEditPage),
            typeof(BudgetEditPage));


        // Notifications

        Routing.RegisterRoute(
            nameof(NotificationListPage),
            typeof(NotificationListPage));


        // Contacts

        Routing.RegisterRoute(
            nameof(ContactListPage),
            typeof(ContactListPage));

        Routing.RegisterRoute(
            nameof(ContactEditPage),
            typeof(ContactEditPage));

        Routing.RegisterRoute(
            nameof(ContactDetailPage),
            typeof(ContactDetailPage));


        // Profile

        Routing.RegisterRoute(
            nameof(UserProfilePage),
            typeof(UserProfilePage));


        // Transaction Add/Edit

        Routing.RegisterRoute(
            nameof(AddEditTransactionPage),
            typeof(AddEditTransactionPage));
    }
}