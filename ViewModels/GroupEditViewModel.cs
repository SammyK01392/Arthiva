using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoneySpend.Services;

namespace MoneySpend.ViewModels;

public partial class MemberRow : ObservableObject
{
    public int ContactId { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>Already in the group — can't be removed (their history would break).</summary>
    public bool IsLocked { get; init; }
    public bool CanToggle => !IsLocked;

    [ObservableProperty] private bool isSelected;
}

/// <summary>Route: GroupEditPage (new group) or GroupEditPage?GroupId=5 (add members / rename).</summary>
[QueryProperty(nameof(GroupIdText), "GroupId")]
public partial class GroupEditViewModel : BaseViewModel
{
    private readonly ISplitGroupService _groupService;
    private readonly ISplitService _splitService;

    private readonly List<MemberRow> _allRows = new();
    private bool _loaded;

    public ObservableCollection<MemberRow> VisibleRows { get; } = new();

    [ObservableProperty] private string groupIdText = string.Empty;
    [ObservableProperty] private string groupName = string.Empty;
    [ObservableProperty] private string contactSearch = string.Empty;

    [ObservableProperty] private bool isAddFriendOpen;
    [ObservableProperty] private string newFriendName = string.Empty;
    [ObservableProperty] private string newFriendMobile = string.Empty;

    private int GroupId => int.TryParse(GroupIdText, out var id) ? id : 0;
    public bool IsEditMode => GroupId > 0;

    partial void OnContactSearchChanged(string value) => ApplySearch();

    public GroupEditViewModel(ISplitGroupService groupService, ISplitService splitService)
    {
        _groupService = groupService;
        _splitService = splitService;
        Title = "New Group";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loaded) return;

        await ExecuteAsync(async () =>
        {
            var contacts = await _splitService.GetContactsAsync();

            var existingIds = new HashSet<int>();
            if (IsEditMode)
            {
                Title = "Add Members";

                foreach (var m in await _groupService.GetMembersAsync(GroupId))
                    existingIds.Add(m.Id);

                var groups = await _groupService.GetGroupEntitiesAsync();
                GroupName = groups.FirstOrDefault(g => g.Id == GroupId)?.Name ?? string.Empty;
            }

            _allRows.Clear();
            foreach (var c in contacts)
            {
                var isMember = existingIds.Contains(c.Id);
                _allRows.Add(new MemberRow
                {
                    ContactId = c.Id,
                    Name = c.Name,
                    IsLocked = isMember,
                    IsSelected = isMember
                });
            }

            ApplySearch();
            _loaded = true;
        });
    }

    private void ApplySearch()
    {
        var q = ContactSearch?.Trim() ?? string.Empty;

        var rows = _allRows.Where(r =>
            r.IsSelected || q.Length == 0 ||
            r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));

        VisibleRows.Clear();
        foreach (var r in rows) VisibleRows.Add(r);
    }

    // ─────────────────────────────────────────────
    //  Quick add friend
    // ─────────────────────────────────────────────
    [RelayCommand]
    private void OpenAddFriend()
    {
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

        AddFriendResult? result = null;
        await ExecuteAsync(async () => result = await _splitService.AddFriendAsync(NewFriendName, NewFriendMobile));

        if (result is null) return;

        if (!result.Success)
        {
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not add the friend.");
            return;
        }

        var row = _allRows.FirstOrDefault(r => r.ContactId == result.ContactId);
        if (row is null)
        {
            row = new MemberRow { ContactId = result.ContactId, Name = result.Name };
            _allRows.Insert(0, row);
        }

        row.IsSelected = true;

        ContactSearch = string.Empty;
        ApplySearch();
        CancelAddFriend();
    }

    // ─────────────────────────────────────────────
    //  Save
    // ─────────────────────────────────────────────
    [RelayCommand]
    private async Task RemoveMemberAsync(MemberRow row)
    {
        if (row is null || !IsEditMode) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Remove member?", $"Remove {row.Name} from this group?", "Remove", "Cancel");
        if (!confirm) return;

        SplitResult? result = null;
        await ExecuteAsync(async () => result = await _groupService.RemoveMemberAsync(GroupId, row.ContactId));

        if (result is null) return;

        if (result.Success)
        {
            _allRows.Remove(row);
            ApplySearch();
        }
        else
        {
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not remove the member.");
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy) return;

        var ids = _allRows.Where(r => r.IsSelected).Select(r => r.ContactId).ToList();

        GroupSaveResult? result = null;
        await ExecuteAsync(async () =>
            result = await _groupService.SaveGroupAsync(IsEditMode ? GroupId : null, GroupName, ids));

        if (result is null) return;

        if (result.Success)
            await Shell.Current.GoToAsync("..");
        else
            await SplitPrompts.AlertAsync(result.ErrorMessage ?? "Could not save the group.");
    }
}
