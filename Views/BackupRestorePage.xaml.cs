using System;
using System.Collections.Generic;
using System.Text;

using MoneySpend.Services;

namespace MoneySpend.Views;

public partial class BackupRestorePage : ContentPage
{
    private readonly IFirebaseAuthService _firebaseAuthService;
    private readonly IFirebaseSyncService _firebaseSyncService;
    private bool _isBusy;

    public BackupRestorePage(
        IFirebaseAuthService firebaseAuthService,
        IFirebaseSyncService firebaseSyncService)
    {
        InitializeComponent();
        _firebaseAuthService = firebaseAuthService;
        _firebaseSyncService = firebaseSyncService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshStateAsync();
    }

    private async Task RefreshStateAsync()
    {
        var isLoggedIn = await _firebaseAuthService.IsLoggedInAsync();

        LoginButtonBorder.IsVisible = !isLoggedIn;
        BackupButtonBorder.IsVisible = isLoggedIn;
        RestoreButtonBorder.IsVisible = isLoggedIn;
        LogoutButtonBorder.IsVisible = isLoggedIn;

        AccountStatusLabel.Text = isLoggedIn ? "Logged in" : "Not logged in";

        if (!isLoggedIn)
        {
            LastBackupLabel.Text = "Cloud backup ke liye login karein.";
            return;
        }

        LastBackupLabel.Text = "Checking last backup...";
        var lastBackup = await _firebaseSyncService.GetLastBackupTimeAsync();
        LastBackupLabel.Text = lastBackup is null
            ? "Abhi tak koi backup nahi hua."
            : $"Last backup: {lastBackup:dd MMM yyyy, hh:mm tt}";
    }

    private async void OnLoginTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(LoginPage));

    private async void OnLogoutTapped(object? sender, TappedEventArgs e)
    {
        await _firebaseAuthService.SignOutAsync();
        await RefreshStateAsync();
    }

    private async void OnBackupTapped(object? sender, TappedEventArgs e)
    {
        if (_isBusy)
            return;

        try
        {
            _isBusy = true;
            SetBusy(BackupButtonBorder, BackupBusyIndicator, true);

            await _firebaseSyncService.BackupAsync();
            await DisplayAlert("Success", "Backup cloud par safal ho gaya.", "OK");
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            _isBusy = false;
            SetBusy(BackupButtonBorder, BackupBusyIndicator, false);
        }
    }

    private async void OnRestoreTapped(object? sender, TappedEventArgs e)
    {
        if (_isBusy)
            return;

        var confirmed = await DisplayAlert(
            "Restore karein?",
            "Ye aapke device ka current data delete karke cloud backup se replace kar dega. Continue karein?",
            "Yes, restore",
            "Cancel");

        if (!confirmed)
            return;

        try
        {
            _isBusy = true;
            SetBusy(RestoreButtonBorder, RestoreBusyIndicator, true);

            await _firebaseSyncService.RestoreAsync();
            await DisplayAlert("Success", "Data restore ho gaya.", "OK");
            await RefreshStateAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            _isBusy = false;
            SetBusy(RestoreButtonBorder, RestoreBusyIndicator, false);
        }
    }
    private async void OnDriveBackupTapped(object? sender, TappedEventArgs e)
    => await Shell.Current.GoToAsync(nameof(DriveBackupPage));
    private static void SetBusy(Border border, ActivityIndicator indicator, bool busy)
    {
        border.IsEnabled = !busy;
        border.Opacity = busy ? 0.7 : 1.0;
        indicator.IsVisible = busy;
        indicator.IsRunning = busy;
    }
}