namespace MoneySpend.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"v{AppInfo.Current.VersionString}";
    }

    private void OnNotificationSettingsTapped(object sender, TappedEventArgs e)
    {
        try { AppInfo.Current.ShowSettingsUI(); }
        catch { /* platform support na ho to ignore */ }
    }

    private async void OnReplayTipsTapped(object sender, TappedEventArgs e)
    {
        Preferences.Remove("swipe_hint_count");
        await DisplayAlert("Done", "Swipe tip agli baar app kholne par dikhega.", "OK");
    }
}