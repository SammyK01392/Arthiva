using MoneySpend.Data;

namespace MoneySpend.Services;

public interface ISharedSyncCoordinator
{
    /// <summary>App start: ensure Firebase profile, friend code and this device are registered, then pull shared requests.</summary>
    Task StartAsync();

    /// <summary>App returned to foreground: throttled device heartbeat + pull shared requests.</summary>
    Task OnResumeAsync();

    /// <summary>Call BEFORE IFirebaseAuthService.SignOutAsync() so this device stops receiving this account's pushes.</summary>
    Task PrepareForSignOutAsync();
}

/// <summary>
/// Single entry point that keeps the shared-Firebase side consistent. Everything is
/// best-effort and idempotent: offline just means "try again at the next start/resume/sign-in".
/// </summary>
public class SharedSyncCoordinator : ISharedSyncCoordinator
{
    private readonly IFirebaseAuthService _auth;
    private readonly IFirebaseRtdbClient _rtdb;
    private readonly IDeviceRegistrationService _devices;
    private readonly IFriendConnectionService _friends;
    private readonly IUserProfileService _profile;
    private readonly ISharedRequestService _requests;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed class ProfileDto
    {
        public string? DisplayName { get; set; }
    }

    public SharedSyncCoordinator(
        IFirebaseAuthService auth,
        IFirebaseRtdbClient rtdb,
        IDeviceRegistrationService devices,
        IFriendConnectionService friends,
        IUserProfileService profile,
        ISharedRequestService requests)
    {
        _auth = auth;
        _rtdb = rtdb;
        _devices = devices;
        _friends = friends;
        _profile = profile;
        _requests = requests;

        // Constructed once at startup (see MauiProgram) so these subscriptions are always live.
        _auth.SignedIn += (sender, args) =>
        {
            _ = Task.Run(async () =>
            {
                await EnsureReadyAsync(forceDevice: true);
                await ReconcileSafeAsync();
            });
        };

        PushBridge.TokenRefreshed += token =>
        {
            _ = Task.Run(RegisterDeviceSafeAsync);
        };

        // Phase 4: a data push means "something changed" → pull.
        PushBridge.DataMessageReceived += data =>
        {
            _ = Task.Run(ReconcileSafeAsync);
        };
    }

    public async Task StartAsync()
    {
        await EnsureReadyAsync(forceDevice: false);
        await ReconcileSafeAsync();
    }

    public async Task OnResumeAsync()
    {
        if (!await _auth.IsLoggedInAsync()) return;
        await RegisterDeviceSafeAsync();
        await ReconcileSafeAsync();
    }

    public async Task PrepareForSignOutAsync()
    {
        try { await _devices.UnregisterAsync(); }
        catch (Exception ex) { CrashLogger.Log(ex, "SharedSync.PrepareForSignOut"); }
    }

    private async Task RegisterDeviceSafeAsync()
    {
        try { await _devices.RegisterAsync(force: true); }
        catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.Offline) { }
        catch (Exception ex) { CrashLogger.Log(ex, "SharedSync.RegisterDevice"); }
    }

    private async Task ReconcileSafeAsync()
    {
        try
        {
            var result = await _requests.ReconcileAsync();
            // result.Success == false is normal when offline; the next start/resume retries.
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "SharedSync.Reconcile");
        }
    }

    private async Task EnsureReadyAsync(bool forceDevice)
    {
        if (!await _auth.IsLoggedInAsync()) return;

        await _gate.WaitAsync();
        try
        {
            var uid = await _auth.GetUidAsync();
            if (string.IsNullOrEmpty(uid)) return;

            await EnsureProfileAsync(uid);
            await _friends.GetOrCreateMyFriendCodeAsync();
            await _devices.RegisterAsync(forceDevice);
        }
        catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.Offline)
        {
            // Offline: retried on next start/resume/sign-in.
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "SharedSync.EnsureReady");
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureProfileAsync(string uid)
    {
        var name = await SharedIdentity.DisplayNameAsync(_profile);
        var path = $"users/{uid}/profile";

        var existing = await _rtdb.GetAsync<ProfileDto>(path);
        if (existing is null)
            await _rtdb.PutAsync(path, new { displayName = name, createdAt = FirebaseRtdbClient.ServerTimestamp });
        else if (existing.DisplayName != name)
            await _rtdb.PatchAsync(path, new { displayName = name });
    }
}
