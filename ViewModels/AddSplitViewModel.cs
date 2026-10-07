using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Models;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

/// <summary>One person in the split (the first row is always "You").</summary>
public partial class ParticipantRow : ObservableObject
{
    /// <summary>null = you.</summary>
    public int? ContactId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsMe => ContactId is null;

    [ObservableProperty] private bool isSelected;

    /// <summary>Typed value: amount (Exact) or percent (Percent). Unused for Equal.</summary>
    [ObservableProperty] private string input = string.Empty;

    /// <summary>Computed ₹ share for this person.</summary>
    [ObservableProperty] private decimal share;

    [ObservableProperty] private bool showInput;

    [ObservableProperty] private string inputHint = "₹";
}

/// <summary>Optional query: AddSplitPage?GroupId=5 pre-selects that group.</summary>
[QueryProperty(nameof(GroupIdText), "GroupId")]
public partial class AddSplitViewModel : BaseViewModel
{
    private readonly ISplitService _splitService;
    private readonly ISplitGroupService _groupService;
    private readonly IAccountService _accountService;
    private readonly ICategoryService _categoryService;
    private readonly ISharedRequestService _sharedRequests;
    private readonly List<ParticipantRow> _allRows = new();
    private readonly List<(int Id, string Name)> _allContacts = new();
    private bool _loaded;
    private bool _suspend;

    public ObservableCollection<Account> Accounts { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<SplitGroup> Groups { get; } = new();
    public ObservableCollection<ParticipantRow> VisibleRows { get; } = new();

    /// <summary>Same people (and order) as _allRows — feeds the "Paid by" picker.</summary>
    public ObservableCollection<ParticipantRow> PayerOptions { get; } = new();

    [ObservableProperty] private string groupIdText = string.Empty;

    [ObservableProperty] private string expenseTitle = string.Empty;
    [ObservableProperty] private string totalText = string.Empty;
    [ObservableProperty] private DateTime splitDate = DateTime.Today;
    [ObservableProperty] private string? notes;

    [ObservableProperty] private Account? selectedAccount;
    [ObservableProperty] private Category? selectedCategory;
    [ObservableProperty] private SplitGroup? selectedGroup;
    [ObservableProperty] private ParticipantRow? selectedPayer;

    [ObservableProperty] private string selectedMethod = "Equal"; // Equal / Exact / Percent
    [ObservableProperty] private string contactSearch = string.Empty;

    // Quick "add friend" form (inline — no page navigation, so nothing typed is lost)
    [ObservableProperty] private bool isAddFriendOpen;
    [ObservableProperty] private string newFriendName = string.Empty;
    [ObservableProperty] private string newFriendMobile = string.Empty;

    [ObservableProperty] private string summaryText = "Enter the amount and pick who is in";
    [ObservableProperty] private bool isSplitValid;

    /// <summary>The "Paid from" account only matters when YOU paid (cash-basis: a friend's payment doesn't touch your account yet).</summary>
    public bool IsPaidByMe => SelectedPayer is null || SelectedPayer.IsMe;

    partial void OnSelectedPayerChanged(ParticipantRow? value) => OnPropertyChanged(nameof(IsPaidByMe));

    partial void OnTotalTextChanged(string value) => Recalculate();

    partial void OnSelectedMethodChanged(string value)
    {
        PrefillInputs();
        Recalculate();
    }

    partial void OnContactSearchChanged(string value) => ApplySearch();

    partial void OnSelectedGroupChanged(SplitGroup? value)
    {
        if (!_loaded) return;
        _ = SafeRebuildAsync();
    }

    public AddSplitViewModel(
        ISplitService splitService,
        ISplitGroupService groupService,
        IAccountService accountService,
        ICategoryService categoryService)
    {
        _splitService = splitService;
        _groupService = groupService;
        _accountService = accountService;
        _categoryService = categoryService;
        Title = "Split Expense";
    }

    // ─────────────────────────────────────────────
    //  Load
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loaded) return;

