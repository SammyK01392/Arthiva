using MoneySpend.Models;

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

public interface IDetectedTransactionService
{
    /// <summary>Parses an SMS and stores it. Returns true if a NEW payment/receipt was saved.</summary>
    Task<bool> IngestSmsAsync(string? sender, string? body, DateTime receivedAt);

    Task<List<DetectedTransaction>> GetPendingAsync();

    Task<int> GetPendingCountAsync();

    /// <summary>Creates a real Income/Expense transaction and remembers the category for this payee.</summary>
    Task AddAsTransactionAsync(DetectedTransaction item, int categoryId, int? accountId = null);

    Task DismissAsync(DetectedTransaction item);

    /// <summary>Renames the payee and remembers the name for future SMS.</summary>
    Task RenamePayeeAsync(DetectedTransaction item, string newName);
}

public class DetectedTransactionService : IDetectedTransactionService
{
    private static readonly string[] DefaultCategoryNames =
        { "others", "other", "miscellaneous", "misc", "uncategorized", "general" };

    private readonly IGenericRepository<DetectedTransaction> _repo;
    private readonly IGenericRepository<PayeeRule> _rules;
    private readonly IGenericRepository<Transaction> _txRepo;
    private readonly IGenericRepository<Category> _categoryRepo;
    private readonly ITransactionService _transactions;
    private readonly IAccountService _accounts;

    // Two SMS arriving together must not both pass the duplicate check.
    private readonly SemaphoreSlim _lock = new(1, 1);

    public DetectedTransactionService(
        IGenericRepository<DetectedTransaction> repo,
        IGenericRepository<PayeeRule> rules,
        IGenericRepository<Transaction> txRepo,
        IGenericRepository<Category> categoryRepo,
        ITransactionService transactions,
        IAccountService accounts)
    {
        _repo = repo;
        _rules = rules;
        _txRepo = txRepo;
        _categoryRepo = categoryRepo;
        _transactions = transactions;
        _accounts = accounts;
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
                    var categoryId = item.SuggestedCategoryId ?? await FindDefaultCategoryIdAsync();

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
    //  HELPERS
    // ─────────────────────────────────────────────────────────
    private async Task CreateTransactionAsync(DetectedTransaction item, int categoryId, int? accountId)
    {
        // account: given -> default -> first available
        accountId ??= (await _accounts.GetDefaultAccountAsync())?.Id;
        accountId ??= (await _accounts.GetAllAsync()).FirstOrDefault()?.Id;

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
                ? "Auto-detected from SMS"
                : (item.IsCredit ? $"Received from {payee}" : $"Paid to {payee}")
        };

        // normal service: balance update (+ for Income, - for Expense) and cloud-sync bookkeeping
        await _transactions.AddTransactionAsync(transaction);

        item.Status = DetectedStatus.Added;
        item.TransactionId = transaction.Id > 0 ? transaction.Id : null;
        await _repo.UpdateAsync(item);
    }

    /// <summary>"Others"-like category used by Auto-add when nothing is remembered; else the first category.</summary>
    private async Task<int?> FindDefaultCategoryIdAsync()
    {
        var categories = await _categoryRepo.FindAsync(c => true);
        if (categories.Count == 0) return null;

        var match = categories.FirstOrDefault(c =>
            DefaultCategoryNames.Contains((c.Name ?? string.Empty).Trim().ToLowerInvariant()));

        return (match ?? categories[0]).Id;
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
