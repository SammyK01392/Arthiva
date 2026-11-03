#if SMS_DETECT

using Android;
using Android.App;

// These two lines put the SMS permissions into the APK's manifest.
// They exist ONLY in the SMS build, so the normal build has no SMS permission
// and Play Protect will not block its installation.
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveSms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReadSms)]

namespace MoneySpend;

/// <summary>Runtime permission for receiving (and reading old) SMS.</summary>
public class SmsReadPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions => new[]
    {
        (Manifest.Permission.ReceiveSms, true),
        (Manifest.Permission.ReadSms, true)
    };
}

#endif
