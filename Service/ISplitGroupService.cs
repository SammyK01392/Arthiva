using MoneySpend.Models;
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public class GroupListItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int MemberCount { get; init; }   // includes you
    public int ExpenseCount { get; init; }
    public decimal MyNet { get; init; }     // + = you are owed, - = you owe

    public string InfoText => $"{MemberCount} members • {ExpenseCount} expenses";
    public string SummaryText => MyNet > 0 ? $"You are owed ₹{MyNet:N2}"
        : MyNet < 0 ? $"You owe ₹{-MyNet:N2}"
        : "All settled up";
    public bool IsOwed => MyNet > 0;
    public bool IsOwing => MyNet < 0;
}

public class MemberBalance
{
    /// <summary>0 = you.</summary>
    public int ContactId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Net { get; init; }

    public decimal AbsNet => Math.Abs(Net);
    public bool IsPositive => Net > 0;
    public bool IsNegative => Net < 0;
    public string StatusText => Net > 0 ? "gets back" : Net < 0 ? "owes" : "settled up";
}

/// <summary>One line of the "fewest payments" plan.</summary>
public class GroupTransfer
{
    public int FromId { get; init; }   // 0 = you
    public int ToId { get; init; }     // 0 = you
    public string FromName { get; init; } = string.Empty;
    public string ToName { get; init; } = string.Empty;
    public decimal Amount { get; init; }

    /// <summary>"Others" (neither is you) / "IPay" / "PayMe".</summary>
    public string Kind { get; init; } = "Others";

    /// <summary>For lines involving you: what you really owe / are owed with that exact person in this group.</summary>
    public decimal DirectPending { get; init; }

    public bool CanAct { get; init; }
    public bool ShowHint => !CanAct;
    public string ActionLabel { get; init; } = string.Empty;
    public string HintText { get; init; } = string.Empty;

    /// <summary>Default amount offered in the prompt.</summary>
    public decimal ActionAmount { get; init; }

    public string Title => $"{FromName} → {ToName}";
}

public class GroupDetail
{
    public int GroupId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal MyNet { get; init; }
    public List<MemberBalance> Balances { get; init; } = new();
    public List<GroupTransfer> Transfers { get; init; } = new();
    public List<SplitListItem> Expenses { get; init; } = new();
}

public record GroupSaveResult(bool Success, int GroupId, string? ErrorMessage = null);

public interface ISplitGroupService
{
    Task<List<GroupListItem>> GetGroupsAsync();

    /// <summary>Raw groups for pickers.</summary>
    Task<List<SplitGroup>> GetGroupEntitiesAsync();

    /// <summary>Member contacts (without you).</summary>
    Task<List<Contact>> GetMembersAsync(int groupId);

    Task<GroupDetail?> GetDetailAsync(int groupId);

    /// <summary>groupId null = create. When editing, only NEW members are added (members with history can't be removed).</summary>
    Task<GroupSaveResult> SaveGroupAsync(int? groupId, string name, IReadOnlyList<int> memberContactIds);

    Task<SplitResult> AddMemberAsync(int groupId, int contactId);

    /// <summary>Blocked while the group still has expenses (delete those first so account balances are restored properly).</summary>
    Task<SplitResult> DeleteGroupAsync(int groupId);

    /// <summary>"From paid To" between two other members. Balances only — no account is touched.</summary>
    Task<SplitResult> MarkPaidAsync(int groupId, int fromContactId, int toContactId, decimal amount);
}
