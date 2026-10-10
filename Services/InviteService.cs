using MoneySpend.Data;
using MoneySpend.Models;

namespace MoneySpend.Services;

public interface IInviteService
{
    /// <summary>https://…/invite.html#c=CODE&amp;n=Name</summary>
    string BuildLink(string code, string? inviterName);

    /// <summary>A fresh one-time (7 days) invite for this contact. Reused while still valid.</summary>
    Task<string> CreateInviteCodeAsync(int? contactId);

    /// <summary>Link for sharing anywhere (uses your permanent friend code; the other side still needs your approval).</summary>
    Task<string> GetShareLinkAsync();

    /// <summary>Creates a one-time invite and opens WhatsApp to this contact's number (falls back to the share sheet).</summary>
    Task SendWhatsAppInviteAsync(int contactId);

    /// <summary>Inviter side: auto-accept connections that came through one of MY one-time invites; notify about the rest.</summary>
    Task ProcessIncomingAsync();

    /// <summary>Invitee side: act on a tapped invite link once the app is ready. Safe to call any time.</summary>
    Task ProcessPendingInviteAsync();
}

public class InviteService : IInviteService
{
    private sealed class InviteDto
    {
        public string? Uid { get; set; }
        public string? DisplayName { get; set; }
        public bool? Once { get; set; }
        public long? Exp { get; set; }
    }

    private sealed class ConnDto
    {
        public string? Status { get; set; }
        public string? DisplayName { get; set; }
        public long CreatedAt { get; set; }
        public string? Via { get; set; }
    }

    private readonly IFirebaseRtdbClient _rtdb;
    private readonly IFirebaseAuthService _auth;
    private readonly IFriendConnectionService _friends;
    private readonly IContactService _contacts;
    private readonly IUserProfileService _profile;
    private readonly INotificationService _notifications;

    private readonly SemaphoreSlim _incomingGate = new(1, 1);
    private int _prompting;
    private int _loginPromptShown;

    public InviteService(
        IFirebaseRtdbClient rtdb,
        IFirebaseAuthService auth,
        IFriendConnectionService friends,
        IContactService contacts,
        IUserProfileService profile,
        INotificationService notifications)
    {
        _rtdb = rtdb;
        _auth = auth;
        _friends = friends;
        _contacts = contacts;
        _profile = profile;
        _notifications = notifications;
    }

    private static bool IsTransient(FirebaseRtdbException ex)
        => ex.Kind is FirebaseErrorKind.Offline or FirebaseErrorKind.Server;

    private async Task<string> RequireUidAsync()
        => await _auth.GetUidAsync() is { Length: > 0 } uid
            ? uid
            : throw new InvalidOperationException("Please login first.");

    // ─────────────────────────────────────────────
    //  Link building
    // ─────────────────────────────────────────────
    public string BuildLink(string code, string? inviterName)
    {
        var link = $"{InviteConstants.PageUrl}#c={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrWhiteSpace(inviterName))
            link += $"&n={Uri.EscapeDataString(SharedIdentity.Clamp(inviterName))}";
        return link;
    }

    public async Task<string> GetShareLinkAsync()
    {
        var code = await _friends.GetOrCreateMyFriendCodeAsync();
        var name = await SharedIdentity.DisplayNameAsync(_profile);
        return BuildLink(code, name);
    }

    // ─────────────────────────────────────────────
    //  Create + send (inviter)
    // ─────────────────────────────────────────────
    public async Task<string> CreateInviteCodeAsync(int? contactId)
    {
        var uid = await RequireUidAsync();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Reuse a still-valid invite for the same contact (no code spam when "Invite" is tapped twice).
        if (contactId is int reuseFor)
        {
            var existing = Preferences.Default.Get($"invite_for_{reuseFor}", string.Empty);
            if (!string.IsNullOrEmpty(existing))
            {
                var dto = await _rtdb.GetAsync<InviteDto>($"friendCodes/{existing}");
                if (dto?.Uid == uid && dto.Once == true && (dto.Exp ?? 0) > nowMs)
                    return existing;
            }
        }

        var name = await SharedIdentity.DisplayNameAsync(_profile);
        var exp = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();

        for (var i = 0; i < 8; i++)
        {
            var code = FriendCode.Generate();
            var created = await _rtdb.PutIfAbsentAsync($"friendCodes/{code}", new
            {
                uid,
                displayName = name,
                once = true,
                exp
            });

            if (!created) continue;

            // Private, device-local: which of MY contacts this invite was meant for.
            if (contactId is int cid)
            {
                Preferences.Default.Set($"invite_{code}", cid);
                Preferences.Default.Set($"invite_for_{cid}", code);
            }

            return code;
        }

        throw new InvalidOperationException("Could not create an invite. Please try again.");
    }

