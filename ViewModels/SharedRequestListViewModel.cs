using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class SharedRequestListViewModel : BaseViewModel
{
    private readonly ISharedRequestService _requests;
    private readonly IAccountService _accountService;
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<SharedRequestLink> Incoming { get; } = new();
    public ObservableCollection<SharedRequestLink> Outgoing { get; } = new();
    public ObservableCollection<SharedRequestLink> History { get; } = new();

    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool hasIncoming;
    [ObservableProperty] private bool hasOutgoing;
    [ObservableProperty] private bool hasHistory;
    [ObservableProperty] private bool isEmpty;

    public SharedRequestListViewModel(ISharedRequestService requests, IAccountService accountService)
    {
        _requests = requests;
        _accountService = accountService;
        Title = "Shared Requests";

        // Any DB change (reconcile applied something, status changed…) reloads the lists.
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    private static void Replace(ObservableCollection<SharedRequestLink> target, IEnumerable<SharedRequestLink> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    // No IsBusy guard (AutoRefresh calls this directly).
    private async Task ReloadAsync()
    {
        try
        {
            var all = await _requests.GetRequestsAsync();

            var incoming = all.Where(l => l.IsIncomingPending).ToList();
            var outgoing = all.Where(l => l.IsOutgoingPending).ToList();
            var history = all.Where(l => !l.IsIncomingPending && !l.IsOutgoingPending).ToList();

            Replace(Incoming, incoming);
            Replace(Outgoing, outgoing);
            Replace(History, history);

            HasIncoming = incoming.Count > 0;
            HasOutgoing = outgoing.Count > 0;
            HasHistory = history.Count > 0;
            IsEmpty = all.Count == 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await ExecuteAsync(async () =>
        {
            await ReloadAsync(); // cached data first, works offline
            var result = await _requests.ReconcileAsync();
            if (!result.Success) ErrorMessage = result.Message;
            await ReloadAsync();
        });
        _autoRefresh.Enabled = true;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var result = await _requests.ReconcileAsync();
            ErrorMessage = result.Success ? null : result.Message;
            await ReloadAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    // ─────────────────────────────────────────────
    //  Accept
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task AcceptAsync(SharedRequestLink? link)
    {
        if (link is null) return;

        int? accountId = null;

        // Split shares are cash-basis (no account entry until you pay back), so no account question.
        if (link.Type != SharedRequestType.Split)
        {
            var question = link.Type == SharedRequestType.Borrow
                ? "Paid from which account?"
                : "Received in which account?";

            var picked = await PickAccountAsync(question);
            if (!picked.Ok) return;
            accountId = picked.AccountId;
        }

        SharedRequestResult? result = null;
        await ExecuteAsync(async () => result = await _requests.AcceptAsync(link.SharedRequestId, accountId));
        await ShowResultAsync(result);
    }

    private async Task<(bool Ok, int? AccountId)> PickAccountAsync(string question)
    {
        var accounts = (await _accountService.GetAllAsync()).ToList();
        if (accounts.Count == 0) return (true, null);

        const string none = "No account (don't record in my accounts)";
        var names = accounts.Select(a => a.Name).Append(none).ToArray();

        var picked = await Shell.Current.DisplayActionSheet(question, "Cancel", null, names);
        if (picked is null || picked == "Cancel") return (false, null);
        if (picked == none) return (true, null);

        var index = Array.IndexOf(names, picked);
        return index >= 0 && index < accounts.Count
            ? (true, accounts[index].Id)
            : (false, null);
    }

    // ─────────────────────────────────────────────
    //  Reject / Cancel
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task RejectAsync(SharedRequestLink? link)
    {
        if (link is null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Reject request?",
            $"{link.OtherName} will see that you rejected this request.",
            "Reject", "Cancel");
        if (!confirm) return;

        SharedRequestResult? result = null;
        await ExecuteAsync(async () => result = await _requests.RejectAsync(link.SharedRequestId));
        await ShowResultAsync(result);
    }

    [RelayCommand]
    private async Task CancelRequestAsync(SharedRequestLink? link)
    {
        if (link is null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Cancel request?",
            $"The request to {link.OtherName} will be withdrawn.",
            "Withdraw", "Keep");
        if (!confirm) return;

        SharedRequestResult? result = null;
        await ExecuteAsync(async () => result = await _requests.CancelAsync(link.SharedRequestId));
        await ShowResultAsync(result);
    }

    private async Task ShowResultAsync(SharedRequestResult? result)
    {
        if (result is null) return; // ExecuteAsync already put the exception text in ErrorMessage

        if (!result.Success || !string.IsNullOrEmpty(result.Message))
            await Shell.Current.DisplayAlert("Shared request", result.Message ?? "Done.", "OK");

        await ReloadAsync();
    }
}
