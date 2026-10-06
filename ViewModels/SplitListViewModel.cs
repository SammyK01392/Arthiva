using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class SplitListViewModel : BaseViewModel
{
    private readonly ISplitService _splitService;
    private readonly ISplitGroupService _groupService;
    private readonly IAccountService _accountService;
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<FriendBalance> Friends { get; } = new();
    public ObservableCollection<GroupListItem> Groups { get; } = new();
    public ObservableCollection<SplitListItem> Splits { get; } = new();

    [ObservableProperty] private string selectedTab = "Friends"; // Friends / Groups / History
    [ObservableProperty] private decimal totalOwedToMe;
    [ObservableProperty] private decimal totalIOwe;
    [ObservableProperty] private string myUpiText = string.Empty;

    public bool IsFriendsTab => SelectedTab == "Friends";
    public bool IsGroupsTab => SelectedTab == "Groups";
    public bool IsHistoryTab => SelectedTab == "History";
    public bool ShowFriendsEmpty => IsFriendsTab && Friends.Count == 0;
    public bool ShowGroupsEmpty => IsGroupsTab && Groups.Count == 0;
    public bool ShowHistoryEmpty => IsHistoryTab && Splits.Count == 0;

    partial void OnSelectedTabChanged(string value) => NotifyTabState();

    public SplitListViewModel(
        ISplitService splitService,
        ISplitGroupService groupService,
        IAccountService accountService)
    {
        _splitService = splitService;
        _groupService = groupService;
        _accountService = accountService;
        Title = "Splits";
        RefreshMyUpiText();

        // Any DB change (split added/deleted, settle, group change) reloads the lists.
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    private void NotifyTabState()
    {
        OnPropertyChanged(nameof(IsFriendsTab));
        OnPropertyChanged(nameof(IsGroupsTab));
        OnPropertyChanged(nameof(IsHistoryTab));
        OnPropertyChanged(nameof(ShowFriendsEmpty));
        OnPropertyChanged(nameof(ShowGroupsEmpty));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
    }

    // No IsBusy guard here (AutoRefresh calls this directly).
    private async Task ReloadAsync()
    {
        try
        {
            var friends = await _splitService.GetFriendBalancesAsync();
            var groups = await _groupService.GetGroupsAsync();
            var splits = await _splitService.GetSplitsAsync();

            Friends.Clear();
            foreach (var f in friends) Friends.Add(f);

            Groups.Clear();
            foreach (var g in groups) Groups.Add(g);

            Splits.Clear();
            foreach (var s in splits) Splits.Add(s);

            TotalOwedToMe = friends.Where(f => f.Net > 0).Sum(f => f.Net);
            TotalIOwe = friends.Where(f => f.Net < 0).Sum(f => -f.Net);

            NotifyTabState();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(ReloadAsync);
        _autoRefresh.Enabled = true;
    }

    [RelayCommand]
    private void SetTab(string tab) => SelectedTab = tab;

    private void RefreshMyUpiText()
    {
        var upi = SplitPrompts.GetMyUpi();
        MyUpiText = string.IsNullOrEmpty(upi)
            ? "Add your UPI ID for payment reminders"
            : $"My UPI: {upi} (tap to change)";
    }

    [RelayCommand]
    private async Task EditMyUpiAsync()
    {
        await SplitPrompts.AskMyUpiAsync();
        RefreshMyUpiText();
    }

    [RelayCommand]
    private async Task ShareReceiptAsync(SplitListItem item)
    {
        if (item is null) return;

        var text = await _splitService.BuildReceiptAsync(item.Id);
        if (string.IsNullOrWhiteSpace(text)) return;

        await Share.Default.RequestAsync(new ShareTextRequest { Text = text, Title = item.Title });
    }

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(
            nameof(AddSplitViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task NewGroupAsync()
        => await Shell.Current.GoToAsync(
            nameof(GroupEditViewModel).Replace("ViewModel", "Page"));

    [RelayCommand]
    private static async Task OpenGroupAsync(GroupListItem group)
    {
        if (group is null) return;

        var route = nameof(GroupDetailViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?GroupId={group.Id}");
    }

    // ─────────────────────────────────────────────
    //  Delete a split
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task DeleteSplitAsync(SplitListItem item)
    {
        if (item is null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Delete split?",
            $"\"{item.Title}\" will be removed together with your entries for it and any payments already made or received. Account balances will be restored.",
            "Delete", "Cancel");

        if (!confirm) return;

        SplitResult? result = null;
        await ExecuteAsync(async () => result = await _splitService.DeleteAsync(item.Id));

        if (result is { Success: false })
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not delete.");
    }

    // ─────────────────────────────────────────────
    //  Remind (WhatsApp, fallback: share sheet)
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task RemindAsync(FriendBalance friend)
    {
        if (friend is null) return;

        // Ask for my own UPI id once; it is appended to reminders so friends can pay directly.
        var myUpi = SplitPrompts.GetMyUpi();
        if (string.IsNullOrEmpty(myUpi) && !Preferences.Default.Get(SplitPrompts.MyUpiAskedKey, false))
        {
            myUpi = await SplitPrompts.AskMyUpiAsync() ?? string.Empty;
            RefreshMyUpiText();
        }

        var text = await _splitService.BuildReminderTextAsync(
            friend.ContactId, string.IsNullOrEmpty(myUpi) ? null : myUpi);
        var phone = NormalizePhone(friend.Mobile);

        try
        {
            if (phone is not null)
            {
                var url = $"https://wa.me/{phone}?text={Uri.EscapeDataString(text)}";
                await Launcher.Default.OpenAsync(url);
                return;
            }
        }
        catch
        {
            // fall through to the share sheet
        }

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Text = text,
            Title = "Reminder"
        });
    }

    /// <summary>Digits only; 10-digit Indian numbers get the 91 prefix wa.me needs.</summary>
    private static string? NormalizePhone(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return null;

        var digits = new string(mobile.Where(char.IsDigit).ToArray());

        if (digits.Length == 10) return "91" + digits;
        if (digits.Length == 11 && digits.StartsWith('0')) return "91" + digits[1..];
        return digits.Length >= 11 ? digits : null;
    }

    // ─────────────────────────────────────────────
    //  Settle = friend paid ME
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task SettleAsync(FriendBalance friend)
    {
        if (friend is null || friend.LendPending <= 0) return;

        var amount = await SplitPrompts.AskAmountAsync(
            $"Settle with {friend.Name}",
            $"Pending: ₹{friend.LendPending:N2}. How much did they pay?",
            friend.LendPending);
        if (amount is null) return;

        var accountId = await SplitPrompts.PickAccountAsync(_accountService, "Received in which account?");
        if (accountId is null) return;

        SplitResult? result = null;
        await ExecuteAsync(async () =>
            result = await _splitService.SettleAsync(friend.ContactId, amount.Value, accountId.Value));

        if (result is { Success: false })
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not record the payment.");
    }

    // ─────────────────────────────────────────────
    //  Pay = I paid a friend (books my share as an expense for split bills)
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task PayAsync(FriendBalance friend)
    {
        if (friend is null || friend.BorrowPending <= 0) return;

        var amount = await SplitPrompts.AskAmountAsync(
            $"Pay {friend.Name}",
            $"You owe: ₹{friend.BorrowPending:N2}. How much are you paying?",
            friend.BorrowPending);
        if (amount is null) return;

        // UPI app or "already paid" — only continues if the payment really happened.
        if (!await SplitPrompts.ConfirmPaymentAsync(_splitService, friend.ContactId, friend.Name, amount.Value))
            return;

        var accountId = await SplitPrompts.PickAccountAsync(_accountService, "Paid from which account?");
        if (accountId is null) return;

        SplitResult? result = null;
        await ExecuteAsync(async () =>
            result = await _splitService.PayAsync(friend.ContactId, amount.Value, accountId.Value));

        if (result is { Success: false })
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not record the payment.");
    }
}
