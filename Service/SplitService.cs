using MoneySpend.Models;
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public class SplitService : ISplitService
{
    private readonly IGenericRepository<SplitExpense> _splitRepo;
    private readonly IGenericRepository<SplitShare> _shareRepo;
    private readonly IGenericRepository<SplitGroup> _groupRepo;
    private readonly IGenericRepository<Transaction> _txRepo;
    private readonly IGenericRepository<Contact> _contactRepo;
    private readonly IBorrowLendService _borrowLend;
    private readonly ITransactionService _transactionService;

    public SplitService(
        IGenericRepository<SplitExpense> splitRepo,
        IGenericRepository<SplitShare> shareRepo,
        IGenericRepository<SplitGroup> groupRepo,
        IGenericRepository<Transaction> txRepo,
        IGenericRepository<Contact> contactRepo,
        IBorrowLendService borrowLend,
        ITransactionService transactionService)
    {
        _splitRepo = splitRepo;
        _shareRepo = shareRepo;
        _groupRepo = groupRepo;
        _txRepo = txRepo;
        _contactRepo = contactRepo;
        _borrowLend = borrowLend;
        _transactionService = transactionService;
    }

    // ─────────────────────────────────────────────
    //  Contacts
    // ─────────────────────────────────────────────
    public async Task<List<Contact>> GetContactsAsync()
    {
        var contacts = await _contactRepo.FindAsync(c => !c.IsDeleted);
        return contacts.OrderBy(c => c.Name).ToList();
    }

    public async Task<AddFriendResult> AddFriendAsync(string name, string? mobile)
    {
        name = name?.Trim() ?? string.Empty;

        if (name.Length == 0)
            return new AddFriendResult(false, 0, string.Empty, "Enter the friend's name.");
        if (name.Length > 100)
            return new AddFriendResult(false, 0, name, "Name is too long.");

        string? digits = null;
        if (!string.IsNullOrWhiteSpace(mobile))
        {
            digits = new string(mobile.Where(char.IsDigit).ToArray());

            if (digits.Length == 0)
                digits = null;
            else if (digits.Length < 10 || digits.Length > 15)
                return new AddFriendResult(false, 0, name, "Enter a valid mobile number, or leave it empty.");
        }

        // Same name already in contacts? Reuse it instead of creating a duplicate.
        var existing = (await _contactRepo.FindAsync(c => !c.IsDeleted))
            .FirstOrDefault(c => string.Equals(c.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
            return new AddFriendResult(true, existing.Id, existing.Name, null, AlreadyExisted: true);

        var contact = new Contact
        {
            Name = name,
            Mobile = digits,
            ContactType = "Friend"
        };

        await _contactRepo.AddAsync(contact);
        DataChangeNotifier.Publish<Contact>();

        return new AddFriendResult(true, contact.Id, contact.Name);
    }

    // ─────────────────────────────────────────────
    //  Create
    // ─────────────────────────────────────────────
    public async Task<SplitResult> CreateAsync(SplitRequest r)
    {
        var title = r.Title?.Trim() ?? string.Empty;
        var paidByMe = r.PaidByContactId is null;

        if (title.Length == 0) return Fail("Enter what this expense was for.");
        if (r.TotalAmount <= 0) return Fail("Enter a valid total amount.");
        if (paidByMe && r.AccountId <= 0) return Fail("Select the account you paid from.");
        if (r.MyShare < 0) return Fail("Your share can't be negative.");
        if (r.MyShare > 0 && r.CategoryId <= 0) return Fail("Select a category for your share.");
        if (r.Shares.Any(s => s.Amount <= 0)) return Fail("Every selected friend needs a share above zero.");
        if (r.Shares.GroupBy(s => s.ContactId).Any(g => g.Count() > 1)) return Fail("A friend is selected twice.");
        if (paidByMe && r.Shares.Count == 0) return Fail("Pick at least one friend with a share above zero.");
        if (!paidByMe && r.MyShare <= 0 && r.GroupId is null)
            return Fail("You need a share in this bill. To track a bill between other people, create it inside a group.");

        var sum = r.MyShare + r.Shares.Sum(s => s.Amount);
        if (sum != r.TotalAmount)
            return Fail($"Shares add up to ₹{sum:N2} but the total is ₹{r.TotalAmount:N2}.");

        Contact? payer = null;
        if (!paidByMe)
        {
            payer = await _contactRepo.GetByIdAsync(r.PaidByContactId!.Value);
            if (payer is null) return Fail("The person who paid was not found.");
        }

        var now = DateTime.UtcNow;
        var split = new SplitExpense
        {
            Title = title,
            TotalAmount = r.TotalAmount,
            MyShareAmount = r.MyShare,
            SplitMethod = r.Method,
            SplitDate = r.Date,
            AccountId = paidByMe ? r.AccountId : 0,
            CategoryId = r.CategoryId,
            Notes = r.Notes,
            GroupId = r.GroupId,
            PaidByContactId = r.PaidByContactId,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _splitRepo.AddAsync(split);

        var createdBorrowLendIds = new List<int>();

        try
        {
            if (paidByMe)
            {
                // Your own share → normal Expense (counts in budgets/reports).
                if (r.MyShare > 0)
                {
                    var myTxn = new Transaction
                    {
                        AccountId = r.AccountId,
                        CategoryId = r.CategoryId,
                        Amount = r.MyShare,
                        TransactionType = "Expense",
                        TransactionDate = r.Date,
                        Description = $"{title} - my share (split ₹{SplitCalculator.Money(r.TotalAmount)})",
                        SourceType = "Split",
                        SourceReferenceId = split.Id
                    };
                    await _transactionService.AddTransactionAsync(myTxn);
                    split.MyTransactionId = myTxn.Id;
                }

                // Each friend → Lend record (also debits the account via BorrowLendService).
                foreach (var share in r.Shares)
                {
                    var contact = await _contactRepo.GetByIdAsync(share.ContactId);

                    var bl = new BorrowLend
                    {
                        ContactId = share.ContactId,
                        Type = "Lend",
                        TotalAmount = share.Amount,
                        GivenDate = r.Date,
                        Notes = $"Split: {title}",
                        ReminderEnabled = false // no due date for splits; avoid stray notifications
                    };

                    try
                    {
                        await _borrowLend.CreateAsync(bl, r.AccountId);
                    }
                    finally
                    {
                        // Even if CreateAsync failed midway, remember it so rollback can clean up.
                        if (bl.Id > 0) createdBorrowLendIds.Add(bl.Id);
                    }

                    await RelabelLendTransactionsAsync(bl.Id, $"Split: {title} - {contact?.Name}");

                    await _shareRepo.AddAsync(new SplitShare
                    {
                        SplitExpenseId = split.Id,
                        ContactId = share.ContactId,
                        ShareAmount = share.Amount,
                        BorrowLendId = bl.Id
                    });
                }
            }
            else
            {
                // A friend paid. Cash-basis: NOTHING leaves your account now.
                // Record what you owe them as a Borrow (no account entry); the Expense is booked
                // when you pay them back (see PayAsync).
                if (r.MyShare > 0)
                {
                    var bl = new BorrowLend
                    {
                        ContactId = payer!.Id,
                        Type = "Borrow",
                        TotalAmount = r.MyShare,
                        GivenDate = r.Date,
                        Notes = $"Split: {title}",
                        ReminderEnabled = false
                    };

                    try
                    {
                        await _borrowLend.CreateAsync(bl, null); // null account = no Transaction created
                    }
                    finally
                    {
                        if (bl.Id > 0) createdBorrowLendIds.Add(bl.Id);
                    }

                    split.MyBorrowLendId = bl.Id;
                }

                // Other people's shares are only for the group's books (no money moves for you).
                foreach (var share in r.Shares)
                {
                    await _shareRepo.AddAsync(new SplitShare
                    {
                        SplitExpenseId = split.Id,
                        ContactId = share.ContactId,
                        ShareAmount = share.Amount,
                        BorrowLendId = 0
                    });
                }
            }

            split.UpdatedAt = DateTime.UtcNow;
            await _splitRepo.UpdateAsync(split);
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "SplitService.CreateAsync");
            await RollbackAsync(split, createdBorrowLendIds);
            return Fail("Could not save the split, so nothing was changed. Please try again.");
        }

        DataChangeNotifier.Publish<SplitExpense>();
        DataChangeNotifier.Publish<BorrowLend>();
        return new SplitResult(true);
    }

    // ─────────────────────────────────────────────
    //  Delete
    // ─────────────────────────────────────────────
    public async Task<SplitResult> DeleteAsync(int splitId)
    {
        var split = await _splitRepo.GetByIdAsync(splitId);
        if (split is null || split.IsDeleted) return Fail("Split not found.");

        var shares = await _shareRepo.FindAsync(s => s.SplitExpenseId == splitId);

        try
        {
            foreach (var share in shares.Where(s => s.BorrowLendId > 0))
                await ReverseBorrowLendAsync(share.BorrowLendId);

            if (split.MyBorrowLendId is int myBl && myBl > 0)
                await ReverseBorrowLendAsync(myBl);

            // Your share Expense (you paid) and any "paid back to a friend" expenses (friend paid).
            await DeleteSplitTransactionsAsync(split.Id);

            split.IsDeleted = true;
            split.UpdatedAt = DateTime.UtcNow;
            await _splitRepo.UpdateAsync(split);
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "SplitService.DeleteAsync");
            return Fail("Could not delete the split. Please try again.");
        }

        DataChangeNotifier.Publish<SplitExpense>();
        DataChangeNotifier.Publish<BorrowLend>();
        return new SplitResult(true);
    }

    // ─────────────────────────────────────────────
    //  Queries
    // ─────────────────────────────────────────────
    public async Task<List<SplitListItem>> GetSplitsAsync(int? groupId = null)
    {
        var splits = (await _splitRepo.FindAsync(s => !s.IsDeleted))
            .Where(s => groupId is null || s.GroupId == groupId)
            .OrderByDescending(s => s.SplitDate)
            .ToList();

        if (splits.Count == 0) return new List<SplitListItem>();

        var shares = (await _shareRepo.FindAsync(s => s.SplitExpenseId > 0)).ToList();
        var borrowLends = (await _borrowLend.GetAllAsync(null, includeClosed: true)).ToDictionary(b => b.Id);
        var contacts = (await _contactRepo.FindAsync(c => c.Id > 0)).ToDictionary(c => c.Id, c => c.Name);
        var groups = (await _groupRepo.FindAsync(g => g.Id > 0)).ToDictionary(g => g.Id, g => g.Name);

        string NameOf(int id) => contacts.TryGetValue(id, out var n) ? n : "Unknown";

        var items = new List<SplitListItem>();
        foreach (var split in splits)
        {
            var mine = shares.Where(s => s.SplitExpenseId == split.Id).ToList();
            var payerId = split.PaidByContactId;

            // ── status (what it means for YOU) ──
            string status;
            bool settled;

            var lendShares = mine.Where(s => s.BorrowLendId > 0).ToList();
            if (payerId is null && lendShares.Count > 0)
            {
                var pending = lendShares.Sum(s =>
                    borrowLends.TryGetValue(s.BorrowLendId, out var bl) ? bl.PendingAmount : 0m);
                settled = pending <= 0;
                status = settled ? "Settled" : $"₹{pending:N2} pending";
            }
            else if (split.MyBorrowLendId is int myBl && myBl > 0)
            {
                var pending = borrowLends.TryGetValue(myBl, out var bl) ? bl.PendingAmount : 0m;
                settled = pending <= 0;
                status = settled ? "Settled" : $"You owe ₹{pending:N2}";
            }
            else
            {
                settled = true;
                status = "No dues for you";
            }

            // ── subtitle ──
            var parts = new List<string>
            {
                payerId is int pid ? $"{NameOf(pid)} paid" : "You paid"
            };

            if (split.GroupId is int gid && groups.TryGetValue(gid, out var groupName))
                parts.Add(groupName);

            var names = mine
                .Where(s => s.ContactId != payerId)
                .Select(s => NameOf(s.ContactId))
                .ToList();

            if (names.Count > 0)
            {
                parts.Add(names.Count <= 2
                    ? $"with {string.Join(", ", names)}"
                    : $"with {names[0]}, {names[1]} +{names.Count - 2}");
            }

            items.Add(new SplitListItem
            {
                Id = split.Id,
                Title = split.Title,
                Date = split.SplitDate,
                Total = split.TotalAmount,
                MyShare = split.MyShareAmount,
                SubtitleText = string.Join(" • ", parts),
                StatusText = status,
                IsSettled = settled
            });
        }

        return items;
    }

    public async Task<List<FriendBalance>> GetFriendBalancesAsync()
    {
        var open = (await _borrowLend.GetAllAsync(null, includeClosed: false))
            .Where(b => b.PendingAmount > 0)
            .ToList();

        if (open.Count == 0) return new List<FriendBalance>();

        var contacts = (await _contactRepo.FindAsync(c => c.Id > 0)).ToDictionary(c => c.Id);

        return open
            .GroupBy(b => b.ContactId)
            .Select(g =>
            {
                contacts.TryGetValue(g.Key, out var contact);
                return new FriendBalance
                {
                    ContactId = g.Key,
                    Name = contact?.Name ?? "Unknown",
                    Mobile = contact?.Mobile,
                    UpiId = contact?.UpiId,
                    LendPending = g.Where(b => b.Type == "Lend").Sum(b => b.PendingAmount),
                    BorrowPending = g.Where(b => b.Type == "Borrow").Sum(b => b.PendingAmount)
                };
            })
            .OrderByDescending(f => f.AbsNet)
            .ToList();
    }

    // ─────────────────────────────────────────────
    //  Settle (money received from a friend)
    // ─────────────────────────────────────────────
    public async Task<SplitResult> SettleAsync(int contactId, decimal amount, int accountId, int? groupId = null)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        if (amount <= 0) return Fail("Enter a valid amount.");
        if (accountId <= 0) return Fail("Select an account.");

        var open = (await _borrowLend.GetByContactAsync(contactId))
            .Where(b => b.Type == "Lend" && !b.IsClosed && b.PendingAmount > 0)
            .OrderBy(b => b.GivenDate)
            .ToList();

        if (groupId is int gid)
        {
            var allowed = await GroupBorrowLendIdsAsync(gid);
            open = open.Where(b => allowed.Contains(b.Id)).ToList();
        }

        var pendingTotal = open.Sum(b => b.PendingAmount);
        if (pendingTotal <= 0) return Fail("Nothing is pending with this friend.");
        if (amount > pendingTotal)
            return Fail($"Amount can't exceed the pending ₹{pendingTotal:N2}.");

        var remaining = amount;
        foreach (var bl in open)
        {
            if (remaining <= 0) break;

            var pay = Math.Min(remaining, bl.PendingAmount);
            var type = pay >= bl.PendingAmount ? "Receive" : "PartialReturn";

            var result = await _borrowLend.RecordTransactionAsync(new BorrowLendTransaction
            {
                BorrowLendId = bl.Id,
                Amount = pay,
                Type = type,
                TransactionDate = DateTime.Now,
                Notes = "Settled via Split"
            }, accountId);

            if (!result.Success)
                return Fail(result.ErrorMessage ?? "Could not record the payment.");

            remaining -= pay;
        }

        DataChangeNotifier.Publish<BorrowLend>();
        return new SplitResult(true);
    }

    // ─────────────────────────────────────────────
    //  Pay (money paid to a friend) — cash-basis moment
    // ─────────────────────────────────────────────
    public async Task<SplitResult> PayAsync(int contactId, decimal amount, int accountId, int? groupId = null)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        if (amount <= 0) return Fail("Enter a valid amount.");
        if (accountId <= 0) return Fail("Select an account.");

        var open = (await _borrowLend.GetByContactAsync(contactId))
            .Where(b => b.Type == "Borrow" && !b.IsClosed && b.PendingAmount > 0)
            .OrderBy(b => b.GivenDate)
            .ToList();

        if (groupId is int gid)
        {
            var allowed = await GroupBorrowLendIdsAsync(gid);
            open = open.Where(b => allowed.Contains(b.Id)).ToList();
        }

        var pendingTotal = open.Sum(b => b.PendingAmount);
        if (pendingTotal <= 0) return Fail("You don't owe this friend anything.");
        if (amount > pendingTotal)
            return Fail($"Amount can't exceed what you owe: ₹{pendingTotal:N2}.");

        var contact = await _contactRepo.GetByIdAsync(contactId);

        var remaining = amount;
        foreach (var bl in open)
        {
            if (remaining <= 0) break;

            var pay = Math.Min(remaining, bl.PendingAmount);
            var type = pay >= bl.PendingAmount ? "Return" : "PartialReturn";

            var move = new BorrowLendTransaction
            {
                BorrowLendId = bl.Id,
                Amount = pay,
                Type = type,
                TransactionDate = DateTime.Now,
                Notes = "Paid via Split"
            };

            var result = await _borrowLend.RecordTransactionAsync(move, accountId);
            if (!result.Success)
                return Fail(result.ErrorMessage ?? "Could not record the payment.");

            // Split-created Borrow? Then this payment is YOUR expense → book it properly.
            await CountAsExpenseAsync(bl.Id, move.TransactionId, contact?.Name);

            remaining -= pay;
        }

        DataChangeNotifier.Publish<BorrowLend>();
        DataChangeNotifier.Publish<Transaction>();
        return new SplitResult(true);
    }

    // ─────────────────────────────────────────────
    //  Reminder text
    // ─────────────────────────────────────────────
    // ─────────────────────────────────────────────
    //  Reminder text
    // ─────────────────────────────────────────────
    private static readonly HttpClient ShortenerHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>Calls one shortener API. Returns null on any failure or non-URL reply.</summary>
    private static async Task<string?> TryShortenAsync(string api)
    {
        try
        {
            var result = (await ShortenerHttp.GetStringAsync(api)).Trim();
            return result.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !result.Contains(' ')
                ? result
                : null;
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "SplitService.TryShortenAsync");
            return null;
        }
    }

    /// <summary>is.gd first, TinyURL as backup (both free, no signup). Null only if both fail.</summary>
    private static async Task<string?> ShortenAsync(string longUrl)
    {
        var enc = Uri.EscapeDataString(longUrl);

        return await TryShortenAsync($"https://is.gd/create.php?format=simple&url={enc}")
            ?? await TryShortenAsync($"https://tinyurl.com/api-create.php?url={enc}");
    }

    public async Task<string> BuildReminderTextAsync(
        int contactId, string? myUpiId = null, string? payeeName = null, string? payPageUrl = null)
    {
        var contact = await _contactRepo.GetByIdAsync(contactId);

        var open = (await _borrowLend.GetByContactAsync(contactId))
            .Where(b => b.Type == "Lend" && !b.IsClosed && b.PendingAmount > 0)
            .ToList();

        var amount = open.Sum(b => b.PendingAmount);
        var openIds = open.Select(b => b.Id).ToHashSet();

        var shares = await _shareRepo.FindAsync(s => s.ContactId == contactId);
        var titles = new List<string>();
        foreach (var share in shares.Where(s => openIds.Contains(s.BorrowLendId)))
        {
            var split = await _splitRepo.GetByIdAsync(share.SplitExpenseId);
            if (split is { IsDeleted: false } && !titles.Contains(split.Title))
                titles.Add(split.Title);
        }

        var name = contact?.Name?.Trim();
        var greeting = string.IsNullOrWhiteSpace(name) ? "Hi 👋" : $"Hi {name} 👋";
        var money = SplitCalculator.Money(amount);

        var text = $"{greeting}\n\n" +
                   "This is a gentle reminder about a pending payment.\n\n" +
                   $"*Amount:* ₹{money}";

        if (titles.Count > 0)
            text += $"\n*For:* {string.Join(", ", titles.Take(3))}";

        if (!string.IsNullOrWhiteSpace(myUpiId))
        {
            var upi = myUpiId.Trim();
            var note = titles.Count == 0 ? "MoneySpend split" : string.Join(", ", titles.Take(3));
            var fullLink = SplitCalculator.BuildPayLink(payPageUrl, upi, payeeName, amount, note);

            if (fullLink is not null)
            {
                // The full link never goes into the message: only a short link is added.
                var link = await ShortenAsync(fullLink);
                if (link is not null)
                    text += $"\n\nPay securely via UPI 👇\n{link}";
            }
        }

        text += "\n\nThank you 🙏";
        return text;
    }

    // ─────────────────────────────────────────────
    //  Receipt + UPI
    // ─────────────────────────────────────────────
    public async Task<string> BuildReceiptAsync(int splitId)
    {
        var split = await _splitRepo.GetByIdAsync(splitId);
        if (split is null || split.IsDeleted) return string.Empty;

        var shares = (await _shareRepo.FindAsync(s => s.SplitExpenseId == splitId)).ToList();
        var contacts = (await _contactRepo.FindAsync(c => c.Id > 0)).ToDictionary(c => c.Id, c => c.Name);

        string NameOf(int id) => contacts.TryGetValue(id, out var n) ? n : "Unknown";

        // Shared with other people, so "You" becomes "Me".
        var payer = split.PaidByContactId is int pid ? NameOf(pid) : "Me";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🧾 {split.Title}");
        sb.AppendLine($"{split.SplitDate:dd MMM yyyy} • {payer} paid ₹{SplitCalculator.Money(split.TotalAmount)}");

        if (split.GroupId is int gid)
        {
            var group = await _groupRepo.GetByIdAsync(gid);
            if (group is not null) sb.AppendLine($"Group: {group.Name}");
        }

        sb.AppendLine();
        if (split.MyShareAmount > 0)
            sb.AppendLine($"Me: ₹{SplitCalculator.Money(split.MyShareAmount)}");
        foreach (var s in shares)
            sb.AppendLine($"{NameOf(s.ContactId)}: ₹{SplitCalculator.Money(s.ShareAmount)}");

        sb.AppendLine();
        sb.Append("Split with MoneySpend");
        return sb.ToString();
    }

    public async Task<string?> GetFriendUpiAsync(int contactId)
    {
        var contact = await _contactRepo.GetByIdAsync(contactId);
        return string.IsNullOrWhiteSpace(contact?.UpiId) ? null : contact!.UpiId;
    }

    public async Task<SplitResult> SetFriendUpiAsync(int contactId, string upiId)
    {
        upiId = upiId?.Trim() ?? string.Empty;

        if (!SplitCalculator.IsValidUpi(upiId))
            return Fail("Enter a valid UPI ID like name@upi.");

        var contact = await _contactRepo.GetByIdAsync(contactId);
        if (contact is null) return Fail("Friend not found.");

        contact.UpiId = upiId;
        contact.UpdatedAt = DateTime.UtcNow;
        await _contactRepo.UpdateAsync(contact);

        DataChangeNotifier.Publish<Contact>();
        return new SplitResult(true);
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    private static SplitResult Fail(string message) => new(false, message);

    /// <summary>BorrowLend ids that belong to a group's (non-deleted) bills.</summary>
    private async Task<HashSet<int>> GroupBorrowLendIdsAsync(int groupId)
    {
        var splits = (await _splitRepo.FindAsync(s => !s.IsDeleted))
            .Where(s => s.GroupId == groupId)
            .ToList();

        var splitIds = splits.Select(s => s.Id).ToHashSet();
        var ids = new HashSet<int>();

        foreach (var s in splits)
            if (s.MyBorrowLendId is int myBl && myBl > 0) ids.Add(myBl);

        var shares = await _shareRepo.FindAsync(s => s.SplitExpenseId > 0);
        foreach (var sh in shares.Where(s => splitIds.Contains(s.SplitExpenseId) && s.BorrowLendId > 0))
            ids.Add(sh.BorrowLendId);

        return ids;
    }

    /// <summary>BorrowLendService names its auto-created transaction "Lend - initial"; give it a useful label.</summary>
    private async Task RelabelLendTransactionsAsync(int borrowLendId, string description)
    {
        int? refId = borrowLendId;
        var txns = await _txRepo.FindAsync(t =>
            !t.IsDeleted && t.SourceType == "BorrowLend" && t.SourceReferenceId == refId);

        foreach (var t in txns)
        {
            t.Description = description;
            t.UpdatedAt = DateTime.UtcNow;
            t.SyncStatus = SyncStatus.Pending;
            await _txRepo.UpdateAsync(t);
        }
    }

    /// <summary>
    /// If this Borrow record came from a split where a friend paid, turn the payment's
    /// transaction into a regular Expense (split's category, SourceType "Split") so budgets
    /// and reports count it. Amount/account/type are untouched, so balances don't change.
    /// </summary>
    private async Task CountAsExpenseAsync(int borrowLendId, int? transactionId, string? friendName)
    {
        if (transactionId is not int txnId) return;

        int? refId = borrowLendId;
        var split = (await _splitRepo.FindAsync(s => !s.IsDeleted && s.MyBorrowLendId == refId))
            .FirstOrDefault();
        if (split is null) return;

        var txn = await _txRepo.GetByIdAsync(txnId);
        if (txn is null) return;

        txn.SourceType = "Split";
        txn.SourceReferenceId = split.Id;
        txn.CategoryId = split.CategoryId;
        txn.Description = $"{split.Title} - paid to {friendName}";
        txn.UpdatedAt = DateTime.UtcNow;
        txn.SyncStatus = SyncStatus.Pending;
        await _txRepo.UpdateAsync(txn);
    }

    /// <summary>
    /// BorrowLendService.SoftDeleteAsync does NOT reverse linked transactions / account
    /// balance, so do that here: delete every linked Transaction (initial lend + any
    /// settlements), then soft-delete the record itself.
    /// </summary>
    private async Task ReverseBorrowLendAsync(int borrowLendId)
    {
        int? refId = borrowLendId;
        var txns = await _txRepo.FindAsync(t =>
            !t.IsDeleted && t.SourceType == "BorrowLend" && t.SourceReferenceId == refId);

        foreach (var t in txns)
            await _transactionService.DeleteTransactionAsync(t.Id);

        await _borrowLend.SoftDeleteAsync(borrowLendId);
    }

    /// <summary>
    /// Deletes every live transaction tagged to this split (your share + payments re-tagged as
    /// expenses). Only live rows are touched: TransactionService.DeleteTransactionAsync would
    /// reverse the balance again for an already-deleted row.
    /// </summary>
    private async Task DeleteSplitTransactionsAsync(int splitId)
    {
        int? refId = splitId;
        var txns = await _txRepo.FindAsync(t =>
            !t.IsDeleted && t.SourceType == "Split" && t.SourceReferenceId == refId);

        foreach (var t in txns)
            await _transactionService.DeleteTransactionAsync(t.Id);
    }

    private async Task RollbackAsync(SplitExpense split, List<int> borrowLendIds)
    {
        foreach (var id in borrowLendIds)
        {
            try { await ReverseBorrowLendAsync(id); }
            catch (Exception ex) { CrashLogger.Log(ex, "SplitService.Rollback.BorrowLend"); }
        }

        try { await DeleteSplitTransactionsAsync(split.Id); }
        catch (Exception ex) { CrashLogger.Log(ex, "SplitService.Rollback.Transactions"); }

        try
        {
            split.IsDeleted = true;
            split.UpdatedAt = DateTime.UtcNow;
            await _splitRepo.UpdateAsync(split);
        }
        catch (Exception ex) { CrashLogger.Log(ex, "SplitService.Rollback.Split"); }
    }
}