    public async Task SendWhatsAppInviteAsync(int contactId)
    {
        var contact = await _contacts.GetByIdAsync(contactId)
            ?? throw new InvalidOperationException("Contact not found.");

        var code = await CreateInviteCodeAsync(contactId);
        var myName = await SharedIdentity.DisplayNameAsync(_profile);
        var link = BuildLink(code, myName);

        var text =
            $"Hi {contact.Name}! Main MoneySpend app mein udhaar aur split ka hisaab rakhta hoon. " +
            "Is link se mujhse connect karo, phir paise ki requests ek tap mein aa jayengi 👇\n" +
            link;

        var phone = PhoneNumbers.ForWhatsApp(contact.Mobile);

        try
        {
            if (phone is not null &&
                await Launcher.Default.OpenAsync($"https://wa.me/{phone}?text={Uri.EscapeDataString(text)}"))
                return;
        }
        catch
        {
            // WhatsApp not installed → share sheet below
        }

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = text,
            Title = "Invite to MoneySpend"
        });
    }

    // ─────────────────────────────────────────────
    //  Inviter: auto-accept my one-time invites
    // ─────────────────────────────────────────────
    public async Task ProcessIncomingAsync()
    {
        var uid = await _auth.GetUidAsync();
        if (string.IsNullOrEmpty(uid)) return;

        if (!await _incomingGate.WaitAsync(0)) return;
        try
        {
            var map = await _rtdb.GetAsync<Dictionary<string, ConnDto>>($"userConnections/{uid}");
            if (map is null) return;

            foreach (var (otherUid, dto) in map)
            {
                if (dto.Status != "incoming") continue;

                if (await TryAutoAcceptAsync(uid, otherUid, dto)) continue;

                var key = $"conn_notified_{otherUid}";
                if (Preferences.Default.Get(key, false)) continue;

                Preferences.Default.Set(key, true);
                await NotifyAsync("Connection request",
                    $"{SharedIdentity.Clamp(dto.DisplayName)} wants to connect on MoneySpend.");
            }
        }
        catch (FirebaseRtdbException ex) when (IsTransient(ex))
        {
            // retried at the next trigger
        }
        finally { _incomingGate.Release(); }
    }

    private async Task<bool> TryAutoAcceptAsync(string uid, string otherUid, ConnDto dto)
    {
        // Only requests that came through one of MY one-time invites are pre-approved.
        if (string.IsNullOrEmpty(dto.Via)) return false;

        var invite = await _rtdb.GetAsync<InviteDto>($"friendCodes/{dto.Via}");
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (invite is null || invite.Uid != uid || invite.Once != true || (invite.Exp ?? 0) < nowMs)
            return false;

        try
        {
            await _friends.AcceptConnectionAsync(otherUid);
        }
        catch (InvalidOperationException)
        {
            return false; // they cancelled meanwhile
        }

        // Single use: the code can't bring in anybody else.
        try { await _rtdb.DeleteAsync($"friendCodes/{dto.Via}"); }
        catch (FirebaseRtdbException) { }

        var invitedContactId = Preferences.Default.Get($"invite_{dto.Via}", -1);
        await LinkInvitedContactAsync(otherUid, dto.DisplayName, invitedContactId);

        Preferences.Default.Remove($"invite_{dto.Via}");
        if (invitedContactId > 0) Preferences.Default.Remove($"invite_for_{invitedContactId}");

        await NotifyAsync("Connected",
            $"{SharedIdentity.Clamp(dto.DisplayName)} joined through your invite. You can now send each other requests.");
        return true;
    }

    private async Task LinkInvitedContactAsync(string otherUid, string? displayName, int invitedContactId)
    {
        try
        {
            if (invitedContactId > 0)
            {
                var existing = await _contacts.GetByIdAsync(invitedContactId);
                if (existing is not null && !existing.IsDeleted)
                {
                    await _friends.LinkContactAsync(invitedContactId, otherUid);
                    return;
                }
            }

            var all = await _contacts.GetAllAsync();
            if (!all.Any(c => c.LinkedUid == otherUid))
                await _friends.CreateLinkedContactAsync(otherUid, SharedIdentity.Clamp(displayName));
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Invite.LinkContact");
        }
    }

    // ─────────────────────────────────────────────
    //  Invitee: a tapped link
    // ─────────────────────────────────────────────
    public async Task ProcessPendingInviteAsync()
    {
        var code = Preferences.Default.Get(InviteConstants.PendingCodeKey, string.Empty);
        if (string.IsNullOrEmpty(code)) return;

        // Not ready yet → keep it pending; called again on start / resume / login / Friends page.
        if (Shell.Current?.CurrentPage is null || IsLockScreen()) return;

        if (!await _auth.IsLoggedInAsync())
        {
            if (Interlocked.Exchange(ref _loginPromptShown, 1) == 0)
            {
                await AlertAsync("Log in to connect",
                    "Log in to your MoneySpend cloud account first (Backup & Restore → Login). " +
                    "We'll finish connecting right after you log in.");
                await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync("LoginPage"));
            }
            return;
        }

        if (Interlocked.Exchange(ref _prompting, 1) == 1) return;
        try
        {
            FriendInvite? invite;
            try
            {
                invite = await _friends.LookupCodeAsync(code);
            }
            catch (FirebaseRtdbException ex) when (IsTransient(ex))
            {
                return; // offline: keep pending
            }

            // From here the invite counts as handled, whatever the outcome.
            Preferences.Default.Remove(InviteConstants.PendingCodeKey);
            Interlocked.Exchange(ref _loginPromptShown, 0);

            if (invite is null)
            {
                await AlertAsync("Invite expired",
                    "This invite link is invalid or has expired. Ask your friend to send a new one.");
                return;
            }

            var me = await _auth.GetUidAsync();
            if (invite.Uid == me)
            {
                await AlertAsync("That's you", "This is your own invite link.");
                return;
            }

            var connect = await MainThread.InvokeOnMainThreadAsync(() =>
                Shell.Current.DisplayAlert(
                    "Connect on MoneySpend?",
                    $"{invite.DisplayName} invited you. Connect to send each other Lend / Borrow / Split requests?",
                    "Connect", "Not now"));
            if (!connect) return;

            try
            {
                await _friends.SendConnectionRequestAsync(invite, invite.Code);
                await AlertAsync("Request sent",
                    $"Connection request sent to {invite.DisplayName}. You'll be connected as soon as their app picks it up.");
            }
            catch (InvalidOperationException ex)
            {
                await AlertAsync("Couldn't connect", ex.Message);
            }
            catch (FirebaseRtdbException)
            {
                await AlertAsync("Couldn't connect", "Please check your internet connection and open the invite link again.");
            }
        }
        finally { Interlocked.Exchange(ref _prompting, 0); }
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    private static bool IsLockScreen()
        => Shell.Current?.CurrentPage?.GetType().Name is "AppLockPage" or "CreatePinPage" or "FirstTimeSetupPage";

    private static Task AlertAsync(string title, string message)
        => MainThread.InvokeOnMainThreadAsync(() =>
            Shell.Current?.DisplayAlert(title, message, "OK") ?? Task.CompletedTask);

    private async Task NotifyAsync(string title, string body)
    {
        try
        {
            await _notifications.CreateAsync(new Notification
            {
                Title = title,
                Body = body,
                Type = "Connection",
                ReminderDate = DateTime.Now,
                IsScheduled = false,
                ActionRoute = "FriendsPage"
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Invite.Notify");
        }
    }
}

public static class PhoneNumbers
{
    /// <summary>Digits only, with the 91 prefix wa.me needs for Indian numbers. null = unusable.</summary>
    public static string? ForWhatsApp(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return null;

        var digits = new string(mobile.Where(char.IsDigit).ToArray());

        if (digits.Length == 10) return "91" + digits;
        if (digits.Length == 11 && digits.StartsWith('0')) return "91" + digits[1..];
        return digits.Length >= 11 ? digits : null;
    }
}