        await ExecuteAsync(async () =>
        {
            // NOTE: assumes IAccountService / ICategoryService expose GetAllAsync().
            var accounts = await _accountService.GetAllAsync();
            var categories = await _categoryService.GetAllAsync();
            var contacts = await _splitService.GetContactsAsync();
            var groups = await _groupService.GetGroupEntitiesAsync();

            Accounts.Clear();
            foreach (var a in accounts) Accounts.Add(a);
            SelectedAccount ??= Accounts.FirstOrDefault();

            Categories.Clear();
            foreach (var c in categories) Categories.Add(c);
            SelectedCategory ??= Categories.FirstOrDefault();

            _allContacts.Clear();
            foreach (var c in contacts) _allContacts.Add((c.Id, c.Name));

            Groups.Clear();
            Groups.Add(new SplitGroup { Id = 0, Name = "No group" });
            foreach (var g in groups) Groups.Add(g);

            int.TryParse(GroupIdText, out var groupId);
            SelectedGroup = Groups.FirstOrDefault(g => g.Id == groupId) ?? Groups[0];

            await RebuildRowsAsync();
            _loaded = true;
        });
    }

    private async Task SafeRebuildAsync()
    {
        try { await RebuildRowsAsync(); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private int? CurrentGroupId => SelectedGroup is { Id: > 0 } g ? g.Id : null;

    /// <summary>
    /// No group → everyone in Contacts, only you ticked.
    /// Group → just its members, all ticked (typical "split with everyone").
    /// </summary>
    private async Task RebuildRowsAsync()
    {
        var groupId = CurrentGroupId;
        var people = new List<(int Id, string Name)>();

        if (groupId is int gid)
        {
            foreach (var c in await _groupService.GetMembersAsync(gid))
                people.Add((c.Id, c.Name));
        }
        else
        {
            people.AddRange(_allContacts);
        }

        _suspend = true;
        try
        {
            _allRows.Clear();
            PayerOptions.Clear();

            Track(new ParticipantRow { Name = "You", IsSelected = true });
            foreach (var (id, name) in people)
                Track(new ParticipantRow { ContactId = id, Name = name, IsSelected = groupId is not null });

            SelectedPayer = PayerOptions.FirstOrDefault();
        }
        finally
        {
            _suspend = false;
        }

        ContactSearch = string.Empty;
        ApplySearch();
        PrefillInputs();
        Recalculate();
    }

    private ParticipantRow Track(ParticipantRow row, int? insertAt = null)
    {
        row.PropertyChanged += OnRowChanged;

        if (insertAt is int index && index >= 0 && index <= _allRows.Count)
        {
            _allRows.Insert(index, row);
            PayerOptions.Insert(index, row);
        }
        else
        {
            _allRows.Add(row);
            PayerOptions.Add(row);
        }

        return row;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suspend) return;

        if (e.PropertyName is nameof(ParticipantRow.IsSelected) or nameof(ParticipantRow.Input))
            Recalculate();
    }

    // ─────────────────────────────────────────────
    //  Search (selected people always stay visible)
    // ─────────────────────────────────────────────
    private void ApplySearch()
    {
        var q = ContactSearch?.Trim() ?? string.Empty;

        var rows = _allRows.Where(r =>
            r.IsMe || r.IsSelected || q.Length == 0 ||
            r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

        VisibleRows.Clear();
        foreach (var r in rows) VisibleRows.Add(r);
    }

    // ─────────────────────────────────────────────
    //  Quick add friend (not in contacts yet)
    // ─────────────────────────────────────────────
    [RelayCommand]
    private void OpenAddFriend()
    {
        // If the user already typed a name in the search box, start from that.
        NewFriendName = ContactSearch?.Trim() ?? string.Empty;
        NewFriendMobile = string.Empty;
        IsAddFriendOpen = true;
    }

    [RelayCommand]
    private void CancelAddFriend()
    {
        IsAddFriendOpen = false;
        NewFriendName = string.Empty;
        NewFriendMobile = string.Empty;
    }

    [RelayCommand]
    private async Task SaveFriendAsync()
    {
        if (IsBusy) return;

        var groupId = CurrentGroupId;
        AddFriendResult? result = null;
        SplitResult? memberResult = null;

        await ExecuteAsync(async () =>
        {
            result = await _splitService.AddFriendAsync(NewFriendName, NewFriendMobile);

            // Inside a group, the new friend also becomes a member of that group.
            if (result.Success && groupId is int gid)
                memberResult = await _groupService.AddMemberAsync(gid, result.ContactId);
        });

        if (result is null) return;

        if (!result.Success)
        {
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not add the friend.");
            return;
        }

        if (memberResult is { Success: false })
        {
            await SplitPrompts.AlertAsync(memberResult.ErrorMessage ?? "Could not add the friend to the group.");
            return;
        }

        if (!_allContacts.Any(c => c.Id == result.ContactId))
            _allContacts.Add((result.ContactId, result.Name));

        // Inserting into the picker's source can reset its selection — remember and restore.
        var payer = SelectedPayer;

        var row = _allRows.FirstOrDefault(r => r.ContactId == result.ContactId)
                  ?? Track(new ParticipantRow { ContactId = result.ContactId, Name = result.Name }, insertAt: 1);

        row.IsSelected = true;

        SelectedPayer = payer ?? PayerOptions.FirstOrDefault();

        // Back to the normal list with the new friend ticked, ready to continue.
        ContactSearch = string.Empty;
        ApplySearch();
        Recalculate();

        CancelAddFriend();
    }

    // ─────────────────────────────────────────────
    //  Split maths
    // ─────────────────────────────────────────────
    [RelayCommand]
    private void SetMethod(string method) => SelectedMethod = method;

    /// <summary>When switching to Exact/Percent, start from an equal split so the user only tweaks.</summary>
    private void PrefillInputs()
    {
        var selected = _allRows.Where(r => r.IsSelected).ToList();

        _suspend = true;
        try
        {
            if (SelectedMethod == "Exact")
            {
                var parts = SplitCalculator.Equal(SplitCalculator.ParseAmount(TotalText), selected.Count);
                for (var i = 0; i < selected.Count; i++)
                    selected[i].Input = parts[i] == 0m ? string.Empty : parts[i].ToString("0.##", CultureInfo.CurrentCulture);
            }
            else if (SelectedMethod == "Percent")
            {
                var parts = SplitCalculator.Equal(100m, selected.Count);
                for (var i = 0; i < selected.Count; i++)
                    selected[i].Input = parts[i].ToString("0.##", CultureInfo.CurrentCulture);
            }
            else
            {
                foreach (var r in _allRows) r.Input = string.Empty;
            }
        }
        finally
        {
            _suspend = false;
        }
    }

    private void Recalculate()
    {
        if (_allRows.Count == 0) return;

        var total = SplitCalculator.ParseAmount(TotalText);
        var selected = _allRows.Where(r => r.IsSelected).ToList();

        _suspend = true;
        try
        {
            foreach (var r in _allRows)
            {
                r.ShowInput = SelectedMethod != "Equal" && r.IsSelected;
                r.InputHint = SelectedMethod == "Percent" ? "%" : "₹";
                if (!r.IsSelected) r.Share = 0m;
            }

            switch (SelectedMethod)
            {
                case "Exact":
                {
                    foreach (var r in selected) r.Share = SplitCalculator.ParseAmount(r.Input);

                    var diff = total - selected.Sum(r => r.Share);
                    IsSplitValid = total > 0 && selected.Count > 0 && diff == 0m;
                    SummaryText = total <= 0 ? "Enter the total amount"
                        : diff == 0m ? "All assigned"
                        : diff > 0 ? $"₹{diff:N2} left to assign"
                        : $"₹{-diff:N2} over the total";
                    break;
                }

                case "Percent":
                {
                    var percents = selected.Select(r => SplitCalculator.ParseAmount(r.Input)).ToList();
                    var amounts = SplitCalculator.Percent(total, percents);
                    for (var i = 0; i < selected.Count; i++) selected[i].Share = amounts[i];

                    var sumPercent = percents.Sum();
                    IsSplitValid = total > 0 && selected.Count > 0 && sumPercent == 100m;
                    SummaryText = total <= 0 ? "Enter the total amount"
                        : sumPercent == 100m ? "100% assigned"
                        : $"{sumPercent:0.##}% of 100% assigned";
                    break;
                }

                default: // Equal
                {
                    var parts = SplitCalculator.Equal(total, selected.Count);
                    for (var i = 0; i < selected.Count; i++) selected[i].Share = parts[i];

                    IsSplitValid = total > 0 && selected.Count > 0;
                    SummaryText = total <= 0 ? "Enter the total amount"
                        : selected.Count == 0 ? "Pick who is in this split"
                        : $"₹{SplitCalculator.Money(total)} ÷ {selected.Count} people";
                    break;
                }
            }
        }
        finally
        {
            _suspend = false;
        }
    }

    // ─────────────────────────────────────────────
    //  Save
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;

        var total = SplitCalculator.ParseAmount(TotalText);

        if (string.IsNullOrWhiteSpace(ExpenseTitle)) { await SplitPrompts.AlertAsync("Enter what this expense was for."); return; }
        if (total <= 0) { await SplitPrompts.AlertAsync("Enter a valid total amount."); return; }

        var me = _allRows.First(r => r.IsMe);
        var payerRow = SelectedPayer ?? me;
        var paidByMe = payerRow.IsMe;
        var groupId = CurrentGroupId;

        if (paidByMe && SelectedAccount is null) { await SplitPrompts.AlertAsync("Select the account you paid from."); return; }

        var myShare = me.IsSelected ? me.Share : 0m;
        if (myShare > 0 && SelectedCategory is null) { await SplitPrompts.AlertAsync("Select a category for your share."); return; }

        // Everyone selected except you (the payer is included if they took a share).
        var others = _allRows.Where(r => r.IsSelected && !r.IsMe && r.Share > 0).ToList();

        if (paidByMe && others.Count == 0) { await SplitPrompts.AlertAsync("Pick at least one friend with a share above zero."); return; }

        if (!paidByMe && myShare <= 0 && groupId is null)
        {
            await SplitPrompts.AlertAsync("You need a share in this bill. To track a bill between other people, pick a group.");
            return;
        }

        if (!IsSplitValid) { await SplitPrompts.AlertAsync(SummaryText); return; }

        var request = new SplitRequest(
            ExpenseTitle.Trim(),
            total,
            SelectedMethod,
            SplitDate.Date + DateTime.Now.TimeOfDay,
            paidByMe ? SelectedAccount!.Id : 0,
            SelectedCategory?.Id ?? 0,
            myShare,
            others.Select(f => new SplitShareInput(f.ContactId!.Value, f.Share)).ToList(),
            string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            PaidByContactId: payerRow.ContactId,
            GroupId: groupId);

        SplitResult? result = null;
        await ExecuteAsync(async () => result = await _splitService.CreateAsync(request));

        if (result is null) return;

        if (result.Success)
        {
            try { await _sharedRequests.SendSplitRequestsAsync(result.SplitId); }
            catch (Exception ex) { CrashLogger.Log(ex, "AddSplit.SendShared"); }
            await Shell.Current.GoToAsync("..");
        }
        else
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not save the split.");
    }
}
