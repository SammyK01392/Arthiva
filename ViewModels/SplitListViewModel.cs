using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class SplitListViewModel : BaseViewModel
{
    private readonly ISplitService _splitService;
    private readonly IAccountService _accountService;
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<FriendBalance> Friends { get; } = new();
    public ObservableCollection<SplitListItem> Splits { get; } = new();

    [ObservableProperty] private string selectedTab = "Friends"; // Friends / History
    [ObservableProperty] private decimal totalOwedToMe;
    [ObservableProperty] private decimal totalIOwe;

    public bool IsFriendsTab => SelectedTab == "Friends";
    public bool IsHistoryTab => SelectedTab == "History";
    public bool ShowFriendsEmpty => IsFriendsTab && Friends.Count == 0;
    public bool ShowHistoryEmpty => IsHistoryTab && Splits.Count == 0;

    partial void OnSelectedTabChanged(string value) => NotifyTabState();

    public SplitListViewModel(ISplitService splitService, IAccountService accountService)
    {
        _splitService = splitService;
        _accountService = accountService;
        Title = "Splits";

        // Any DB change (split added/deleted, settle, transaction edit) reloads the lists.
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    private void NotifyTabState()
    {
        OnPropertyChanged(nameof(IsFriendsTab));
        OnPropertyChanged(nameof(IsHistoryTab));
        OnPropertyChanged(nameof(ShowFriendsEmpty));
        OnPropertyChanged(nameof(ShowHistoryEmpty));
    }

    // No IsBusy guard here (AutoRefresh calls this directly).
    private async Task ReloadAsync()
    {
        try
        {
            var friends = await _splitService.GetFriendBalancesAsync();
            var splits = await _splitService.GetSplitsAsync();

            Friends.Clear();
            foreach (var f in friends) Friends.Add(f);

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

    [RelayCommand]
    private static async Task GoToAddAsync()
        => await Shell.Current.GoToAsync(
            nameof(AddSplitViewModel).Replace("ViewModel", "Page"));

    // ─────────────────────────────────────────────
    //  Delete a split
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task DeleteSplitAsync(SplitListItem item)
    {
        if (item is null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Delete split?",
            $"\"{item.Title}\" will be removed: your share, your friends' entries and any payments already received. Account balances will be restored.",
            "Delete", "Cancel");

        if (!confirm) return;

        SplitResult? result = null;
        await ExecuteAsync(async () => result = await _splitService.DeleteAsync(item.Id));

        if (result is { Success: false })
            await Shell.Current.DisplayAlert("Split", result.ErrorMessage ?? "Could not delete.", "OK");
    }

    // ─────────────────────────────────────────────
    //  Remind (WhatsApp, fallback: share sheet)
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task RemindAsync(FriendBalance friend)
    {
        if (friend is null) return;

        var text = await _splitService.BuildReminderTextAsync(friend.ContactId);
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
    //  Settle (money received from a friend)
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task SettleAsync(FriendBalance friend)
    {
        if (friend is null || friend.LendPending <= 0) return;

        var input = await Shell.Current.DisplayPromptAsync(
            $"Settle with {friend.Name}",
            $"Pending: ₹{friend.LendPending:N2}. How much did they pay?",
            accept: "Next",
            cancel: "Cancel",
            keyboard: Keyboard.Numeric,
            initialValue: friend.LendPending.ToString("0.##", CultureInfo.CurrentCulture));

        if (input is null) return;

        var amount = SplitCalculator.ParseAmount(input);
        if (amount <= 0)
        {
            await Shell.Current.DisplayAlert("Settle", "Enter a valid amount.", "OK");
            return;
        }

        // NOTE: assumes IAccountService.GetAllAsync() and Account.Name (same as the Add form).
        var accounts = (await _accountService.GetAllAsync()).ToList();
        if (accounts.Count == 0)
        {
            await Shell.Current.DisplayAlert("Settle", "Add an account first.", "OK");
            return;
        }

        var account = accounts[0];
        if (accounts.Count > 1)
        {
            var names = accounts.Select(a => a.Name).ToArray();
            var picked = await Shell.Current.DisplayActionSheet(
                "Received in which account?", "Cancel", null, names);

            var index = picked is null ? -1 : Array.IndexOf(names, picked);
            if (index < 0) return;

            account = accounts[index];
        }

        SplitResult? result = null;
        await ExecuteAsync(async () => result = await _splitService.SettleAsync(friend.ContactId, amount, account.Id));

        if (result is { Success: false })
            await Shell.Current.DisplayAlert("Settle", result.ErrorMessage ?? "Could not record the payment.", "OK");
    }
}
