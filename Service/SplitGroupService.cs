using MoneySpend.Models;
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public class SplitGroupService : ISplitGroupService
{
    private readonly IGenericRepository<SplitGroup> _groupRepo;
    private readonly IGenericRepository<SplitGroupMember> _memberRepo;
    private readonly IGenericRepository<GroupSettlement> _settleRepo;
    private readonly IGenericRepository<SplitExpense> _splitRepo;
    private readonly IGenericRepository<SplitShare> _shareRepo;
    private readonly IGenericRepository<Contact> _contactRepo;
    private readonly IBorrowLendService _borrowLend;
    private readonly ISplitService _splitService;

    public SplitGroupService(
        IGenericRepository<SplitGroup> groupRepo,
        IGenericRepository<SplitGroupMember> memberRepo,
        IGenericRepository<GroupSettlement> settleRepo,
        IGenericRepository<SplitExpense> splitRepo,
        IGenericRepository<SplitShare> shareRepo,
        IGenericRepository<Contact> contactRepo,
        IBorrowLendService borrowLend,
        ISplitService splitService)
    {
        _groupRepo = groupRepo;
        _memberRepo = memberRepo;
        _settleRepo = settleRepo;
        _splitRepo = splitRepo;
        _shareRepo = shareRepo;
        _contactRepo = contactRepo;
        _borrowLend = borrowLend;
        _splitService = splitService;
    }

    // ─────────────────────────────────────────────
    //  Ledger maths (nets per member; 0 = you)
    //
    //  net > 0  → the member is owed money
    //  net < 0  → the member owes money
    //
    //  Sources:
    //   • bills: payer +total, everyone -their share
    //   • settlements involving YOU: derived from BorrowLend (how much of each share is no
    //     longer pending) — so paying/receiving from the Friends tab or from here both count
    //   • settlements between OTHER members: GroupSettlement rows
    // ─────────────────────────────────────────────
    private sealed class Ledger
    {
        public List<SplitExpense> Splits { get; set; } = new();
        public List<SplitShare> Shares { get; set; } = new();
        public Dictionary<int, BorrowLend> BorrowLends { get; set; } = new();
        public List<GroupSettlement> Settlements { get; set; } = new();
        public Dictionary<int, string> ContactNames { get; set; } = new();

        public string NameOf(int id)
            => id == 0 ? "You" : ContactNames.TryGetValue(id, out var n) ? n : "Unknown";
    }

    private sealed record Position(
        Dictionary<int, decimal> Net,
        Dictionary<int, decimal> LendPending,
        Dictionary<int, decimal> BorrowPending);

    private async Task<Ledger> LoadLedgerAsync()
    {
        var ledger = new Ledger
        {
            Splits = (await _splitRepo.FindAsync(s => !s.IsDeleted)).Where(s => s.GroupId != null).ToList(),
            Shares = (await _shareRepo.FindAsync(s => s.SplitExpenseId > 0)).ToList(),
            BorrowLends = (await _borrowLend.GetAllAsync(null, includeClosed: true)).ToDictionary(b => b.Id),
            Settlements = (await _settleRepo.FindAsync(s => !s.IsDeleted)).ToList(),
            ContactNames = (await _contactRepo.FindAsync(c => c.Id > 0)).ToDictionary(c => c.Id, c => c.Name)
        };
        return ledger;
    }

    private static void Add(Dictionary<int, decimal> map, int key, decimal value)
        => map[key] = map.GetValueOrDefault(key) + value;

    private static Position Compute(Ledger l, int groupId)
    {
        var net = new Dictionary<int, decimal>();
        var lend = new Dictionary<int, decimal>();
        var borrow = new Dictionary<int, decimal>();

        foreach (var split in l.Splits.Where(s => s.GroupId == groupId))
        {
            var payer = split.PaidByContactId ?? 0;

            Add(net, payer, split.TotalAmount);
            if (split.MyShareAmount > 0) Add(net, 0, -split.MyShareAmount);

            var shares = l.Shares.Where(s => s.SplitExpenseId == split.Id).ToList();
            foreach (var sh in shares) Add(net, sh.ContactId, -sh.ShareAmount);

            if (payer == 0)
            {
                // You paid: each friend's share is a Lend. What's no longer pending was paid back to you.
                foreach (var sh in shares.Where(s => s.BorrowLendId > 0))
                {
                    var pending = l.BorrowLends.TryGetValue(sh.BorrowLendId, out var bl) ? bl.PendingAmount : 0m;
                    var settled = sh.ShareAmount - pending;

                    Add(net, sh.ContactId, settled);
                    Add(net, 0, -settled);
                    Add(lend, sh.ContactId, pending);
                }
            }
            else if (split.MyBorrowLendId is int myBl && myBl > 0)
            {
                // A friend paid: your share is a Borrow. What's no longer pending, you paid them.
                var pending = l.BorrowLends.TryGetValue(myBl, out var bl) ? bl.PendingAmount : 0m;
                var settled = split.MyShareAmount - pending;

                Add(net, 0, settled);
                Add(net, payer, -settled);
                Add(borrow, payer, pending);
            }
        }

        foreach (var st in l.Settlements.Where(s => s.GroupId == groupId))
        {
            Add(net, st.FromContactId, st.Amount);
            Add(net, st.ToContactId, -st.Amount);
        }

        return new Position(net, lend, borrow);
    }

    /// <summary>Greedy "fewest payments": biggest debtor pays biggest creditor, repeat.</summary>
    private static List<(int From, int To, decimal Amount)> Simplify(Dictionary<int, decimal> net)
    {
        var creditors = net.Where(k => k.Value > 0).Select(k => (Id: k.Key, Amt: k.Value)).ToList();
        var debtors = net.Where(k => k.Value < 0).Select(k => (Id: k.Key, Amt: -k.Value)).ToList();
        var result = new List<(int From, int To, decimal Amount)>();

        while (creditors.Count > 0 && debtors.Count > 0)
        {
            creditors = creditors.OrderByDescending(c => c.Amt).ToList();
            debtors = debtors.OrderByDescending(d => d.Amt).ToList();

            var c = creditors[0];
            var d = debtors[0];
            var x = Math.Min(c.Amt, d.Amt);

            result.Add((d.Id, c.Id, x));

            if (c.Amt == x) creditors.RemoveAt(0); else creditors[0] = (c.Id, c.Amt - x);
            if (d.Amt == x) debtors.RemoveAt(0); else debtors[0] = (d.Id, d.Amt - x);
        }

        return result;
    }

    // ─────────────────────────────────────────────
    //  Queries
    // ─────────────────────────────────────────────
    public async Task<List<GroupListItem>> GetGroupsAsync()
    {
        var groups = (await _groupRepo.FindAsync(g => !g.IsDeleted)).OrderBy(g => g.Name).ToList();
        if (groups.Count == 0) return new List<GroupListItem>();

        var members = (await _memberRepo.FindAsync(m => m.GroupId > 0)).ToList();
        var ledger = await LoadLedgerAsync();

        return groups.Select(g =>
        {
            var position = Compute(ledger, g.Id);
            return new GroupListItem
            {
                Id = g.Id,
                Name = g.Name,
                MemberCount = members.Count(m => m.GroupId == g.Id) + 1,
                ExpenseCount = ledger.Splits.Count(s => s.GroupId == g.Id),
                MyNet = position.Net.GetValueOrDefault(0)
            };
        }).ToList();
    }

    public async Task<List<SplitGroup>> GetGroupEntitiesAsync()
    {
        var groups = await _groupRepo.FindAsync(g => !g.IsDeleted);
        return groups.OrderBy(g => g.Name).ToList();
    }

    public async Task<List<Contact>> GetMembersAsync(int groupId)
    {
        var ids = (await _memberRepo.FindAsync(m => m.GroupId == groupId))
            .Select(m => m.ContactId)
            .ToHashSet();

        var contacts = await _contactRepo.FindAsync(c => !c.IsDeleted);
        return contacts.Where(c => ids.Contains(c.Id)).OrderBy(c => c.Name).ToList();
    }

    public async Task<GroupDetail?> GetDetailAsync(int groupId)
    {
        var group = await _groupRepo.GetByIdAsync(groupId);
        if (group is null || group.IsDeleted) return null;

        var memberIds = (await _memberRepo.FindAsync(m => m.GroupId == groupId))
            .Select(m => m.ContactId)
            .Distinct()
            .ToList();

        var ledger = await LoadLedgerAsync();
        var position = Compute(ledger, groupId);

        var ids = new List<int> { 0 };
        ids.AddRange(memberIds);
        foreach (var key in position.Net.Keys)
            if (!ids.Contains(key)) ids.Add(key); // safety: anyone with a balance is shown

        var balances = ids
            .Select(id => new MemberBalance
            {
                ContactId = id,
                Name = ledger.NameOf(id),
                Net = position.Net.GetValueOrDefault(id)
            })
            .OrderByDescending(b => b.Net)
            .ThenBy(b => b.Name)
            .ToList();

        var transfers = Simplify(position.Net).Select(t =>
        {
            var kind = t.From == 0 ? "IPay" : t.To == 0 ? "PayMe" : "Others";

            var direct = kind switch
            {
                "IPay" => position.BorrowPending.GetValueOrDefault(t.To),
                "PayMe" => position.LendPending.GetValueOrDefault(t.From),
                _ => 0m
            };

            var canAct = kind == "Others" || direct > 0;

            return new GroupTransfer
            {
                FromId = t.From,
                ToId = t.To,
                FromName = ledger.NameOf(t.From),
                ToName = ledger.NameOf(t.To),
                Amount = t.Amount,
                Kind = kind,
                DirectPending = direct,
                CanAct = canAct,
                ActionAmount = kind == "Others" ? t.Amount : Math.Min(t.Amount, direct),
                ActionLabel = kind switch { "IPay" => "Pay", "PayMe" => "Received", _ => "Mark paid" },
                HintText = canAct ? string.Empty : "Indirect — settle your direct dues first"
            };
        }).ToList();

        return new GroupDetail
        {
            GroupId = groupId,
            Name = group.Name,
            MyNet = position.Net.GetValueOrDefault(0),
            Balances = balances,
            Transfers = transfers,
            Expenses = await _splitService.GetSplitsAsync(groupId)
        };
    }

    // ─────────────────────────────────────────────
    //  Save / members / delete
    // ─────────────────────────────────────────────
    public async Task<GroupSaveResult> SaveGroupAsync(int? groupId, string name, IReadOnlyList<int> memberContactIds)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0) return new GroupSaveResult(false, 0, "Enter a group name.");
        if (name.Length > 100) return new GroupSaveResult(false, 0, "Group name is too long.");

        var ids = memberContactIds.Distinct().ToList();
        if (ids.Count == 0) return new GroupSaveResult(false, 0, "Pick at least one friend for the group.");

        SplitGroup group;
        if (groupId is int existingId && existingId > 0)
        {
            var found = await _groupRepo.GetByIdAsync(existingId);
            if (found is null || found.IsDeleted) return new GroupSaveResult(false, 0, "Group not found.");

            group = found;
            group.Name = name;
            group.UpdatedAt = DateTime.UtcNow;
            await _groupRepo.UpdateAsync(group);
        }
        else
        {
            group = new SplitGroup { Name = name };
            await _groupRepo.AddAsync(group);
        }

        var current = (await _memberRepo.FindAsync(m => m.GroupId == group.Id))
            .Select(m => m.ContactId)
            .ToHashSet();

        foreach (var id in ids.Where(id => !current.Contains(id)))
            await _memberRepo.AddAsync(new SplitGroupMember { GroupId = group.Id, ContactId = id });

        DataChangeNotifier.Publish<SplitGroup>();
        return new GroupSaveResult(true, group.Id);
    }

    public async Task<SplitResult> AddMemberAsync(int groupId, int contactId)
    {
        var exists = (await _memberRepo.FindAsync(m => m.GroupId == groupId))
            .Any(m => m.ContactId == contactId);

        if (!exists)
        {
            await _memberRepo.AddAsync(new SplitGroupMember { GroupId = groupId, ContactId = contactId });
            DataChangeNotifier.Publish<SplitGroup>();
        }

        return new SplitResult(true);
    }

    public async Task<SplitResult> DeleteGroupAsync(int groupId)
    {
        var group = await _groupRepo.GetByIdAsync(groupId);
        if (group is null || group.IsDeleted) return new SplitResult(false, "Group not found.");

        var hasExpenses = (await _splitRepo.FindAsync(s => !s.IsDeleted)).Any(s => s.GroupId == groupId);
        if (hasExpenses)
            return new SplitResult(false, "This group still has expenses. Delete them first so your account balances are restored properly.");

        group.IsDeleted = true;
        group.UpdatedAt = DateTime.UtcNow;
        await _groupRepo.UpdateAsync(group);

        var settlements = (await _settleRepo.FindAsync(s => !s.IsDeleted)).Where(s => s.GroupId == groupId);
        foreach (var s in settlements)
        {
            s.IsDeleted = true;
            await _settleRepo.UpdateAsync(s);
        }

        DataChangeNotifier.Publish<SplitGroup>();
        return new SplitResult(true);
    }

    public async Task<SplitResult> MarkPaidAsync(int groupId, int fromContactId, int toContactId, decimal amount)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        if (amount <= 0) return new SplitResult(false, "Enter a valid amount.");
        if (fromContactId == 0 || toContactId == 0)
            return new SplitResult(false, "Payments involving you are recorded with Pay / Received.");
        if (fromContactId == toContactId) return new SplitResult(false, "Pick two different people.");

        // Only allow what the group's books actually support.
        var position = Compute(await LoadLedgerAsync(), groupId);
        var maxAllowed = Math.Min(-position.Net.GetValueOrDefault(fromContactId), position.Net.GetValueOrDefault(toContactId));

        if (maxAllowed <= 0)
            return new SplitResult(false, "Nothing is pending between these two people.");
        if (amount > maxAllowed)
            return new SplitResult(false, $"Amount can't exceed ₹{maxAllowed:N2}.");

        await _settleRepo.AddAsync(new GroupSettlement
        {
            GroupId = groupId,
            FromContactId = fromContactId,
            ToContactId = toContactId,
            Amount = amount,
            SettledDate = DateTime.Now
        });

        DataChangeNotifier.Publish<GroupSettlement>();
        return new SplitResult(true);
    }
}
