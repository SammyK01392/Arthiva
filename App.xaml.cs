using MoneySpend.Data;
using MoneySpend.Services;
using MoneySpend.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MoneySpend;

public partial class App : Application
{
    private readonly MoneySpendDatabase _database;
    private readonly IServiceProvider _serviceProvider;

    public App(MoneySpendDatabase database, IServiceProvider serviceProvider)
    {
        InitializeComponent();          // loads Colors.xaml/Styles.xaml into App resources FIRST

        _database = database;
        _serviceProvider = serviceProvider;

        // IMPORTANT: we deliberately do NOT resolve AppShell here anymore.
        // Resolving it immediately (as before) meant Dashboard could be
        // constructed — and briefly visible — before we know whether a
        // PIN lock even needs to be shown. Instead we show a blank,
        // branded placeholder for the brief moment it takes to initialize
        // SQLite and decide where to route the user, then swap MainPage
        // to the correct destination. AppShell itself is still a DI
        // singleton (see MauiProgram.cs) and is only ever constructed
        // once, the first time it's actually needed.
        MainPage = new ContentPage
        {
            BackgroundColor = Color.FromArgb("#FFFFFF")
        };
    }

    protected override void OnStart()
        => _ = InitializeAndRouteAsync();

    private async Task InitializeAndRouteAsync()
    {
        try
        {
            // 1. Database must be fully ready first
            await _database.InitializeAsync();

            // 2. NOW start shared/Firebase sync coordinator
            _ = Task.Run(() =>
                _serviceProvider
                    .GetService<ISharedSyncCoordinator>()
                    ?.StartAsync());

            // 3. Continue normal startup/routing
            var userProfileService =
                _serviceProvider.GetRequiredService<IUserProfileService>();

            var hasProfile = await userProfileService.ProfileExistsAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!hasProfile)
                {
                    var firstTimeSetupPage =
                        _serviceProvider.GetRequiredService<FirstTimeSetupPage>();

                    MainPage = new NavigationPage(firstTimeSetupPage);
                }
                else
                {
                    var appLockPage =
                        _serviceProvider.GetRequiredService<AppLockPage>();

                    MainPage = new NavigationPage(appLockPage);
                }
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "App.InitializeAndRouteAsync");

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                MainPage = new ContentPage
                {
                    BackgroundColor = Color.FromArgb("#FFFFFF"),
                    Content = new Label
                    {
                        Text = "Something went wrong starting MoneySpend. Please restart the app.",
                        Margin = 24,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.Center,
                        TextColor = Color.FromArgb("#1A1410")
                    }
                };
            });
        }
    }
}