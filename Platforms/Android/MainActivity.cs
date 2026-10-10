using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Firebase;
using Microsoft.Maui;
using MoneySpend.Services;

namespace MoneySpend
{
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges =
            ConfigChanges.ScreenSize |
            ConfigChanges.Orientation |
            ConfigChanges.UiMode |
            ConfigChanges.ScreenLayout |
            ConfigChanges.SmallestScreenSize |
            ConfigChanges.Density
    )]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var app = FirebaseApp.InitializeApp(this);

            Log.Info(
                "MoneySpendFCM",
                app == null
                    ? "Firebase initialization returned NULL"
                    : $"Firebase initialized: {app.Name}"
            );

            InviteLinkBridge.Handle(base.Intent?.DataString);
        }
    }
}