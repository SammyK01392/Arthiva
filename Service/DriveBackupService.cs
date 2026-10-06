using System;
using System.Collections.Generic;


using System.Globalization;
using System.Text;

namespace MoneySpend.Services;

public interface IDriveBackupService
{
    Task<DriveBackupStatus> GetStatusAsync();
    Task<DriveFileInfo?> GetRemoteBackupInfoAsync();
    Task<GoogleAccount> ConnectAsync();
    Task DisconnectAsync();
    Task BackupAsync(string? passphrase, IProgress<string>? progress = null);
    Task RestoreAsync(string? passphrase, IProgress<string>? progress = null);
    Task<DriveSyncOutcome> SyncAsync(string? passphrase, IProgress<string>? progress = null);
    Task RememberPassphraseAsync(string passphrase);
    Task ForgetPassphraseAsync();
}

public class DriveBackupService : IDriveBackupService
{
    private const string PrefLastBackup = "drive_last_backup_utc";
    private const string PrefLastStatus = "drive_last_status";
    private const string PrefSyncedHash = "drive_synced_hash";
    private const string KeyPassphrase = "drive_backup_passphrase";
    private const int MinPassphraseLength = 8;

    private readonly IGoogleAuthService _auth;
    private readonly IGoogleDriveService _drive;
    private readonly IBackupDataService _data;
    private readonly INotificationService _notifications;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DriveBackupService(IGoogleAuthService auth, IGoogleDriveService drive,
                              IBackupDataService data, INotificationService notifications)
    {
        _auth = auth; _drive = drive; _data = data; _notifications = notifications;
    }

    // ── Status / account ───────────────────────────────────
    public async Task<DriveBackupStatus> GetStatusAsync()
    {
        var connected = await _auth.IsSignedInAsync();
        DateTime? last = DateTime.TryParse(Preferences.Default.Get(PrefLastBackup, ""), null,
            DateTimeStyles.RoundtripKind, out var dt) ? dt.ToLocalTime() : null;
        var hasPass = !string.IsNullOrEmpty(await SecureStorage.GetAsync(KeyPassphrase));
        return new DriveBackupStatus(connected, connected ? await _auth.GetAccountEmailAsync() : null,
            last, Preferences.Default.Get(PrefLastStatus, "No backup yet"), hasPass);
    }

    public async Task<DriveFileInfo?> GetRemoteBackupInfoAsync()
    {
        if (!await _auth.IsSignedInAsync()) return null;
        try { return await _drive.FindBackupAsync(); } catch { return null; }
    }

    public Task<GoogleAccount> ConnectAsync() => _auth.SignInAsync();

    public async Task DisconnectAsync()
    {
        await _auth.SignOutAsync();
        Preferences.Default.Remove(PrefSyncedHash);
        await ForgetPassphraseAsync();
    }

    public Task RememberPassphraseAsync(string passphrase) => SecureStorage.SetAsync(KeyPassphrase, passphrase);
    public Task ForgetPassphraseAsync() { SecureStorage.Remove(KeyPassphrase); return Task.CompletedTask; }

    // ── Public operations (serialized by a gate) ───────────
    public Task BackupAsync(string? passphrase, IProgress<string>? progress = null)
        => RunExclusive(async () =>
        {
            await EnsureConnectedAsync();
            var pass = await ResolvePassphraseAsync(passphrase, enforceStrength: true);
            progress?.Report("Reading local data...");
            var snap = await _data.CreateSnapshotAsync();
            progress?.Report("Checking Google Drive...");
            var existing = await _drive.FindBackupAsync();   // update in place → never duplicates
            await BackupCoreAsync(pass, snap, existing, progress);
            return DriveSyncOutcome.Uploaded;
        });

    public Task RestoreAsync(string? passphrase, IProgress<string>? progress = null)
        => RunExclusive(async () =>
        {
            await EnsureConnectedAsync();
            var pass = await ResolvePassphraseAsync(passphrase, enforceStrength: false);
            progress?.Report("Checking Google Drive...");
            var remote = await _drive.FindBackupAsync()
                ?? throw new DriveBackupException(DriveErrorKind.NoBackup, "No backup found on Google Drive.");
            await RestoreCoreAsync(pass, remote, progress);
            return DriveSyncOutcome.Downloaded;
        });

