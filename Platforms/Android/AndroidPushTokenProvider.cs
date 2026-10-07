using Android.Gms.Extensions;
using Firebase.Messaging;
using MoneySpend.Data;

namespace MoneySpend.Services;

/// <summary>Android implementation of IPushTokenProvider (FCM). Lives in Platforms/Android so it only compiles there.</summary>
public sealed class AndroidPushTokenProvider : IPushTokenProvider
{
    public string Platform => "android";

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            var result = await FirebaseMessaging.Instance.GetToken().AsAsync<Java.Lang.String>();
            var token = result?.ToString();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (Exception ex)
        {
            // No Play Services, firebase not initialised (missing google-services.json), offline…
            CrashLogger.Log(ex, "FCM.GetToken");
            return null;
        }
    }

    public async Task DeleteTokenAsync()
    {
        try
        {
            await FirebaseMessaging.Instance.DeleteToken().AsAsync<Java.Lang.Object>();
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "FCM.DeleteToken");
        }
    }
}
