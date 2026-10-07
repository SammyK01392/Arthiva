namespace MoneySpend.Services;

/// <summary>
/// The only identity data that ever leaves the phone: a display name (max 60 chars,
/// enforced by the security rules). No email, mobile number, or UPI id is shared.
/// </summary>
internal static class SharedIdentity
{
    public const string Fallback = "MoneySpend user";

    public static string Clamp(string? name)
    {
        name = string.IsNullOrWhiteSpace(name) ? Fallback : name.Trim();
        return name.Length > 60 ? name[..60] : name;
    }

    public static async Task<string> DisplayNameAsync(IUserProfileService profile)
    {
        string? name = null;
        try { name = (await profile.GetProfileAsync())?.FullName; }
        catch { /* profile not ready → fallback */ }
        return Clamp(name);
    }
}
