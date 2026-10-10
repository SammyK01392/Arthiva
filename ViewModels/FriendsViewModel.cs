using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

/// <summary>Display row for one connection.</summary>
public class ConnectionRow
{
    public string OtherUid { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ConnectionStatus Status { get; init; }
    public string? LinkedContactName { get; init; }

    public bool IsIncoming => Status == ConnectionStatus.Incoming;
    public bool IsOutgoing => Status == ConnectionStatus.Outgoing;
    public bool IsActive => Status == ConnectionStatus.Active;
    public bool NeedsLink => IsActive && LinkedContactName is null;

    public string StatusText => Status switch
    {
        ConnectionStatus.Incoming => "Wants to connect with you",
        ConnectionStatus.Outgoing => "Request sent, waiting for them",
        _ => "Connected"
    };

    public string LinkText => LinkedContactName is null
        ? "Not linked to a contact"
        : $"Contact: {LinkedContactName}";
}

public partial class FriendsViewModel : BaseViewModel
{
    private readonly IFriendConnectionService _friends;
    private readonly IContactService _contacts;
    private readonly IFirebaseAuthService _auth;
    private readonly IInviteService _invites;

    private string _rawCode = string.Empty;

    public ObservableCollection<ConnectionRow> Connections { get; } = new();

    [ObservableProperty] private string myCode = "—";
    [ObservableProperty] private string inviteCode = string.Empty;
    [ObservableProperty] private bool isLoggedIn;

    public bool NotLoggedIn => !IsLoggedIn;

    partial void OnIsLoggedInChanged(bool value) => OnPropertyChanged(nameof(NotLoggedIn));

    public FriendsViewModel(
        IFriendConnectionService friends,
        IContactService contacts,
        IFirebaseAuthService auth,
        IInviteService invites)
    {
        _friends = friends;
        _contacts = contacts;
        _auth = auth;
        _invites = invites;
        Title = "Friends";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            IsLoggedIn = await _auth.IsLoggedInAsync();
            if (!IsLoggedIn)
            {
                Connections.Clear();
                MyCode = "—";
                return;
            }

            _rawCode = await _friends.GetOrCreateMyFriendCodeAsync();
            MyCode = FriendCode.Format(_rawCode);
            await LoadConnectionsAsync();
        });

