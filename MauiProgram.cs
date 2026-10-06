using MoneySpend.Data;
using MoneySpend.Services;
using MoneySpend.ViewModels;
using MoneySpend.Views;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;

using INotificationService = MoneySpend.Services.INotificationService;
namespace MoneySpend;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
     CrashLogger.Log(args.ExceptionObject as Exception, "AppDomain.UnhandledException");

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            CrashLogger.Log(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };

        builder
            .UseMauiApp<App>()
            .UseLocalNotification()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        RegisterDatabase(builder.Services);
        RegisterRepositories(builder.Services);
        RegisterServices(builder.Services);
        RegisterViewModels(builder.Services);
        RegisterPages(builder.Services);

        // One Shell instance for the app's lifetime — it owns the
        // notification-badge state shown in the global title bar.
        builder.Services.AddSingleton<AppShell>();
        SwipeHint.Register();
        return builder.Build();
    }

    private static void RegisterDatabase(IServiceCollection services)
    {
        // Singleton: one SQLite connection shared across the whole app lifetime.
        services.AddSingleton(_ =>
        {
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, DatabaseConstants.DatabaseFileName);
            return new MoneySpendDatabase(dbPath);
        });
    }

    private static void RegisterRepositories(IServiceCollection services)
    {
        // Open generic registration — resolves IGenericRepository<Account>,
        // IGenericRepository<Transaction>, etc. automatically wherever needed.
        services.AddSingleton(typeof(IGenericRepository<>), typeof(GenericRepository<>));
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IAccountService, AccountService>();
        services.AddSingleton<ICategoryService, CategoryService>();
        services.AddSingleton<ITransactionService, TransactionService>();
        services.AddSingleton<IContactService, ContactService>();
        services.AddSingleton<IBorrowLendService, BorrowLendService>();
        services.AddSingleton<IEmiService, EmiService>();
        services.AddSingleton<IBillService, BillService>();
        services.AddSingleton<IBudgetService, BudgetService>();
        services.AddSingleton<ISavingGoalService, SavingGoalService>();
        services.AddSingleton<IRecurringTransactionService, RecurringTransactionService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();
        services.AddSingleton<IMonthlySummaryService, MonthlySummaryService>();
        services.AddSingleton<IUserProfileService, UserProfileService>();
        services.AddSingleton<ISplitService, SplitService>();
        services.AddSingleton<ISplitGroupService, SplitGroupService>();
        // App PIN lock — stateless, safe as a singleton.
        services.AddSingleton<IPinService, PinService>();
        services.AddTransient<ChangePinPage>();
        // Cloud backup — Firebase Auth (Email/Password) + Realtime Database sync.
        services.AddSingleton<IFirebaseAuthService, FirebaseAuthService>();
        services.AddSingleton<IFirebaseSyncService, FirebaseSyncService>();

        services.AddSingleton<IBackupDataService, BackupDataService>();
        services.AddSingleton<IGoogleAuthService, GoogleAuthService>();
        services.AddSingleton<IGoogleDriveService, GoogleDriveService>();
        services.AddSingleton<IDriveBackupService, DriveBackupService>();
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        // Transient: a fresh instance every time a page is navigated to, so
        // leftover form state from a previous visit never leaks into a new one.
        services.AddTransient<DashboardViewModel>();

        services.AddTransient<AccountListViewModel>();
        services.AddTransient<AccountEditViewModel>();

        services.AddTransient<TransactionListViewModel>();
        services.AddTransient<AddEditTransactionViewModel>();

        services.AddTransient<CategoryListViewModel>();
        services.AddTransient<CategoryEditViewModel>();

        services.AddTransient<BillListViewModel>();
        services.AddTransient<BillEditViewModel>();
        services.AddTransient<RecordBillPaymentViewModel>();

        services.AddTransient<EmiListViewModel>();
        services.AddTransient<EmiEditViewModel>();
        services.AddTransient<RecordEmiPaymentViewModel>();
        services.AddTransient<EmiDetailViewModel>();

        services.AddTransient<BorrowLendListViewModel>();
        services.AddTransient<BorrowLendEditViewModel>();
        services.AddTransient<RecordBorrowLendTransactionViewModel>();

        services.AddTransient<SavingGoalListViewModel>();
        services.AddTransient<SavingGoalEditViewModel>();
        services.AddTransient<SavingGoalContributeViewModel>();

        services.AddTransient<BudgetListViewModel>();
        services.AddTransient<BudgetEditViewModel>();

        services.AddTransient<NotificationListViewModel>();

        services.AddTransient<ContactListViewModel>();
        services.AddTransient<ContactEditViewModel>();
        services.AddTransient<ContactDetailViewModel>();

        services.AddTransient<UserProfileViewModel>();

        services.AddTransient<AddSplitViewModel>();
        services.AddTransient<SplitListViewModel>();
        services.AddTransient<GroupEditViewModel>();
        services.AddTransient<GroupDetailViewModel>();

        services.AddTransient<DriveBackupViewModel>();


    }

    private static void RegisterPages(IServiceCollection services)
    {
        // NOTE: these Page classes are created in the next step (XAML Pages).
        // Registering them here now means no further MauiProgram changes will
        // be needed once those files are added to the Views/ folder.
        services.AddTransient<DashboardPage>();

        services.AddTransient<AccountListPage>();
        services.AddTransient<AccountEditPage>();

        services.AddTransient<TransactionListPage>();
        services.AddTransient<AddEditTransactionPage>();

        services.AddTransient<CategoryListPage>();
        services.AddTransient<CategoryEditPage>();

        services.AddTransient<BillListPage>();
        services.AddTransient<BillEditPage>();
        services.AddTransient<RecordBillPaymentPage>();

        services.AddTransient<EmiListPage>();
        services.AddTransient<EmiEditPage>();
        services.AddTransient<RecordEmiPaymentPage>();
        services.AddTransient<EmiDetailPage>();

        services.AddTransient<BorrowLendListPage>();
        services.AddTransient<BorrowLendEditPage>();
        services.AddTransient<RecordBorrowLendTransactionPage>();

        services.AddTransient<SavingGoalListPage>();
        services.AddTransient<SavingGoalEditPage>();
        services.AddTransient<SavingGoalContributePage>();

        services.AddTransient<BudgetListPage>();
        services.AddTransient<BudgetEditPage>();

        services.AddTransient<NotificationListPage>();

        services.AddTransient<ContactListPage>();
        services.AddTransient<ContactEditPage>();
        services.AddTransient<ContactDetailPage>();

        services.AddTransient<UserProfilePage>();

        services.AddTransient<MorePage>();

        // App PIN onboarding / lock gate.
        services.AddTransient<FirstTimeSetupPage>();
        services.AddTransient<CreatePinPage>();
        services.AddTransient<AppLockPage>();

        // Cloud backup UI.
        services.AddTransient<LoginPage>();
        services.AddTransient<BackupRestorePage>();

        services.AddTransient<AddSplitPage>();
        services.AddTransient<SplitListPage>();
        services.AddTransient<GroupEditPage>();
        services.AddTransient<GroupDetailPage>();

        services.AddTransient<DriveBackupPage>();
    }
}