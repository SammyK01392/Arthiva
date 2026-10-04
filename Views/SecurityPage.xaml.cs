namespace MoneySpend.Views;

public partial class SecurityPage : ContentPage
{
    private bool _loading = true;

    public SecurityPage()
    {
        InitializeComponent();
        HideBalanceSwitch.IsToggled = Preferences.Get("hide_balance_default", false);
        _loading = false;
    }

    private void OnHideBalanceToggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Set("hide_balance_default", e.Value);
    }


    private async void OnPinTapped(object sender, TappedEventArgs e)
    => await Shell.Current.GoToAsync(nameof(ChangePinPage));

    private async void OnBackupTapped(object sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(BackupRestorePage));
}