    public Task<DriveSyncOutcome> SyncAsync(string? passphrase, IProgress<string>? progress = null)
        => RunExclusive(async () =>
        {
            await EnsureConnectedAsync();
            var pass = await ResolvePassphraseAsync(passphrase, enforceStrength: false);
            progress?.Report("Comparing local and cloud data...");
            var snap = await _data.CreateSnapshotAsync();
            var remote = await _drive.FindBackupAsync();

            if (remote is null)
            {
                await BackupCoreAsync(pass, snap, null, progress);
                return DriveSyncOutcome.Uploaded;
            }

            if (remote.ContentHash == snap.ContentHash)
            {
                MarkSynced(snap.ContentHash, remote.ModifiedUtc);
                RecordStatus("Already in sync");
                return DriveSyncOutcome.UpToDate;
            }

            var lastHash = Preferences.Default.Get(PrefSyncedHash, "");
            var localChanged = lastHash != snap.ContentHash;   // empty lastHash ⇒ changed
            var remoteChanged = remote.ContentHash != lastHash;

            if (localChanged && !remoteChanged) { await BackupCoreAsync(pass, snap, remote, progress); return DriveSyncOutcome.Uploaded; }
            if (!localChanged && remoteChanged) { await RestoreCoreAsync(pass, remote, progress); return DriveSyncOutcome.Downloaded; }

            RecordStatus("Sync paused: both phone and Drive changed");
            return DriveSyncOutcome.Conflict;   // the UI asks the user which copy to keep
        });

    // ── Cores ──────────────────────────────────────────────
    private async Task BackupCoreAsync(string pass, BackupSnapshot snap, DriveFileInfo? existing, IProgress<string>? p)
    {
        p?.Report("Encrypting backup...");
        var enc = await Task.Run(() => BackupCrypto.Encrypt(Encoding.UTF8.GetBytes(snap.Json), pass));
        p?.Report("Uploading to Google Drive...");
        var info = await _drive.UploadBackupAsync(enc, snap.ContentHash, DeviceInfo.Current.Name, existing);
        MarkSynced(snap.ContentHash, info.ModifiedUtc);
        RecordStatus("Backup successful");
    }

    private async Task RestoreCoreAsync(string pass, DriveFileInfo remote, IProgress<string>? p)
    {
        p?.Report("Downloading backup...");
        var bytes = await _drive.DownloadAsync(remote.Id);

        p?.Report("Decrypting...");
        var json = Encoding.UTF8.GetString(await Task.Run(() => BackupCrypto.Decrypt(bytes, pass)));

        p?.Report("Saving a safety copy of current data...");
        var current = await _data.CreateSnapshotAsync();
        var safety = await Task.Run(() => BackupCrypto.Encrypt(Encoding.UTF8.GetBytes(current.Json), pass));
        await File.WriteAllBytesAsync(Path.Combine(FileSystem.AppDataDirectory, "pre_restore_backup.msbk"), safety);

        p?.Report("Restoring data...");
        try { await _data.RestoreSnapshotAsync(json); }
        catch (InvalidDataException ex)
        { throw new DriveBackupException(DriveErrorKind.CorruptBackup, ex.Message, ex); }

        var after = await _data.CreateSnapshotAsync();
        MarkSynced(after.ContentHash, remote.ModifiedUtc);
        try { await _notifications.RescheduleAllPendingAsync(); } catch { /* non-fatal */ }
        RecordStatus("Restore successful");
    }

    // ── Helpers ────────────────────────────────────────────
    private async Task EnsureConnectedAsync()
    {
        if (!await _auth.IsSignedInAsync())
            throw new DriveBackupException(DriveErrorKind.NotSignedIn, "Please connect your Google account first.");
    }

    private async Task<string> ResolvePassphraseAsync(string? provided, bool enforceStrength)
    {
        var pass = !string.IsNullOrWhiteSpace(provided) ? provided : await SecureStorage.GetAsync(KeyPassphrase);
        if (string.IsNullOrEmpty(pass))
            throw new DriveBackupException(DriveErrorKind.PassphraseRequired, "Enter your backup passphrase.");
        if (enforceStrength && pass.Length < MinPassphraseLength)
            throw new DriveBackupException(DriveErrorKind.PassphraseRequired,
                $"Passphrase must be at least {MinPassphraseLength} characters.");
        return pass;
    }

    private static void MarkSynced(string hash, DateTime modifiedUtc)
    {
        Preferences.Default.Set(PrefSyncedHash, hash);
        Preferences.Default.Set(PrefLastBackup, modifiedUtc.ToString("O"));
    }

    private static void RecordStatus(string s) => Preferences.Default.Set(PrefLastStatus, s);

    private async Task<T> RunExclusive<T>(Func<Task<T>> work)
    {
        if (!await _gate.WaitAsync(0))
            throw new DriveBackupException(DriveErrorKind.Busy, "Another backup operation is already running.");
        try { return await work(); }
        catch (DriveBackupException ex)
        {
            if (ex.Kind != DriveErrorKind.Cancelled) RecordStatus($"Failed: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "DriveBackup");
            RecordStatus("Failed: unexpected error");
            throw new DriveBackupException(DriveErrorKind.Api, "Something went wrong. Please try again.", ex);
        }
        finally { _gate.Release(); }
    }
}
