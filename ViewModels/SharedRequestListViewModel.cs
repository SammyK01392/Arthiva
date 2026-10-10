using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

/// <summary>An accepted request that still has money outstanding.</summary>
public class ActiveRequestRow
{
    public SharedRequestLink Link { get; init; } = null!;
    public decimal Pending { get; init; }

    public string Headline => Link.Headline;
    public string PendingText => $"₹{Pending:N2} still pending of ₹{Link.Amount:N2}";
}

/// <summary>One payment (settlement) with its request's name for display.</summary>
public class SettlementRow
{
    public SharedSettlementLink Link { get; init; } = null!;

    public string Headline => Link.Headline;
    public string StatusText => Link.StatusText;
    public string DateText => Link.DateText;
}

public partial class SharedRequestListViewModel : BaseViewModel
{
    private readonly ISharedRequestService _requests;
    private readonly ISharedSettlementService _settlements;
    private readonly IBorrowLendService _borrowLend;
    private readonly IAccountService _accountService;
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<SharedRequestLink> Incoming { get; } = new();
    public ObservableCollection<SharedRequestLink> Outgoing { get; } = new();
    public ObservableCollection<SettlementRow> PaymentsToConfirm { get; } = new();
    public ObservableCollection<ActiveRequestRow> Active { get; } = new();
    public ObservableCollection<SettlementRow> PaymentsWaiting { get; } = new();
    public ObservableCollection<SharedRequestLink> History { get; } = new();
    public ObservableCollection<SettlementRow> PaymentHistory { get; } = new();

    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool hasIncoming;
    [ObservableProperty] private bool hasOutgoing;
    [ObservableProperty] private bool hasPaymentsToConfirm;
    [ObservableProperty] private bool hasActive;
    [ObservableProperty] private bool hasPaymentsWaiting;
    [ObservableProperty] private bool hasHistory;
    [ObservableProperty] private bool hasPaymentHistory;
    [ObservableProperty] private bool isEmpty;

