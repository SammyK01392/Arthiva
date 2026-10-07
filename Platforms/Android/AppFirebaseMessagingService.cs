using Android.App;
using Firebase.Messaging;
using MoneySpend.Services;

namespace MoneySpend;

/// <summary>
/// Android entry point for FCM. The [Service]/[IntentFilter] attributes generate the
/// manifest entry automatically. Phase 1 only forwards token changes; Phase 4 uses the
/// data-message path to trigger a reconcile when a request arrives while the app is closed.
/// </summary>
[Service(Exported = false)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class AppFirebaseMessagingService : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        PushBridge.RaiseTokenRefreshed(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        var data = new Dictionary<string, string>();
        if (message.Data is not null)
            foreach (var kv in message.Data)
                data[kv.Key] = kv.Value;

        PushBridge.RaiseDataMessage(data);
    }
}
