namespace MoneySpend.Services;

/// <summary>
/// Platform-neutral push-token access. Android implements it with FCM now;
/// an iOS implementation (FCM-over-APNs) can be added later without touching
/// any shared code. Platform is the string written to Firebase
/// ("android" | "ios") and validated by the security rules.
/// </summary>
public interface IPushTokenProvider
{
    string Platform { get; }
    Task<string?> GetTokenAsync();

    /// <summary>Invalidate this install's token (used on sign-out so the next account gets a fresh one).</summary>
    Task DeleteTokenAsync();
}

/// <summary>
/// Static hand-off between platform entry points (Android FirebaseMessagingService,
/// later iOS AppDelegate) which the DI container doesn't construct, and the
/// shared singletons that react to them.
/// </summary>
public static class PushBridge
{
    public static event Action<string>? TokenRefreshed;
    public static event Action<IDictionary<string, string>>? DataMessageReceived;

    public static void RaiseTokenRefreshed(string token) => TokenRefreshed?.Invoke(token);
    public static void RaiseDataMessage(IDictionary<string, string> data) => DataMessageReceived?.Invoke(data);
}

/// <summary>Used on platforms with no push implementation yet.</summary>
public sealed class NullPushTokenProvider : IPushTokenProvider
{
    public string Platform => "android"; // never registered: GetTokenAsync is null
    public Task<string?> GetTokenAsync() => Task.FromResult<string?>(null);
    public Task DeleteTokenAsync() => Task.CompletedTask;
}