    public SharedRequestListViewModel(
        ISharedRequestService requests,
        ISharedSettlementService settlements,
        IBorrowLendService borrowLend,
        IAccountService accountService)
    {
        _requests = requests;
        _settlements = settlements;
        _borrowLend = borrowLend;
        _accountService = accountService;
        Title = "Shared Requests";

        // Any DB change (reconcile applied something, status changed…) reloads the lists.
        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
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
            var payments = await _settlements.GetAllAsync();

            var incoming = all.Where(l => l.IsIncomingPending).ToList();
            var outgoing = all.Where(l => l.IsOutgoingPending).ToList();

            // Accepted + still money outstanding → "Active"
            var active = new List<ActiveRequestRow>();
            var activeIds = new HashSet<string>();
            var acceptedApplied = all
                .Where(r => r.Status == SharedRequestStatus.Accepted && r.BorrowLendId is not null)
                .ToList();

            foreach (var request in acceptedApplied)
            {
                var bl = await _borrowLend.GetByIdAsync(request.BorrowLendId!.Value);
                var pending = bl?.PendingAmount ?? 0m;
                if (pending <= 0) continue;

                active.Add(new ActiveRequestRow { Link = request, Pending = pending });
                activeIds.Add(request.SharedRequestId);
            }

            var history = all
                .Where(l => !l.IsIncomingPending && !l.IsOutgoingPending && !activeIds.Contains(l.SharedRequestId))
                .ToList();

            var toConfirm = payments.Where(p => p.IsIncomingPending).Select(p => new SettlementRow { Link = p }).ToList();
            var waiting = payments.Where(p => !p.IsIncomingPending && p.Status == SettlementStatus.Pending)
                                  .Select(p => new SettlementRow { Link = p }).ToList();
            var paymentHistory = payments.Where(p => p.Status != SettlementStatus.Pending)
                                         .Select(p => new SettlementRow { Link = p }).ToList();

            Replace(Incoming, incoming);
            Replace(Outgoing, outgoing);
            Replace(PaymentsToConfirm, toConfirm);
            Replace(Active, active);
            Replace(PaymentsWaiting, waiting);
            Replace(History, history);
            Replace(PaymentHistory, paymentHistory);

            HasIncoming = incoming.Count > 0;
            HasOutgoing = outgoing.Count > 0;
            HasPaymentsToConfirm = toConfirm.Count > 0;
            HasActive = active.Count > 0;
            HasPaymentsWaiting = waiting.Count > 0;
            HasHistory = history.Count > 0;
            HasPaymentHistory = paymentHistory.Count > 0;
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
    //  Account picker (private choice, never uploaded)
    // ─────────────────────────────────────────────
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

    private async Task ShowResultAsync(SharedRequestResult? result)
    {
        if (result is null) return; // ExecuteAsync already put the exception text in ErrorMessage

        if (!result.Success || !string.IsNullOrEmpty(result.Message))
            await Shell.Current.DisplayAlert("Shared request", result.Message ?? "Done.", "OK");

        await ReloadAsync();
    }

    // ─────────────────────────────────────────────
    //  Requests: Accept / Reject / Withdraw
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

    // ─────────────────────────────────────────────
    //  Payments (settlements)
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task RecordPaymentAsync(ActiveRequestRow? row)
    {
        if (row is null) return;

        var link = row.Link;
        var iPaid = link.IAmPayer;

        var amount = await SplitPrompts.AskAmountAsync(
            iPaid ? $"Pay {link.OtherName}" : $"Received from {link.OtherName}",
            iPaid
                ? $"Pending: ₹{row.Pending:N2}. How much did you pay? {link.OtherName} has to confirm it."
                : $"Pending: ₹{row.Pending:N2}. How much did you receive? {link.OtherName} has to confirm it.",
            row.Pending);
        if (amount is null) return;

        if (amount.Value > row.Pending)
        {
            await SplitPrompts.AlertAsync($"Amount can't exceed the pending ₹{row.Pending:N2}.");
            return;
        }

        var picked = await PickAccountAsync(iPaid ? "Paid from which account?" : "Received in which account?");
        if (!picked.Ok) return;

        SharedRequestResult? result = null;
        await ExecuteAsync(async () =>
            result = await _settlements.ProposeAsync(link.SharedRequestId, amount.Value, DateTime.Now, picked.AccountId));
        await ShowResultAsync(result);
    }

    [RelayCommand]
    private async Task ConfirmPaymentAsync(SettlementRow? row)
    {
        if (row is null) return;

        var link = row.Link;
        var picked = await PickAccountAsync(link.IAmPayer ? "Paid from which account?" : "Received in which account?");
        if (!picked.Ok) return;

        SharedRequestResult? result = null;
        await ExecuteAsync(async () =>
            result = await _settlements.ConfirmAsync(link.SharedSettlementId, picked.AccountId));
        await ShowResultAsync(result);
    }

    [RelayCommand]
    private async Task RejectPaymentAsync(SettlementRow? row)
    {
        if (row is null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Reject this payment?",
            $"{row.Link.OtherName} will see that you did not confirm it.",
            "Reject", "Cancel");
        if (!confirm) return;

        SharedRequestResult? result = null;
        await ExecuteAsync(async () => result = await _settlements.RejectAsync(row.Link.SharedSettlementId));
        await ShowResultAsync(result);
    }

    [RelayCommand]
    private async Task CancelPaymentAsync(SettlementRow? row)
    {
        if (row is null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Withdraw this payment?",
            "It will no longer wait for confirmation.",
            "Withdraw", "Keep");
        if (!confirm) return;

        SharedRequestResult? result = null;
        await ExecuteAsync(async () => result = await _settlements.CancelAsync(row.Link.SharedSettlementId));
        await ShowResultAsync(result);
    }
}
