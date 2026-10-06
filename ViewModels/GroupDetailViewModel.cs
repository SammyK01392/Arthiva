using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

/// <summary>Route: GroupDetailPage?GroupId=5</summary>
[QueryProperty(nameof(GroupIdText), "GroupId")]
public partial class GroupDetailViewModel : BaseViewModel
{
    private readonly ISplitGroupService _groupService;
    private readonly ISplitService _splitService;
    private readonly IAccountService _accountService;
    private readonly AutoRefresh _autoRefresh;

    public ObservableCollection<MemberBalance> Balances { get; } = new();
    public ObservableCollection<GroupTransfer> Transfers { get; } = new();
    public ObservableCollection<SplitListItem> Expenses { get; } = new();

    [ObservableProperty] private string groupIdText = string.Empty;
    [ObservableProperty] private string myNetText = string.Empty;
    [ObservableProperty] private string myNetKind = "Settled"; // Owed / Owing / Settled

    public bool ShowNoTransfers => Transfers.Count == 0;
    public bool ShowNoExpenses => Expenses.Count == 0;

    private int GroupId => int.TryParse(GroupIdText, out var id) ? id : 0;

    public GroupDetailViewModel(
        ISplitGroupService groupService,
        ISplitService splitService,
        IAccountService accountService)
    {
        _groupService = groupService;
        _splitService = splitService;
        _accountService = accountService;
        Title = "Group";

        _autoRefresh = new AutoRefresh(ReloadAsync);
    }

    // No IsBusy guard here (AutoRefresh calls this directly).
    private async Task ReloadAsync()
    {
        if (GroupId <= 0) return;

        try
        {
            var detail = await _groupService.GetDetailAsync(GroupId);
            if (detail is null)
            {
                ErrorMessage = "Group not found.";
                return;
            }

            Title = detail.Name;

            Balances.Clear();
            foreach (var b in detail.Balances) Balances.Add(b);

            Transfers.Clear();
            foreach (var t in detail.Transfers) Transfers.Add(t);

            Expenses.Clear();
            foreach (var e in detail.Expenses) Expenses.Add(e);

            if (detail.MyNet > 0)
            {
                MyNetText = $"You are owed ₹{detail.MyNet:N2}";
                MyNetKind = "Owed";
            }
            else if (detail.MyNet < 0)
            {
                MyNetText = $"You owe ₹{-detail.MyNet:N2}";
                MyNetKind = "Owing";
            }
            else
            {
                MyNetText = "You're all settled up";
                MyNetKind = "Settled";
            }

            OnPropertyChanged(nameof(ShowNoTransfers));
            OnPropertyChanged(nameof(ShowNoExpenses));
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
    private async Task AddExpenseAsync()
    {
        var route = nameof(AddSplitViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?GroupId={GroupId}");
    }

    [RelayCommand]
    private async Task AddMembersAsync()
    {
        var route = nameof(GroupEditViewModel).Replace("ViewModel", "Page");
        await Shell.Current.GoToAsync($"{route}?GroupId={GroupId}");
    }

    [RelayCommand]
    private async Task ShareSummaryAsync()
    {
        var text = await _groupService.BuildSummaryAsync(GroupId);
        if (string.IsNullOrWhiteSpace(text)) return;

        await Share.Default.RequestAsync(new ShareTextRequest { Text = text, Title = Title });
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
    private async Task DeleteGroupAsync()
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Delete group?", $"\"{Title}\" will be deleted.", "Delete", "Cancel");
        if (!confirm) return;

        SplitResult? result = null;
        await ExecuteAsync(async () => result = await _groupService.DeleteGroupAsync(GroupId));

        if (result is null) return;

        if (result.Success)
            await Shell.Current.GoToAsync("..");
        else
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not delete the group.");
    }

    [RelayCommand]
    private async Task DeleteSplitAsync(SplitListItem item)
    {
        if (item is null) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Delete expense?",
            $"\"{item.Title}\" will be removed together with your entries for it and any payments already made or received. Account balances will be restored.",
            "Delete", "Cancel");
        if (!confirm) return;

        SplitResult? result = null;
        await ExecuteAsync(async () => result = await _splitService.DeleteAsync(item.Id));

        if (result is { Success: false })
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not delete.");
    }

    // ─────────────────────────────────────────────
    //  Act on a "fewest payments" line
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task TransferActionAsync(GroupTransfer transfer)
    {
        if (transfer is null || !transfer.CanAct) return;

        switch (transfer.Kind)
        {
            case "Others":
            {
                // Between two other members: balances only, no account involved.
                var amount = await SplitPrompts.AskAmountAsync(
                    transfer.Title,
                    "Amount paid? (only the group's balances change, not your accounts)",
                    transfer.ActionAmount);
                if (amount is null) return;

                SplitResult? result = null;
                await ExecuteAsync(async () =>
                    result = await _groupService.MarkPaidAsync(GroupId, transfer.FromId, transfer.ToId, amount.Value));

                if (result is { Success: false })
                    await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not record this.");
                break;
            }

            case "PayMe":
            {
                var amount = await SplitPrompts.AskAmountAsync(
                    $"{transfer.FromName} paid you",
                    $"They owe you ₹{transfer.DirectPending:N2} here. How much did you receive?",
                    transfer.ActionAmount);
                if (amount is null) return;

                var accountId = await SplitPrompts.PickAccountAsync(_accountService, "Received in which account?");
                if (accountId is null) return;

                SplitResult? result = null;
                await ExecuteAsync(async () =>
                    result = await _splitService.SettleAsync(transfer.FromId, amount.Value, accountId.Value, GroupId));

                if (result is { Success: false })
                    await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not record the payment.");
                break;
            }

            case "IPay":
            {
                var amount = await SplitPrompts.AskAmountAsync(
                    $"Pay {transfer.ToName}",
                    $"You owe them ₹{transfer.DirectPending:N2} here. How much are you paying?",
                    transfer.ActionAmount);
                if (amount is null) return;

                // UPI app or "already paid" — only continues if the payment really happened.
                if (!await SplitPrompts.ConfirmPaymentAsync(_splitService, transfer.ToId, transfer.ToName, amount.Value))
                    return;

                var accountId = await SplitPrompts.PickAccountAsync(_accountService, "Paid from which account?");
                if (accountId is null) return;

                SplitResult? result = null;
                await ExecuteAsync(async () =>
                    result = await _splitService.PayAsync(transfer.ToId, amount.Value, accountId.Value, GroupId));

                if (result is { Success: false })
                    await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not record the payment.");
                break;
            }
        }
    }
}
