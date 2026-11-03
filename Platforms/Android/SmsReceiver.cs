#if SMS_DETECT

using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using MoneySpend.Services;

namespace MoneySpend;

/// <summary>
/// Wakes up whenever an SMS arrives (even if the app is closed),
/// hands it to the detector and goes back to sleep.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_SMS")]
[IntentFilter(new[] { "android.provider.Telephony.SMS_RECEIVED" }, Priority = 999)]
public class SmsReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Android.Provider.Telephony.Sms.Intents.SmsReceivedAction)
            return;

        var messages = Android.Provider.Telephony.Sms.Intents.GetMessagesFromIntent(intent);
        if (messages is null || messages.Length == 0)
            return;

        var sender = messages[0].OriginatingAddress ?? string.Empty;
        var body = string.Concat(messages.Select(m => m.MessageBody));
        var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(messages[0].TimestampMillis).LocalDateTime;

        // keep the process alive until we finish the (async) database work
        var pending = GoAsync();

        Task.Run(async () =>
        {
            try
            {
                var service = IPlatformApplication.Current?.Services.GetService<IDetectedTransactionService>();

                if (service is not null)
                    await service.IngestSmsAsync(sender, body, receivedAt);
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("SmsReceiver", ex.ToString());
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}

#endif
