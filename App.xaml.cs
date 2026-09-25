using Arthiva.Data;
using Arthiva.Services;
using Arthiva.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Arthiva;

public partial class App : Application
{
    private readonly ArthivaDatabase _database;
    private readonly IServiceProvider _serviceProvider;

    public App(ArthivaDatabase database, IServiceProvider serviceProvider)
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
            await _database.InitializeAsync();

            var userProfileService = _serviceProvider.GetRequiredService<IUserProfileService>();
            var hasProfile = await userProfileService.ProfileExistsAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!hasProfile)
                {
                    // First install / no profile yet: onboarding gate.
                    var firstTimeSetupPage = _serviceProvider.GetRequiredService<FirstTimeSetupPage>();
                    MainPage = new NavigationPage(firstTimeSetupPage);
                }
                else
                {
                    // Existing user: PIN lock gate. AppShell/Dashboard are
                    // only reachable after AppLockPage verifies the PIN.
                    var appLockPage = _serviceProvider.GetRequiredService<AppLockPage>();
                    MainPage = new NavigationPage(appLockPage);
                }
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "App.InitializeAndRouteAsync");

            // If startup routing itself fails, fail safe to the lock/setup
            // decision again rather than ever falling through to Dashboard.
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                MainPage = new ContentPage
                {
                    BackgroundColor = Color.FromArgb("#FFFFFF"),
                    Content = new Label
                    {
                        Text = "Something went wrong starting Arthiva. Please restart the app.",
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