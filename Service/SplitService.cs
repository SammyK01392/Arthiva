using MoneySpend.Models;
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public class SplitService : ISplitService
{
    private readonly IGenericRepository<SplitExpense> _splitRepo;
    private readonly IGenericRepository<SplitShare> _shareRepo;
    private readonly IGenericRepository<Transaction> _txRepo;
    private readonly IGenericRepository<Contact> _contactRepo;
    private readonly IBorrowLendService _borrowLend;
    private readonly ITransactionService _transactionService;

    public SplitService(
        IGenericRepository<SplitExpense> splitRepo,
        IGenericRepository<SplitShare> shareRepo,
        IGenericRepository<Transaction> txRepo,
        IGenericRepository<Contact> contactRepo,
        IBorrowLendService borrowLend,
        ITransactionService transactionService)
    {
        _splitRepo = splitRepo;
        _shareRepo = shareRepo;
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

        if (title.Length == 0) return Fail("Enter what this expense was for.");
        if (r.TotalAmount <= 0) return Fail("Enter a valid total amount.");
        if (r.AccountId <= 0) return Fail("Select the account you paid from.");
        if (r.MyShare < 0) return Fail("Your share can't be negative.");
        if (r.MyShare > 0 && r.CategoryId <= 0) return Fail("Select a category for your share.");
        if (r.Shares.Count == 0 || r.Shares.Any(s => s.Amount <= 0))
            return Fail("Pick at least one friend with a share above zero.");
        if (r.Shares.GroupBy(s => s.ContactId).Any(g => g.Count() > 1))
            return Fail("A friend is selected twice.");

        var sum = r.MyShare + r.Shares.Sum(s => s.Amount);
        if (sum != r.TotalAmount)
            return Fail($"Shares add up to ₹{sum:N2} but the total is ₹{r.TotalAmount:N2}.");

        var now = DateTime.UtcNow;
        var split = new SplitExpense
        {
            Title = title,
            TotalAmount = r.TotalAmount,
            MyShareAmount = r.MyShare,
            SplitMethod = r.Method,
            SplitDate = r.Date,
            AccountId = r.AccountId,
            CategoryId = r.CategoryId,
            Notes = r.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _splitRepo.AddAsync(split);

        var createdBorrowLendIds = new List<int>();
        int? myTxnId = null;

        try
        {
            // 1) Your own share → normal Expense (counts in budgets/reports).
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
                myTxnId = myTxn.Id;
            }

            // 2) Each friend → Lend record (also debits the account via BorrowLendService).
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

            split.MyTransactionId = myTxnId;
            split.UpdatedAt = DateTime.UtcNow;
            await _splitRepo.UpdateAsync(split);
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "SplitService.CreateAsync");
            await RollbackAsync(split, myTxnId, createdBorrowLendIds);
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
            foreach (var share in shares)
                await ReverseBorrowLendAsync(share.BorrowLendId);

            await DeleteTransactionOnceAsync(split.MyTransactionId);

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
    public async Task<List<SplitListItem>> GetSplitsAsync()
    {
        var splits = (await _splitRepo.FindAsync(s => !s.IsDeleted))
            .OrderByDescending(s => s.SplitDate)
            .ToList();

        if (splits.Count == 0) return new List<SplitListItem>();

        var shares = (await _shareRepo.FindAsync(s => s.SplitExpenseId > 0)).ToList();
        var openAndClosed = await _borrowLend.GetAllAsync(null, includeClosed: true);
        var borrowLends = openAndClosed.ToDictionary(b => b.Id);
        var contacts = (await _contactRepo.FindAsync(c => c.Id > 0)).ToDictionary(c => c.Id, c => c.Name);

        var items = new List<SplitListItem>();
        foreach (var split in splits)
        {
            var mine = shares.Where(s => s.SplitExpenseId == split.Id).ToList();

            var pending = mine.Sum(s =>
                borrowLends.TryGetValue(s.BorrowLendId, out var bl) ? bl.PendingAmount : 0m);

            var names = mine
                .Select(s => contacts.TryGetValue(s.ContactId, out var n) ? n : "Unknown")
                .ToList();

            var friendsText = names.Count <= 2
                ? string.Join(", ", names)
                : $"{names[0]}, {names[1]} +{names.Count - 2}";

            items.Add(new SplitListItem
            {
                Id = split.Id,
                Title = split.Title,
                Date = split.SplitDate,
                Total = split.TotalAmount,
                MyShare = split.MyShareAmount,
                FriendsText = friendsText,
                Pending = pending
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
                    LendPending = g.Where(b => b.Type == "Lend").Sum(b => b.PendingAmount),
                    BorrowPending = g.Where(b => b.Type == "Borrow").Sum(b => b.PendingAmount)
                };
            })
            .OrderByDescending(f => f.AbsNet)
            .ToList();
    }

    // ─────────────────────────────────────────────
    //  Settle
    // ─────────────────────────────────────────────
    public async Task<SplitResult> SettleAsync(int contactId, decimal amount, int accountId)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);

        if (amount <= 0) return Fail("Enter a valid amount.");
        if (accountId <= 0) return Fail("Select an account.");

        var open = (await _borrowLend.GetByContactAsync(contactId))
            .Where(b => b.Type == "Lend" && !b.IsClosed && b.PendingAmount > 0)
            .OrderBy(b => b.GivenDate)
            .ToList();

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
    //  Reminder text
    // ─────────────────────────────────────────────
    public async Task<string> BuildReminderTextAsync(int contactId)
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

        var greeting = string.IsNullOrWhiteSpace(contact?.Name) ? "Hi" : $"Hi {contact!.Name}";
        var what = titles.Count == 0 ? string.Empty : $" ({string.Join(", ", titles.Take(3))})";

        return $"{greeting}, ₹{SplitCalculator.Money(amount)} pending hai{what}. Jab ho sake bhej dena 🙏";
    }

    // ─────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────
    private static SplitResult Fail(string message) => new(false, message);

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
    /// BorrowLendService.SoftDeleteAsync does NOT reverse linked transactions / account
    /// balance, so do that here: delete every linked Transaction (initial lend + any
    /// settlements received), then soft-delete the record itself.
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
    /// TransactionService.DeleteTransactionAsync reverses the balance every time it runs,
    /// even for an already-deleted row — so never call it twice for the same transaction.
    /// </summary>
    private async Task DeleteTransactionOnceAsync(int? transactionId)
    {
        if (!transactionId.HasValue) return;

        var txn = await _transactionService.GetByIdAsync(transactionId.Value);
        if (txn is { IsDeleted: false })
            await _transactionService.DeleteTransactionAsync(txn.Id);
    }

    private async Task RollbackAsync(SplitExpense split, int? myTxnId, List<int> borrowLendIds)
    {
        foreach (var id in borrowLendIds)
        {
            try { await ReverseBorrowLendAsync(id); }
            catch (Exception ex) { CrashLogger.Log(ex, "SplitService.Rollback.BorrowLend"); }
        }

        try { await DeleteTransactionOnceAsync(myTxnId); }
        catch (Exception ex) { CrashLogger.Log(ex, "SplitService.Rollback.MyTxn"); }

        try
        {
            split.IsDeleted = true;
            split.UpdatedAt = DateTime.UtcNow;
            await _splitRepo.UpdateAsync(split);
        }
        catch (Exception ex) { CrashLogger.Log(ex, "SplitService.Rollback.Split"); }
    }
}
