using System;
using System.Collections.Generic;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class DriveBackupViewModel : ObservableObject
{
    private readonly IDriveBackupService _drive;

    public DriveBackupViewModel(IDriveBackupService drive) => _drive = drive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    private bool _isBusy;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDisconnected))]
    private bool _isConnected;
    public bool IsDisconnected => !IsConnected;

    [ObservableProperty] private string _accountText = "Not connected";
    [ObservableProperty] private string _lastBackupText = "Never";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _passphrase = "";
    [ObservableProperty] private bool _rememberPassphrase;

    private bool CanRun() => !IsBusy;

    public async Task LoadAsync()
    {
        var s = await _drive.GetStatusAsync();
        IsConnected = s.IsConnected;
        AccountText = s.IsConnected ? s.Email ?? "Connected" : "Not connected";
        LastBackupText = s.LastBackupLocal?.ToString("dd MMM yyyy, hh:mm tt") ?? "Never";
        StatusText = s.LastStatus;
        RememberPassphrase = s.HasSavedPassphrase;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ConnectAsync() => RunAsync(async () =>
    {
        var acc = await _drive.ConnectAsync();
        await Alert("Connected", $"Signed in as {acc.Email}.");
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DisconnectAsync() => RunAsync(async () =>
    {
        if (!await Confirm("Disconnect",
            "Sign out of Google Drive? Your existing cloud backup stays in Drive.", "Disconnect")) return;
        await _drive.DisconnectAsync();
        Passphrase = "";
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task BackupAsync() => RunAsync(async () =>
    {
        await _drive.BackupAsync(Passphrase, Progress());
        await PersistPassphraseChoiceAsync();
        await Alert("Backup complete", "Your data was encrypted and saved to Google Drive.");
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RestoreAsync() => RunAsync(async () =>
    {
        if (!await Confirm("Restore from Google Drive",
            "This replaces ALL data on this phone with the Drive copy. A safety copy of your current data is saved first.",
            "Restore")) return;
        await _drive.RestoreAsync(Passphrase, Progress());
        await PersistPassphraseChoiceAsync();
        await Alert("Restore complete", "Your data was restored. Reopen screens to see the latest data.");
    });

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task SyncAsync() => RunAsync(async () =>
    {
        var outcome = await _drive.SyncAsync(Passphrase, Progress());
        await PersistPassphraseChoiceAsync();
        switch (outcome)
        {
            case DriveSyncOutcome.UpToDate: await Alert("Sync", "Already up to date."); break;
            case DriveSyncOutcome.Uploaded: await Alert("Sync", "This phone's changes were uploaded to Drive."); break;
            case DriveSyncOutcome.Downloaded: await Alert("Sync", "Newer data from Drive was applied to this phone."); break;
            case DriveSyncOutcome.Conflict:
                var choice = await Shell.Current.DisplayActionSheet(
                    "Both this phone and Drive have changes", "Cancel", null,
                    "Keep this phone's data (overwrite Drive)",
                    "Use Drive's data (replace this phone)");
                if (choice?.StartsWith("Keep") == true) await _drive.BackupAsync(Passphrase, Progress());
                else if (choice?.StartsWith("Use") == true) await _drive.RestoreAsync(Passphrase, Progress());
                break;
        }
    });

    private async Task PersistPassphraseChoiceAsync()
    {
        if (RememberPassphrase && !string.IsNullOrWhiteSpace(Passphrase))
            await _drive.RememberPassphraseAsync(Passphrase);
        else if (!RememberPassphrase)
            await _drive.ForgetPassphraseAsync();
    }

    private IProgress<string> Progress() => new Progress<string>(m => ProgressText = m);

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (DriveBackupException ex) when (ex.Kind == DriveErrorKind.Cancelled) { }
        catch (DriveBackupException ex)
        {
            await Alert(ex.Kind switch
            {
                DriveErrorKind.Offline => "You're offline",
                DriveErrorKind.WrongPassphrase => "Wrong passphrase",
                DriveErrorKind.AuthExpired or DriveErrorKind.NotSignedIn => "Sign-in required",
                _ => "Google Drive"
            }, ex.Message);
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "DriveBackupViewModel");
            await Alert("Google Drive", "Something went wrong. Please try again.");
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
            await LoadAsync();
        }
    }

    private static Task Alert(string t, string m) => Shell.Current.DisplayAlert(t, m, "OK");
    private static Task<bool> Confirm(string t, string m, string ok) => Shell.Current.DisplayAlert(t, m, ok, "Cancel");
}
