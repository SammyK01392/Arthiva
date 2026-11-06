using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MoneySpend.Models;
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

/// <summary>Auto-add switch. Stored in Preferences so the SMS receiver can read it while the UI is closed.</summary>
public static class SmsAutoDetectSettings
{
    private const string AutoAddKey = "sms_auto_add";

    public static bool AutoAdd
    {
        get => Preferences.Default.Get(AutoAddKey, false);
        set => Preferences.Default.Set(AutoAddKey, value);
    }
}

/// <summary>Settings for statement (PDF / screenshot) import.</summary>
public static class StatementImportSettings
{
    private const string AutoAddKnownKey = "stmt_auto_add_known";
    private const string ImportAccountKey = "stmt_import_account_id";

    /// <summary>ON (default): a payee you already added once is added again directly, no review.</summary>
    public static bool AutoAddKnown
    {
        get => Preferences.Default.Get(AutoAddKnownKey, true);
        set => Preferences.Default.Set(AutoAddKnownKey, value);
    }

    /// <summary>Account that statement payments go to. 0 = not chosen (a Bank account is used).</summary>
    public static int ImportAccountId
    {
        get => Preferences.Default.Get(ImportAccountKey, 0);
        set => Preferences.Default.Set(ImportAccountKey, value);
    }
}

public interface IDetectedTransactionService
{
    /// <summary>
    /// Saves a row that is ALREADY decided (known payee) straight as an Added entry + real transaction.
    /// </summary>
    Task AddKnownAsync(DetectedTransaction item, int categoryId);

    /// <summary>Parses an SMS and stores it. Returns true if a NEW payment/receipt was saved.</summary>
    Task<bool> IngestSmsAsync(string? sender, string? body, DateTime receivedAt);

    Task<List<DetectedTransaction>> GetPendingAsync();

    Task<int> GetPendingCountAsync();

    /// <summary>Adds every pending row marked High confidence (Income / Expense / EMI). Returns how many were added.</summary>
    Task<int> AddAllConfidentAsync();

    /// <summary>Creates a real Income/Expense transaction and remembers the category for this payee.</summary>
    Task AddAsTransactionAsync(DetectedTransaction item, int categoryId, int? accountId = null);

    Task DismissAsync(DetectedTransaction item);

    /// <summary>Plain-words preview of what "Add" will do for a person row (contact + Borrow/Lend record).</summary>
    Task<string> DescribePersonPaymentAsync(DetectedTransaction item);

    /// <summary>Creates the Contact if needed, then the Borrow/Lend record or the return payment. Returns the summary.</summary>
    Task<string> AddPersonPaymentAsync(DetectedTransaction item);

    /// <summary>All pending person rows of the same payee, oldest first (so balances come out right).</summary>
    Task<List<DetectedTransaction>> GetSamePersonRowsAsync(DetectedTransaction item);

    /// <summary>Renames the payee and remembers the name for future SMS.</summary>
    Task RenamePayeeAsync(DetectedTransaction item, string newName);
}

public class DetectedTransactionService : IDetectedTransactionService
{
    private readonly IGenericRepository<DetectedTransaction> _repo;
    private readonly IGenericRepository<PayeeRule> _rules;
    private readonly IGenericRepository<Transaction> _txRepo;
    private readonly IGenericRepository<Category> _categoryRepo;
    private readonly ITransactionService _transactions;
    private readonly IAccountService _accounts;
    private readonly IContactService _contacts;
    private readonly IBorrowLendService _borrowLend;
    private readonly IAccountLinkService _accountLinks;

