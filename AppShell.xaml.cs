using Arthiva.Services;
using Arthiva.Views;

namespace Arthiva;

public partial class AppShell : Shell
{
    public static readonly BindableProperty UnreadNotificationCountProperty =
        BindableProperty.Create(nameof(UnreadNotificationCount), typeof(int), typeof(AppShell), 0);

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

        Loaded += async (_, _) => await RefreshUnreadCountAsync();
        Navigated += async (_, _) => await RefreshUnreadCountAsync();
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
            // Database may not be initialized yet on the very first frame — ignore.
        }
    }

    private async void OnBellTapped(object? sender, TappedEventArgs e)
        => await GoToAsync(nameof(NotificationListPage));

    /// <summary>
    /// Pages reachable via Shell.Current.GoToAsync(...) that are NOT one of
    /// the 5 TabBar entries (those are auto-registered by their Route above).
    /// </summary>
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

        // AddEditTransactionPage also needs a flat route for edit-mode
        // navigation from the Transactions list (the tab route is add-only).
        Routing.RegisterRoute(nameof(AddEditTransactionPage), typeof(AddEditTransactionPage));
    }
}
