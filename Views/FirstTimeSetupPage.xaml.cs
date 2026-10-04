using Microsoft.Extensions.DependencyInjection;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.Views;

/// <summary>
/// First screen shown when no UserProfile exists yet. Collects Full Name
/// and Mobile Number, creates the profile (no PIN by default) and opens
/// the app directly. A PIN can be enabled later from Settings > App Lock.
/// </summary>
public partial class FirstTimeSetupPage : ContentPage
{
    private readonly IServiceProvider _serviceProvider;
    private bool _isSubmitting;

    public FirstTimeSetupPage(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        NavigationPage.SetHasNavigationBar(this, false);
    }

    private void OnFullNameChanged(object? sender, TextChangedEventArgs e)
        => FullNameErrorLabel.IsVisible = false;

    private void OnMobileNoChanged(object? sender, TextChangedEventArgs e)
    {
        MobileNoErrorLabel.IsVisible = false;

        // Keep only digits, max 10
        var digitsOnly = new string(Array.FindAll((e.NewTextValue ?? string.Empty).ToCharArray(), char.IsDigit));
        if (digitsOnly.Length > 10)
            digitsOnly = digitsOnly[..10];

        if (digitsOnly != e.NewTextValue)
            MobileNoEntry.Text = digitsOnly;
    }

    private async void OnContinueTapped(object? sender, TappedEventArgs e)
    {
        if (_isSubmitting)
            return;

        var fullName = (FullNameEntry.Text ?? string.Empty).Trim();
        var mobileNo = (MobileNoEntry.Text ?? string.Empty).Trim();

        var isValid = true;

        if (string.IsNullOrWhiteSpace(fullName))
        {
            FullNameErrorLabel.Text = "Full name is required.";
            FullNameErrorLabel.IsVisible = true;
            isValid = false;
        }

        if (mobileNo.Length != 10 || !IsAllDigits(mobileNo))
        {
            MobileNoErrorLabel.Text = "Enter a valid 10-digit mobile number.";
            MobileNoErrorLabel.IsVisible = true;
            isValid = false;
        }

        if (!isValid)
            return;

        try
        {
            _isSubmitting = true;
            SetBusy(true);

            // No PIN by default: create the profile and open the app directly
            var userProfileService = _serviceProvider.GetRequiredService<IUserProfileService>();
            await userProfileService.CreateAsync(new UserProfile
            {
                FullName = fullName,
                MobileNo = mobileNo,
                CurrencyCode = "INR",
                PinHash = string.Empty,
                IsActive = true
            });

            // App Lock stays off until the user enables it in Settings
            Preferences.Set(SettingsPage.AppLockKey, false);

            var appShell = _serviceProvider.GetRequiredService<AppShell>();
            Application.Current!.MainPage = appShell;
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "FirstTimeSetupPage.OnContinueTapped");
            await DisplayAlert("Something went wrong",
                "Couldn't save your details. Please try again.", "OK");
        }
        finally
        {
            _isSubmitting = false;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ContinueButtonBorder.Opacity = busy ? 0.7 : 1.0;
        ContinueButtonBorder.IsEnabled = !busy;
        ContinueBusyIndicator.IsVisible = busy;
        ContinueBusyIndicator.IsRunning = busy;
    }

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }
        return true;
    }
}