        // A tapped invite link waiting for the user to open this app is handled here too.
        if (IsLoggedIn)
        {
            await _invites.ProcessPendingInviteAsync();
            await ExecuteAsync(LoadConnectionsAsync);
        }
    }

    private async Task LoadConnectionsAsync()
    {
        var list = await _friends.GetConnectionsAsync();
        var contacts = (await _contacts.GetAllAsync()).ToDictionary(c => c.Id, c => c.Name);

        Connections.Clear();
        foreach (var c in list)
        {
            string? linkedName = null;
            if (c.LinkedContactId is int id)
                contacts.TryGetValue(id, out linkedName);

            Connections.Add(new ConnectionRow
            {
                OtherUid = c.OtherUid,
                DisplayName = c.DisplayName,
                Status = c.Status,
                LinkedContactName = linkedName
            });
        }
    }

    // ─────────────────────────────────────────────
    //  Invite a contact (WhatsApp)
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task InviteContactAsync()
    {
        var all = await _contacts.GetAllAsync();
        var candidates = all.Where(c => string.IsNullOrEmpty(c.LinkedUid)).ToList();

        if (candidates.Count == 0)
        {
            await Shell.Current.DisplayAlert(
                "No one to invite",
                "All your contacts are already connected, or you have no contacts yet. Use \"Share invite link\" instead.",
                "OK");
            return;
        }

        var names = candidates
            .Select(c => string.IsNullOrWhiteSpace(c.Mobile) ? $"{c.Name} (no number)" : c.Name)
            .ToArray();

        var picked = await Shell.Current.DisplayActionSheet("Invite on WhatsApp", "Cancel", null, names);
        if (picked is null || picked == "Cancel") return;

        var index = Array.IndexOf(names, picked);
        if (index < 0) return;

        var contactId = candidates[index].Id;
        await ExecuteAsync(async () => await _invites.SendWhatsAppInviteAsync(contactId));
    }

    // ─────────────────────────────────────────────
    //  My code / link
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task CopyCodeAsync()
    {
        if (string.IsNullOrEmpty(_rawCode)) return;
        await Clipboard.Default.SetTextAsync(_rawCode);
        await Shell.Current.DisplayAlert("Copied", "Your friend code is copied.", "OK");
    }

    [RelayCommand]
    private async Task ShareCodeAsync()
    {
        if (string.IsNullOrEmpty(_rawCode)) return;

        string? link = null;
        await ExecuteAsync(async () => link = await _invites.GetShareLinkAsync());
        if (link is null) return;

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = $"Connect with me on MoneySpend 👇\n{link}\n(Friend code: {MyCode})",
            Title = "MoneySpend invite"
        });
    }

    [RelayCommand]
    private async Task NewCodeAsync()
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Create a new code?",
            "Your old code and old share links stop working. People you're already connected with are not affected.",
            "Create new", "Cancel");
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            _rawCode = await _friends.RotateFriendCodeAsync();
            MyCode = FriendCode.Format(_rawCode);
        });
    }

    // ─────────────────────────────────────────────
    //  Connect with someone else's code
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task ConnectAsync()
    {
        FriendInvite? found = null;
        await ExecuteAsync(async () => found = await _friends.LookupCodeAsync(InviteCode));

        if (ErrorMessage is not null) return; // a real error is shown in the page

        if (found is null)
        {
            await Shell.Current.DisplayAlert("Not found", "No user found with that code, or the invite has expired.", "OK");
            return;
        }

        var ok = await Shell.Current.DisplayAlert(
            "Connect?",
            $"Send a connection request to {found.DisplayName}?",
            "Send", "Cancel");
        if (!ok) return;

        var invite = found;
        await ExecuteAsync(async () =>
        {
            await _friends.SendConnectionRequestAsync(invite, invite.Code);
            InviteCode = string.Empty;
            await LoadConnectionsAsync();
        });
    }

    // ─────────────────────────────────────────────
    //  Connection actions
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task AcceptAsync(ConnectionRow? row)
    {
        if (row is null) return;

        await ExecuteAsync(async () =>
        {
            await _friends.AcceptConnectionAsync(row.OtherUid);
            await LoadConnectionsAsync();
        });

        if (ErrorMessage is null)
            await LinkFlowAsync(row);
    }

    [RelayCommand]
    private async Task RemoveAsync(ConnectionRow? row)
    {
        if (row is null) return;

        var text = row.IsActive
            ? $"Disconnect from {row.DisplayName}? You won't be able to send each other new requests. Existing records stay."
            : row.IsIncoming
                ? $"Decline the request from {row.DisplayName}?"
                : $"Cancel your request to {row.DisplayName}?";

        var confirm = await Shell.Current.DisplayAlert("Are you sure?", text, "Yes", "No");
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            await _friends.RemoveConnectionAsync(row.OtherUid);
            await LoadConnectionsAsync();
        });
    }

    [RelayCommand]
    private async Task LinkContactAsync(ConnectionRow? row)
    {
        if (row is null) return;
        await LinkFlowAsync(row);
    }

    /// <summary>Map this connection to a local contact (existing or new). Local only – nothing is uploaded.</summary>
    private async Task LinkFlowAsync(ConnectionRow row)
    {
        try
        {
            var contacts = (await _contacts.GetAllAsync()).ToList();

            const string createNew = "➕ Create a new contact";
            var names = contacts.Select(c => c.Name).Prepend(createNew).ToArray();

            var picked = await Shell.Current.DisplayActionSheet(
                $"Link {row.DisplayName} to a contact", "Skip", null, names);
            if (picked is null || picked == "Skip") return;

            if (picked == createNew)
            {
                await _friends.CreateLinkedContactAsync(row.OtherUid, row.DisplayName);
            }
            else
            {
                var index = Array.IndexOf(names, picked) - 1;
                if (index < 0 || index >= contacts.Count) return;
                await _friends.LinkContactAsync(contacts[index].Id, row.OtherUid);
            }

            await LoadConnectionsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private static async Task OpenRequestsAsync()
        => await Shell.Current.GoToAsync(
            nameof(SharedRequestListViewModel).Replace("ViewModel", "Page"));
}
