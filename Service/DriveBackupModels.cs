using System;
using System.Collections.Generic;
using System.Text;

namespace MoneySpend.Services;

public enum DriveErrorKind
{
    Offline, NotSignedIn, AuthExpired, Cancelled, PermissionDenied, PassphraseRequired,
    WrongPassphrase, NoBackup, CorruptBackup, QuotaExceeded, Busy, Api
}

public class DriveBackupException : Exception
{
    public DriveErrorKind Kind { get; }
    public DriveBackupException(DriveErrorKind kind, string message, Exception? inner = null)
        : base(message, inner) => Kind = kind;
}

public enum DriveSyncOutcome { UpToDate, Uploaded, Downloaded, Conflict }

public record GoogleAccount(string Email, string? Name);
public record DriveFileInfo(string Id, DateTime ModifiedUtc, long SizeBytes, string? ContentHash, string? DeviceName);
public record DriveBackupStatus(bool IsConnected, string? Email, DateTime? LastBackupLocal,
                                string LastStatus, bool HasSavedPassphrase);
public record BackupSnapshot(string Json, string ContentHash, int RowCount);

internal static class DriveGuard
{
    public static void EnsureOnline()
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            throw new DriveBackupException(DriveErrorKind.Offline,
                "No internet connection. Please connect and try again.");
    }
}