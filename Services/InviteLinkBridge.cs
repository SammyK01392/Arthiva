namespace MoneySpend.Services;

public static class InviteConstants
{
    /// <summary>
    /// Full https URL of invite.html, hosted next to your pay.html (same GitHub Pages site).
    /// This is the link people receive on WhatsApp.
    /// </summary>
    public const string PageUrl = "https://sammyk01392.github.io/Arthiva/invite.html";

    /// <summary>moneyspend://invite?code=XXXXXXXX (opened by invite.html on Android).</summary>
    public const string Scheme = "moneyspend";

    public const string PendingCodeKey = "pending_invite_code";
}

/// <summary>
/// Hand-off from the platform entry point (Android MainActivity, later iOS) to shared code.
/// The code is stored as "pending" and only acted on once the app is ready: the user is
/// logged in, not on the PIN screen, and the Shell exists. See IInviteService.ProcessPendingInviteAsync.
/// </summary>
public static class InviteLinkBridge
{
    public static event Action? InviteReceived;

    /// <summary>Call with the raw intent / URL text. Anything that isn't a valid invite is ignored.</summary>
    public static void Handle(string? uriText)
    {
        if (!TryParse(uriText, out var code)) return;

        Preferences.Default.Set(InviteConstants.PendingCodeKey, code);
        InviteReceived?.Invoke();
    }

    public static bool TryParse(string? uriText, out string code)
    {
        code = string.Empty;

        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, InviteConstants.Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, "invite", StringComparison.OrdinalIgnoreCase)) return false;

        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2 || kv[0] != "code") continue;

            var candidate = FriendCode.Normalize(Uri.UnescapeDataString(kv[1]));
            if (!FriendCode.IsValid(candidate)) return false;

            code = candidate;
            return true;
        }

        return false;
    }
}
