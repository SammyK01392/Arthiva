using Arthiva.Controls;
using Arthiva.Models;
using Arthiva.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Arthiva.Views;

/// <summary>
/// Gate page shown on every normal launch once a UserProfile already
/// exists. Nothing about Dashboard/AppShell is touched until the PIN is
/// verified. This page is the root of its own NavigationPage (see
/// App.xaml.cs) with hardware Back blocked, so it can't be bypassed.
/// </summary>
public partial class AppLockPage : ContentPage
{
    private readonly IUserProfileService _userProfileService;
    private readonly IPinService _pinService;
    private readonly IServiceProvider _serviceProvider;

    private UserProfile? _profile;
    private bool _isVerifying;

    public AppLockPage(
        IUserProfileService userProfileService,
        IPinService pinService,
        IServiceProvider serviceProvider)
    {
        InitializeComponent();

        _userProfileService = userProfileService;
        _pinService = pinService;
        _serviceProvider = serviceProvider;

        NavigationPage.SetHasNavigationBar(this, false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        PinPad.Clear();
        ErrorLabel.IsVisible = false;

        try
        {
            _profile = await _userProfileService.GetProfileAsync();
            UserNameLabel.Text = string.IsNullOrWhiteSpace(_profile?.FullName)
                ? "Arthiva User"
                : _profile.FullName;
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "AppLockPage.OnAppearing");
            UserNameLabel.Text = "Arthiva User";
        }
    }

    private async void OnPinPadChanged(object? sender, string currentPin)
    {
        if (currentPin.Length < PinPadView.PinLength)
        {
            ErrorLabel.IsVisible = false;
            return;
        }

        await VerifyAsync(currentPin);
    }

    private async Task VerifyAsync(string enteredPin)
    {
        // Guards against a fast double-tap on the last digit triggering
        // two overlapping verification attempts.
        if (_isVerifying)
            return;

        try
        {
            _isVerifying = true;
            SetBusy(true);

            if (_profile is null)
            {
                _profile = await _userProfileService.GetProfileAsync();
            }

            var storedHash = _profile?.PinHash ?? string.Empty;

            var isCorrect = await Task.Run(() => _pinService.VerifyPin(enteredPin, storedHash));

            if (isCorrect)
            {
                var appShell = _serviceProvider.GetRequiredService<AppShell>();
                Application.Current!.MainPage = appShell;
                return;
            }

            ErrorLabel.Text = "Incorrect PIN. Please try again.";
            ErrorLabel.IsVisible = true;
            PinPad.Clear();
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "AppLockPage.VerifyAsync");
            ErrorLabel.Text = "Something went wrong. Please try again.";
            ErrorLabel.IsVisible = true;
            PinPad.Clear();
        }
        finally
        {
            _isVerifying = false;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        VerifyingIndicator.IsVisible = busy;
        VerifyingIndicator.IsRunning = busy;
    }

    protected override bool OnBackButtonPressed()
    {
        // Block hardware Back entirely so the lock screen can never be
        // bypassed on the way to AppShell/Dashboard. The user minimizes
        // the app via the Home button instead.
        return true;
    }
}
