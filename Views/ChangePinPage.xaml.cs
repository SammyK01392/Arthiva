using MoneySpend.Controls;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.Views;

public partial class ChangePinPage : ContentPage
{
    private enum Stage { Current, New, Confirm }

    private readonly IUserProfileService _userProfileService;
    private readonly IGenericRepository<UserProfile> _profileRepo;
    private readonly IPinService _pinService;

    private UserProfile? _profile;
    private Stage _stage = Stage.Current;
    private string _newPin = string.Empty;
    private bool _busy;

    public ChangePinPage(
        IUserProfileService userProfileService,
        IGenericRepository<UserProfile> profileRepo,
        IPinService pinService)
    {
        InitializeComponent();
        _userProfileService = userProfileService;
        _profileRepo = profileRepo;
        _pinService = pinService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _profile = await _userProfileService.GetProfileAsync();
            // If no PIN exists yet, skip the current-PIN step
            SetStage(string.IsNullOrEmpty(_profile?.PinHash) ? Stage.New : Stage.Current);
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "ChangePinPage.OnAppearing");
            ShowError("Couldn't load your profile. Please try again.");
        }
    }

    private void SetStage(Stage stage)
    {
        _stage = stage;
        ErrorLabel.IsVisible = false;
        (HeaderLabel.Text, SubHeaderLabel.Text) = stage switch
        {
            Stage.Current => ("Enter current PIN", "Enter your existing 4-digit PIN to continue."),
            Stage.New => ("Create new PIN", "Choose a new 4-digit PIN."),
            _ => ("Confirm new PIN", "Re-enter the new PIN to confirm.")
        };
        ClearPad();
    }

    private void ClearPad()
        => PinPad.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), PinPad.Clear);

    private void ShowError(string text)
    {
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
    }

    private async void OnPinPadChanged(object? sender, string pin)
    {
        if (pin.Length < PinPadView.PinLength)
        {
            ErrorLabel.IsVisible = false;
            return;
        }
        if (_busy) return;

        try
        {
            _busy = true;
            switch (_stage)
            {
                case Stage.Current:
                    var stored = _profile?.PinHash ?? string.Empty;
                    var ok = await Task.Run(() => _pinService.VerifyPin(pin, stored));
                    if (ok)
                    {
                        SetStage(Stage.New);
                    }
                    else
                    {
                        ShowError("Incorrect PIN. Please try again.");
                        ClearPad();
                    }
                    break;

                case Stage.New:
                    _newPin = pin;
                    SetStage(Stage.Confirm);
                    break;

                case Stage.Confirm:
                    if (pin != _newPin)
                    {
                        _newPin = string.Empty;
                        SetStage(Stage.New);
                        ShowError("PINs don't match. Please try again.");
                        break;
                    }
                    await SaveAsync(pin);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "ChangePinPage.OnPinPadChanged");
            ShowError("Something went wrong. Please try again.");
            ClearPad();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SaveAsync(string pin)
    {
        if (_profile is null)
        {
            ShowError("Profile not found.");
            return;
        }

        var hash = await Task.Run(() => _pinService.HashPin(pin));
        _profile.PinHash = hash;
        await _profileRepo.UpdateAsync(_profile);
        Preferences.Set(SettingsPage.AppLockKey, true);
        await DisplayAlert("PIN updated", "Your PIN has been changed successfully.", "OK");
        await Shell.Current.GoToAsync("..");
    }
}