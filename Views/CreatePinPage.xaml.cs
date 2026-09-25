using Arthiva.Controls;
using Arthiva.Models;
using Arthiva.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Arthiva.Views;

/// <summary>
/// Handles both "create PIN" and "confirm PIN" as two stages of one page
/// (rather than two separate pages/nav pushes) — the UI is identical
/// (branding + PinPadView), only the heading text and behaviour on
/// completion change. Reached only from FirstTimeSetupPage, so there is
/// no existing UserProfile yet and no Dashboard to accidentally reveal.
/// </summary>
public partial class CreatePinPage : ContentPage
{
    private enum Stage
    {
        CreatePin,
        ConfirmPin
    }

    private readonly IUserProfileService _userProfileService;
    private readonly IPinService _pinService;
    private readonly IServiceProvider _serviceProvider;

    private string _fullName = string.Empty;
    private string _mobileNo = string.Empty;
    private string _firstEnteredPin = string.Empty;
    private Stage _stage = Stage.CreatePin;
    private bool _isSaving;

    public CreatePinPage(
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

    /// <summary>Must be called right after this page is resolved from DI, before it is shown.</summary>
    public void Initialize(string fullName, string mobileNo)
    {
        _fullName = fullName;
        _mobileNo = mobileNo;
    }

    private async void OnPinPadChanged(object? sender, string currentPin)
    {
        if (currentPin.Length < PinPadView.PinLength)
        {
            ErrorLabel.IsVisible = false;
            return;
        }

        switch (_stage)
        {
            case Stage.CreatePin:
                _firstEnteredPin = currentPin;
                MoveToConfirmStage();
                break;

            case Stage.ConfirmPin:
                await HandleConfirmAsync(currentPin);
                break;
        }
    }

    private void MoveToConfirmStage()
    {
        _stage = Stage.ConfirmPin;
        ErrorLabel.IsVisible = false;
        HeaderLabel.Text = "Confirm your PIN";
        SubHeaderLabel.Text = "Re-enter the same 4-digit PIN to confirm.";

        // Small delay so the 4th dot fill is visible before resetting.
        PinPad.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), PinPad.Clear);
    }

    private async Task HandleConfirmAsync(string confirmedPin)
    {
        if (_isSaving)
            return;

        if (confirmedPin != _firstEnteredPin)
        {
            ErrorLabel.Text = "PINs don't match. Please try again.";
            ErrorLabel.IsVisible = true;

            // Safely discard both attempts and restart from the beginning
            // rather than silently retrying confirmation against a PIN the
            // user has apparently mistyped or forgotten.
            _firstEnteredPin = string.Empty;
            _stage = Stage.CreatePin;
            HeaderLabel.Text = "Create your App PIN";
            SubHeaderLabel.Text = "Choose a 4-digit PIN to protect your financial data.";

            PinPad.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), PinPad.Clear);
            return;
        }

        try
        {
            _isSaving = true;

            var pinHash = await Task.Run(() => _pinService.HashPin(confirmedPin));

            var profile = new UserProfile
            {
                FullName = _fullName,
                MobileNo = _mobileNo,
                CurrencyCode = "INR",
                PinHash = pinHash,
                IsActive = true
            };

            await _userProfileService.CreateAsync(profile);

            // First-run setup complete — hand off to the real app shell.
            // AppShell is a DI singleton, so this resolves the same
            // instance the rest of the app expects, created for the first
            // time right here.
            var appShell = _serviceProvider.GetRequiredService<AppShell>();
            Application.Current!.MainPage = appShell;
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "CreatePinPage.HandleConfirmAsync");

            ErrorLabel.Text = "Something went wrong saving your PIN. Please try again.";
            ErrorLabel.IsVisible = true;

            _firstEnteredPin = string.Empty;
            _stage = Stage.CreatePin;
            HeaderLabel.Text = "Create your App PIN";
            SubHeaderLabel.Text = "Choose a 4-digit PIN to protect your financial data.";
            PinPad.Clear();
        }
        finally
        {
            _isSaving = false;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        // Deliberately simplified: PIN setup can't be backed out of once
        // started (matches common payment-app UX). The user can force-close
        // the app instead; nothing sensitive is reachable from here since
        // AppShell has not been created yet.
        return true;
    }
}