    // Two SMS arriving together must not both pass the duplicate check.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DetectedTransactionService(
        IGenericRepository<DetectedTransaction> repo,
        IGenericRepository<PayeeRule> rules,
        IGenericRepository<Transaction> txRepo,
        IGenericRepository<Category> categoryRepo,
        ITransactionService transactions,
        IAccountService accounts,
        IContactService contacts,
        IBorrowLendService borrowLend,
        IAccountLinkService accountLinks)
    {
        _repo = repo;
        _rules = rules;
        _txRepo = txRepo;
        _categoryRepo = categoryRepo;
        _transactions = transactions;
        _accounts = accounts;
        _contacts = contacts;
        _borrowLend = borrowLend;
        _accountLinks = accountLinks;
    }

    // ─────────────────────────────────────────────────────────
    //  INGEST (called by the SMS receiver)
    // ─────────────────────────────────────────────────────────
    public async Task<bool> IngestSmsAsync(string? sender, string? body, DateTime receivedAt)
    {
        var parsed = SmsParser.Parse(sender, body);
        if (parsed is null) return false;

        var text = body!;

        await _lock.WaitAsync();
        try
        {
            // ---- duplicate check ----
            if (!string.IsNullOrEmpty(parsed.ReferenceId))
            {
                var refId = parsed.ReferenceId;

                if ((await _repo.FindAsync(d => d.ReferenceId == refId)).Count > 0)
                    return false;

                // user already entered this one manually
                if ((await _txRepo.FindAsync(t => !t.IsDeleted && t.ReferenceId == refId)).Count > 0)
                    return false;
            }
            else
            {
                var sameBody = await _repo.FindAsync(d => d.RawBody == text);
                if (sameBody.Any(d => Math.Abs((d.TransactionDate - receivedAt).TotalMinutes) < 5))
                    return false;
            }

            // ---- what did the user decide for this payee last time? ----
            var key = MakeKey(parsed.Payee);
            PayeeRule? rule = null;

            if (key is not null)
            {
                rule = (await _rules.FindAsync(r => r.MatchKey == key && r.IsCredit == parsed.IsCredit))
                    .FirstOrDefault();
            }

            var item = new DetectedTransaction
            {
                Amount = parsed.Amount,
                IsCredit = parsed.IsCredit,
                Payee = string.IsNullOrWhiteSpace(rule?.DisplayName) ? parsed.Payee : rule!.DisplayName,
                MatchKey = key,
                SuggestedCategoryId = rule?.CategoryId,
                ReferenceId = parsed.ReferenceId,
                AccountHint = parsed.AccountHint,
                Method = parsed.Method,
                Sender = sender,
                RawBody = text,
                TransactionDate = receivedAt,
                Status = DetectedStatus.Pending
            };

            await _repo.AddAsync(item);

            // make sure we hold the saved row (with its Id) before updating it later
            if (item.Id == 0)
            {
                item = (await _repo.FindAsync(d => d.RawBody == text))
                    .OrderByDescending(d => d.Id)
                    .First();
            }

            // ---- AUTO MODE: create the real entry right now ----
            if (SmsAutoDetectSettings.AutoAdd)
            {
                try
                {
                    var categoryId = item.SuggestedCategoryId ?? await FindDefaultCategoryIdAsync(item.IsCredit);

                    if (categoryId is not null)
                        await CreateTransactionAsync(item, categoryId.Value, null);
                    // no category at all -> stays in the inbox for manual add
                }
                catch (Exception ex)
                {
                    // anything goes wrong -> leave it Pending in the inbox
                    System.Diagnostics.Debug.WriteLine($"[SmsAutoAdd] {ex}");
                }
            }

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ─────────────────────────────────────────────────────────
    //  INBOX
    // ─────────────────────────────────────────────────────────
    public async Task<List<DetectedTransaction>> GetPendingAsync()
    {
        var list = await _repo.FindAsync(d => d.Status == DetectedStatus.Pending);
        return list.OrderByDescending(d => d.TransactionDate).ToList();
    }

    public async Task<int> GetPendingCountAsync()
        => (await _repo.FindAsync(d => d.Status == DetectedStatus.Pending)).Count;

    public async Task<int> AddAllConfidentAsync()
    {
        var added = 0;

        foreach (var item in (await GetPendingAsync()).Where(d => d.IsAutoAddable))
        {
            try
            {
                var categoryId = item.SuggestedCategoryId ?? await FindDefaultCategoryIdAsync(item.IsCredit);
                if (categoryId is null) continue;

                await CreateTransactionAsync(item, categoryId.Value, null);
                added++;

                // remember it (only when the category was a real match, not the "Others" fallback)
                if (item.MatchKey is not null && item.SuggestedCategoryId is not null)
                    await UpsertRuleAsync(item.MatchKey, item.IsCredit, displayName: null, categoryId.Value);
            }
            catch (Exception ex)
            {
                // one bad row must not stop the rest
                System.Diagnostics.Debug.WriteLine($"[AddAllConfident] {ex}");
            }
        }

        return added;
    }

    public async Task AddAsTransactionAsync(DetectedTransaction item, int categoryId, int? accountId = null)
    {
        await CreateTransactionAsync(item, categoryId, accountId);

        // learn: next SMS from this payee gets this category
        if (item.MatchKey is not null)
            await UpsertRuleAsync(item.MatchKey, item.IsCredit, displayName: null, categoryId);
    }

    public async Task DismissAsync(DetectedTransaction item)
    {
        item.Status = DetectedStatus.Dismissed;
        await _repo.UpdateAsync(item);
    }

    public async Task RenamePayeeAsync(DetectedTransaction item, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return;

        item.Payee = newName;
        await _repo.UpdateAsync(item);

        if (item.MatchKey is not null)
            await UpsertRuleAsync(item.MatchKey, item.IsCredit, newName, categoryId: null);
    }

    // ─────────────────────────────────────────────────────────
    //  PEOPLE: Lend / Borrow / returns  (creates the Contact if it does not exist)
    // ─────────────────────────────────────────────────────────
    private sealed class PersonPlan
    {
        public string Name = string.Empty;
        public string? Mobile;
        public Contact? Contact;
        public List<BorrowLend> Open = new();
        public List<PlanStep> Steps = new();
    }

    public async Task<List<DetectedTransaction>> GetSamePersonRowsAsync(DetectedTransaction item)
    {
        if (string.IsNullOrEmpty(item.MatchKey))
            return new List<DetectedTransaction> { item };

        var key = item.MatchKey;
        var rows = await _repo.FindAsync(d => d.Status == DetectedStatus.Pending && d.MatchKey == key);

        return rows.Where(d => d.IsPersonRow).OrderBy(d => d.TransactionDate).ToList();
    }

    public async Task<string> DescribePersonPaymentAsync(DetectedTransaction item)
        => DescribePlan(item, await BuildPlanAsync(item));

    public async Task<string> AddPersonPaymentAsync(DetectedTransaction item)
    {
        var plan = await BuildPlanAsync(item);
        var summary = DescribePlan(item, plan);

        var accountId = await ResolveAccountIdAsync(item);

        if (accountId is null)
            throw new InvalidOperationException("Please create an account first.");

        // 1. the contact
        var contact = plan.Contact;

        if (contact is null)
        {
            contact = new Contact { Name = plan.Name, Mobile = plan.Mobile };
            await _contacts.CreateAsync(contact);

            // make sure we hold the saved row (with its Id)
            if (contact.Id == 0)
            {
                contact = (await _contacts.GetAllAsync())
                    .OrderByDescending(c => c.Id)
                    .FirstOrDefault(c => c.Name == plan.Name)
                    ?? throw new InvalidOperationException("Could not create the contact.");
            }
        }

        var note = $"From statement{(string.IsNullOrWhiteSpace(item.ReferenceId) ? string.Empty : $" • UTR {item.ReferenceId}")}";

        // 2. the money
        foreach (var step in plan.Steps)
        {
            if (step.RecordId is int recordId)
            {
                var txn = new BorrowLendTransaction
                {
                    BorrowLendId = recordId,
                    Type = step.Kind,
                    Amount = step.Amount,
                    TransactionDate = item.TransactionDate,
                    PaymentMethod = item.Method ?? "UPI",
                    Notes = note
                };

                // also creates the matching Income/Expense and moves the account balance
                var result = await _borrowLend.RecordTransactionAsync(txn, accountId);

                if (!result.Success)
                    throw new InvalidOperationException("Could not record this payment against the existing record.");
            }
            else
            {
                await _borrowLend.CreateAsync(new BorrowLend
                {
                    ContactId = contact.Id,
                    Type = step.Kind == "NewLend" ? "Lend" : "Borrow",
                    TotalAmount = step.Amount,
                    GivenDate = item.TransactionDate,
                    Status = "Pending",
                    Notes = note
                }, accountId);
            }
        }

        item.Status = DetectedStatus.Added;
        await _repo.UpdateAsync(item);

        return summary;
    }

    private async Task<PersonPlan> BuildPlanAsync(DetectedTransaction item)
    {
        if (string.IsNullOrWhiteSpace(item.Payee))
            throw new InvalidOperationException("This entry has no name. Tap the name and rename it first.");

        var plan = new PersonPlan
        {
            Name = CleanPersonName(item.Payee),
            Mobile = Regex.Match(item.Payee, @"^(\d{10})@").Success
                ? Regex.Match(item.Payee, @"^(\d{10})@").Groups[1].Value
                : null
        };

        plan.Contact = FindContact(await _contacts.GetAllAsync(), plan.Name, plan.Mobile);

        if (plan.Contact is not null)
        {
            // records shared with another MoneySpend user need both people to agree, so we leave them alone
            plan.Open = (await _borrowLend.GetByContactAsync(plan.Contact.Id))
                .Where(r => r.PendingAmount > 0 && string.IsNullOrEmpty(r.SharedRequestId))
                .ToList();
        }

        plan.Steps = PersonPaymentPlanner.Plan(
            item.IsCredit,
            item.Amount,
            plan.Open.Select(r => new OpenRecord(r.Id, r.Type, r.PendingAmount, r.GivenDate)));

        return plan;
    }

    private static string DescribePlan(DetectedTransaction item, PersonPlan plan)
    {
        var sb = new StringBuilder();

        sb.AppendLine(item.IsCredit
            ? $"+₹{item.Amount:N0} from {plan.Name}"
            : $"-₹{item.Amount:N0} to {plan.Name}");

        sb.AppendLine(plan.Contact is null
            ? $"• New contact \"{plan.Name}\" will be created"
            : $"• Existing contact: {plan.Contact.Name}");

        foreach (var step in plan.Steps)
        {
            var record = plan.Open.FirstOrDefault(r => r.Id == step.RecordId);

            sb.AppendLine(step.Kind switch
            {
                "Receive" => $"• ₹{step.Amount:N0} received against their Lend (pending ₹{record?.PendingAmount:N0} → ₹{record?.PendingAmount - step.Amount:N0})",
                "Return" or "PartialReturn" => $"• ₹{step.Amount:N0} paid back against what you owe (pending ₹{record?.PendingAmount:N0} → ₹{record?.PendingAmount - step.Amount:N0})",
                "NewLend" => $"• New Lend record of ₹{step.Amount:N0} (you gave money)",
                _ => $"• New Borrow record of ₹{step.Amount:N0} (you received money)"
            });
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>"RAHUL SHARMA" -> "Rahul Sharma", "Mr Kaneti Anandh Kumar" -> "Kaneti Anandh Kumar".</summary>
    private static string CleanPersonName(string payee)
    {
        var name = Regex.Replace(payee, @"^(?:mr|mrs|ms|miss|dr|shri|smt|sri|shree)\.?\s+", string.Empty, RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+", " ").Trim();

        var allUpper = name == name.ToUpperInvariant();
        var allLower = name == name.ToLowerInvariant();

        return allUpper || allLower
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant())
            : name;
    }

    private static Contact? FindContact(IEnumerable<Contact> contacts, string name, string? mobile)
    {
        var target = NameTokens(name);

        foreach (var c in contacts)
        {
            if (!string.IsNullOrEmpty(mobile) && !string.IsNullOrEmpty(c.Mobile) && c.Mobile.EndsWith(mobile))
                return c;

            var tokens = NameTokens(c.Name);

            // every word of the saved name is in the statement name ("Rahul" in "Rahul Sharma")
            if (tokens.Count > 0 && tokens.All(target.Contains))
                return c;
        }

        return null;
    }

    private static HashSet<string> NameTokens(string? s)
        => (s ?? string.Empty)
            .ToLowerInvariant()
            .Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 3 && x.All(char.IsLetter))
            .ToHashSet();

    // ─────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────
    private async Task CreateTransactionAsync(DetectedTransaction item, int categoryId, int? accountId)
    {
        var transaction = await SaveTransactionAsync(item, categoryId, accountId);

        item.Status = DetectedStatus.Added;
        item.TransactionId = transaction.Id > 0 ? transaction.Id : null;
        await _repo.UpdateAsync(item);
    }

    public async Task AddKnownAsync(DetectedTransaction item, int categoryId)
    {
        var transaction = await SaveTransactionAsync(item, categoryId, null);

        // inserted once, already in its final state (Added)
        item.Status = DetectedStatus.Added;
        item.TransactionId = transaction.Id > 0 ? transaction.Id : null;
        await _repo.AddAsync(item);
    }

    private async Task<Transaction> SaveTransactionAsync(DetectedTransaction item, int categoryId, int? accountId)
    {
        accountId ??= await ResolveAccountIdAsync(item);

        if (accountId is null)
            throw new InvalidOperationException("Please create an account first.");

        var payee = item.Payee;

        var transaction = new Transaction
        {
            AccountId = accountId.Value,
            CategoryId = categoryId,
            Amount = item.Amount,
            TransactionType = item.IsCredit ? "Income" : "Expense",
            TransactionDate = item.TransactionDate,
            Payee = payee,
            ReferenceId = item.ReferenceId,
            PaymentMethod = item.Method ?? "UPI",
            Description = string.IsNullOrWhiteSpace(payee)
                ? "Auto-detected"
                : (item.IsCredit ? $"Received from {payee}" : $"Paid to {payee}")
        };

        // normal service: balance update (+ for Income, - for Expense) and cloud-sync bookkeeping
        await _transactions.AddTransactionAsync(transaction);

        return transaction;
    }

    /// <summary>
    /// An "Others"-type category of the RIGHT type (Expense for money out, Income for money in).
    /// null = you have none: the row then stays in the inbox instead of landing in a wrong category.
    /// </summary>
    private async Task<int?> FindDefaultCategoryIdAsync(bool isCredit)
    {
        var categories = await _categoryRepo.FindAsync(c => true);
        return CategoryResolver.DefaultFor(isCredit, ToInfo(categories));
    }

    private static List<(int Id, string Name, string? Type)> ToInfo(IEnumerable<Category> categories)
        => categories
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => (c.Id, c.Name!, (string?)c.Type))
            .ToList();

    /// <summary>
    /// Which account the money moved in: the one chosen for imports -> a Bank account (UPI is bank money,
    /// not cash) -> the default account.
    /// </summary>
    private async Task<int?> ResolveAccountIdAsync(DetectedTransaction item)
    {
        // the account number in the SMS / statement ("IDBI Bank 9847") -> its own account,
        // created automatically the first time it is seen
        if (!string.IsNullOrWhiteSpace(item.AccountHint))
        {
            try
            {
                var linked = await _accountLinks.ResolveAsync(item.AccountHint, item.AccountName);
                if (linked is int linkedId) return linkedId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AccountLink] {ex}");
            }
        }

        // no account number: the account chosen for imports, else a Bank account, else the default
        var accounts = await _accounts.GetAllAsync();

        var chosen = StatementImportSettings.ImportAccountId;
        if (chosen > 0 && accounts.Any(a => a.Id == chosen)) return chosen;

        var wantedType = string.Equals(item.Method, "Wallet", StringComparison.OrdinalIgnoreCase) ? "Wallet" : "Bank";
        var match = accounts.FirstOrDefault(a => string.Equals(a.Type, wantedType, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Id;

        return (await _accounts.GetDefaultAccountAsync())?.Id ?? accounts.FirstOrDefault()?.Id;
    }

    private async Task UpsertRuleAsync(string key, bool isCredit, string? displayName, int? categoryId)
    {
        var existing = (await _rules.FindAsync(r => r.MatchKey == key && r.IsCredit == isCredit))
            .FirstOrDefault();

        if (existing is null)
        {
            await _rules.AddAsync(new PayeeRule
            {
                MatchKey = key,
                IsCredit = isCredit,
                DisplayName = displayName,
                CategoryId = categoryId,
                UpdatedAt = DateTime.Now
            });
            return;
        }

        if (displayName is not null) existing.DisplayName = displayName;
        if (categoryId is not null) existing.CategoryId = categoryId;
        existing.UpdatedAt = DateTime.Now;

        await _rules.UpdateAsync(existing);
    }

    private static string? MakeKey(string? payee)
    {
        var key = payee?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(key) ? null : key;
    }
}
