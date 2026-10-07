using MoneySpend.Data;

namespace MoneySpend.Services;

public interface IDeviceRegistrationService
{
    string DeviceId { get; }

    /// <summary>Upsert this device (FCM token + lastSeen) under the logged-in uid. Throttled unless force.</summary>
    Task RegisterAsync(bool force = false);

    /// <summary>Remove this device from Firebase and invalidate its FCM token. Call BEFORE signing out.</summary>
    Task UnregisterAsync();
}

/// <summary>
/// Layout:  /users/{uid}/devices/{deviceId} = { fcmToken, platform, appVersion, lastSeen }
/// One node per install, so the same account on N phones has N tokens and the
/// Cloud Function (Phase 4) pushes to all of them.
/// </summary>
public class DeviceRegistrationService : IDeviceRegistrationService
{
    private const string KeyDeviceId = "shared_device_id";
    private const string KeyLastToken = "shared_last_fcm_token";
    private const string KeyLastUid = "shared_last_uid";
    private const string KeyLastAt = "shared_last_register_unix";
    private static readonly TimeSpan MinInterval = TimeSpan.FromHours(6);

    private readonly IFirebaseAuthService _auth;
    private readonly IFirebaseRtdbClient _rtdb;
    private readonly IPushTokenProvider _push;

    public DeviceRegistrationService(IFirebaseAuthService auth, IFirebaseRtdbClient rtdb, IPushTokenProvider push)
    {
        _auth = auth;
        _rtdb = rtdb;
        _push = push;
    }

    public string DeviceId
    {
        get
        {
            var id = Preferences.Default.Get(KeyDeviceId, string.Empty);
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N"); // 32 hex chars, matches the rules regex
                Preferences.Default.Set(KeyDeviceId, id);
            }
            return id;
        }
    }

    public async Task RegisterAsync(bool force = false)
    {
        var uid = await _auth.GetUidAsync();
        if (string.IsNullOrEmpty(uid)) return;

        var token = await _push.GetTokenAsync(); // may be null (no Play Services / permission) – still register presence

        var lastToken = Preferences.Default.Get(KeyLastToken, string.Empty);
        var lastUid = Preferences.Default.Get(KeyLastUid, string.Empty);
        var lastAt = DateTimeOffset.FromUnixTimeSeconds(Preferences.Default.Get(KeyLastAt, 0L));

        if (!force
            && uid == lastUid
            && (token ?? string.Empty) == lastToken
            && DateTimeOffset.UtcNow - lastAt < MinInterval)
            return;

        await _rtdb.PatchAsync($"users/{uid}/devices/{DeviceId}", new
        {
            fcmToken = token,   // null is omitted → an existing token is never blanked by a failed lookup
            platform = _push.Platform,
            appVersion = AppInfo.Current.VersionString,
            lastSeen = FirebaseRtdbClient.ServerTimestamp
        });

        Preferences.Default.Set(KeyLastToken, token ?? string.Empty);
        Preferences.Default.Set(KeyLastUid, uid);
        Preferences.Default.Set(KeyLastAt, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public async Task UnregisterAsync()
    {
        try
        {
            var uid = await _auth.GetUidAsync();
            if (!string.IsNullOrEmpty(uid))
                await _rtdb.DeleteAsync($"users/{uid}/devices/{DeviceId}");
        }
        catch (Exception ex)
        {
            // Offline sign-out: the token is still invalidated below, so FCM will
            // report it as unregistered and the Cloud Function prunes the stale node.
            CrashLogger.Log(ex, "DeviceRegistration.Unregister");
        }

        await _push.DeleteTokenAsync();

        Preferences.Default.Remove(KeyLastToken);
        Preferences.Default.Remove(KeyLastUid);
        Preferences.Default.Remove(KeyLastAt);
    }
}
