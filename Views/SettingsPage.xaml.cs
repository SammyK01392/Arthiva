using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.Views;

public partial class SettingsPage : ContentPage
{
    public const string AppLockKey = "app_lock_enabled";

    private readonly IUserProfileService _userProfileService;
    private readonly IPinService _pinService;
    private bool _loading = true;

    public SettingsPage(IUserProfileService userProfileService, IPinService pinService)
    {
        InitializeComponent();
        _userProfileService = userProfileService;
        _pinService = pinService;
        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
    }

    // Lock is on only if the user turned it on. Older users who already
    // have a PIN (and never touched the setting) stay protected.
    public static bool IsLockEnabled(UserProfile? profile)
    {
        var hasPin = !string.IsNullOrEmpty(profile?.PinHash);
        if (!hasPin) return false;
        return Preferences.ContainsKey(AppLockKey) ? Preferences.Get(AppLockKey, false) : true;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var profile = await _userProfileService.GetProfileAsync();
            SetSwitchSilently(IsLockEnabled(profile));
        }
        catch (Exception ex) { CrashLogger.Log(ex, "SettingsPage.OnAppearing"); }
    }

    private async void OnAppLockToggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;

        try
        {
            var profile = await _userProfileService.GetProfileAsync();
            var hasPin = !string.IsNullOrEmpty(profile?.PinHash);

            if (e.Value)
            {
                if (hasPin)
                {
                    Preferences.Set(AppLockKey, true);
                    return;
                }

                // No PIN yet: let the user create one first
                SetSwitchSilently(false);
                await Shell.Current.GoToAsync(nameof(ChangePinPage));
                return;
            }

            // Turning OFF needs the current PIN
            if (await VerifyPinAsync(profile?.PinHash ?? string.Empty))
            {
                Preferences.Set(AppLockKey, false);
                await DisplayAlert("App Lock off", "The app will now open without a PIN.", "OK");
            }
            else
            {
                SetSwitchSilently(true);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "SettingsPage.OnAppLockToggled");
            SetSwitchSilently(!e.Value);
        }
    }

    private async Task<bool> VerifyPinAsync(string stored)
    {
        var entered = await DisplayPromptAsync(
            "Confirm PIN",
            "Enter your current 4-digit PIN to turn off App Lock.",
            accept: "Confirm", cancel: "Cancel",
            placeholder: "••••", maxLength: 4, keyboard: Keyboard.Numeric);

        if (string.IsNullOrEmpty(entered)) return false;

        var correct = await Task.Run(() => _pinService.VerifyPin(entered, stored));
        if (!correct)
            await DisplayAlert("Incorrect PIN", "The PIN you entered is wrong. App Lock stays on.", "OK");
        return correct;
    }

    private void SetSwitchSilently(bool value)
    {
        _loading = true;
        AppLockSwitch.IsToggled = value;
        _loading = false;
    }

    private void OnNotificationSettingsTapped(object sender, TappedEventArgs e)
    {
        try { AppInfo.Current.ShowSettingsUI(); }
        catch { }
    }

    private async void OnReplayTipsTapped(object sender, TappedEventArgs e)
    {
        Preferences.Remove("swipe_hint_count");
        await DisplayAlert("Done", "The swipe tip will show again next time you open the app.", "OK");
    }
}