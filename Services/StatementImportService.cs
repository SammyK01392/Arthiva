using MoneySpend.Models;
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public sealed record StatementImportResult(
    bool Success,
    string? Error,
    int Found,
    int Added,
    int Duplicates,
    int Skipped,
    int Unreadable,
    int NeedsReview,
    int AutoAdded = 0,
    IReadOnlyList<string>? NewAccounts = null,
    IReadOnlyList<EmiStatement>? LoanStatements = null);

public interface IStatementImportService
{
    /// <summary>Reads the PDF(s) / screenshot(s), puts every new transaction in the Detected Payments inbox.</summary>
    Task<StatementImportResult> ImportAsync(IReadOnlyList<FileResult> files, Func<Task<string?>> askPassword);
}

public class StatementImportService : IStatementImportService
{
    private static readonly string[] ReviewTypes = { "Borrow", "Lend", "ReturnPaid", "ReturnReceived", "Transfer" };

    private readonly IGenericRepository<DetectedTransaction> _repo;
    private readonly IGenericRepository<PayeeRule> _rules;
    private readonly IGenericRepository<Transaction> _txRepo;
    private readonly IGenericRepository<Category> _categoryRepo;
    private readonly IGenericRepository<Contact> _contactRepo;
    private readonly IBorrowLendService _borrowLend;
    private readonly IDetectedTransactionService _detected;
    private readonly IAccountLinkService _accountLinks;

    public StatementImportService(
        IGenericRepository<DetectedTransaction> repo,
        IGenericRepository<PayeeRule> rules,
        IGenericRepository<Transaction> txRepo,
        IGenericRepository<Category> categoryRepo,
        IGenericRepository<Contact> contactRepo,
        IBorrowLendService borrowLend,
        IDetectedTransactionService detected,
        IAccountLinkService accountLinks)
    {
        _repo = repo;
        _rules = rules;
        _txRepo = txRepo;
        _categoryRepo = categoryRepo;
        _contactRepo = contactRepo;
        _borrowLend = borrowLend;
        _detected = detected;
        _accountLinks = accountLinks;
    }

    public async Task<StatementImportResult> ImportAsync(
        IReadOnlyList<FileResult> files, Func<Task<string?>> askPassword)
    {
        // ---------- 1. read every file ----------
        var parsedRows = new List<(StatementRow Row, string Source)>();
        int skipped = 0, unreadable = 0;
        var errors = new List<string>();
        var loanStatements = new List<EmiStatement>();   // EMI / loan statements of account

        foreach (var file in files)
        {
            string? saved = null;

            try
            {
                saved = await StatementFiles.SaveAsync(file);

                var lines = await StatementTextExtractor.ExtractLinesAsync(saved, askPassword);

                // a loan / EMI statement of account is not a list of UPI payments: it goes to the EMI flow
                if (EmiStatementParser.LooksLikeLoanStatement(lines) && EmiStatementParser.Parse(lines) is { } loan)
                {
                    loanStatements.Add(loan);   // the file is kept for 1 day like the others
                    continue;
                }

                var parsed = StatementParser.Parse(lines, DateTime.Now);

                skipped += parsed.Skipped;
                unreadable += parsed.Unreadable;
                parsedRows.AddRange(parsed.Rows.Select(r => (r, file.FileName)));

                // nothing useful inside -> no reason to keep the file
                if (parsed.Rows.Count == 0)
                    StatementFiles.DeleteNow(saved);
            }
            catch (OperationCanceledException)
            {
                StatementFiles.DeleteNow(saved);
                errors.Add($"{file.FileName}: password not entered");
            }
            catch (Exception ex)
            {
                StatementFiles.DeleteNow(saved);
                errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        // only EMI statements: nothing to put in the payments inbox, the EMI flow takes over
        if (parsedRows.Count == 0 && loanStatements.Count > 0)
            return new StatementImportResult(true, null, 0, 0, 0, 0, 0, 0, 0, null, loanStatements);

        if (parsedRows.Count == 0)
        {
            var message = errors.Count > 0
                ? string.Join("\n", errors)
                : "No transactions found. Is this a PhonePe / Paytm / GPay statement?";

            return new StatementImportResult(false, message, 0, 0, 0, skipped, unreadable, 0);
        }

        // ---------- 2. what do we already have? ----------
        var from = parsedRows.Min(r => r.Row.Date).Date.AddDays(-1);
        var to = parsedRows.Max(r => r.Row.Date).Date.AddDays(2);

        var existingTx = await _txRepo.FindAsync(t =>
            !t.IsDeleted && t.TransactionDate >= from && t.TransactionDate <= to);

        var existingDetected = await _repo.FindAsync(d =>
            d.TransactionDate >= from && d.TransactionDate <= to);

        // UTR -> which directions we already have (true = received, false = sent)
        var knownRefs = new Dictionary<string, HashSet<bool>>(StringComparer.OrdinalIgnoreCase);
        var knownKeys = new HashSet<string>();

        void Remember(string? reference, bool isCredit)
        {
            if (string.IsNullOrWhiteSpace(reference)) return;

            if (!knownRefs.TryGetValue(reference, out var set))
                knownRefs[reference] = set = new HashSet<bool>();

            set.Add(isCredit);
        }

        foreach (var t in existingTx)
        {
            Remember(t.ReferenceId, string.Equals(t.TransactionType, "Income", StringComparison.OrdinalIgnoreCase));
            knownKeys.Add(LooseKey(t.TransactionDate, t.Amount, t.Payee));
        }

        foreach (var d in existingDetected)
        {
            Remember(d.ReferenceId, d.IsCredit);
            knownKeys.Add(LooseKey(d.TransactionDate, d.Amount, d.Payee));
        }

        // same UTR sent in one file and received in another = money moved between YOUR OWN accounts
        var ownTransferRefs = StatementPairing.OwnTransferRefs(parsedRows.Select(p => p.Row));

        // ---------- 3. things the classifier needs ----------
        var categories = await _categoryRepo.FindAsync(c => true);
        var categoryInfo = categories
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => (c.Id, c.Name!, (string?)c.Type))
            .ToList();
        var rules = (await _rules.FindAsync(r => true))
            .GroupBy(r => (r.MatchKey, r.IsCredit))
            .ToDictionary(g => g.Key, g => g.First());

        var contacts = await LoadContactsAsync();

        // ---------- 4. classify + save ----------
        int added = 0, duplicates = 0, review = 0, autoAdded = 0;

        foreach (var (row, source) in parsedRows)
        {
            // same payment already there (UTR match works across SMS, manual and earlier imports)
            var hasRef = !string.IsNullOrWhiteSpace(row.ReferenceId);
            var oppositeKnown = false;

            if (hasRef && knownRefs.TryGetValue(row.ReferenceId!, out var directions))
            {
                if (directions.Contains(row.IsCredit))
                {
                    duplicates++;
                    continue;
                }

                oppositeKnown = true;   // we already have the other side of this UTR
            }

            Remember(row.ReferenceId, row.IsCredit);

            var isOwnTransfer = hasRef && (oppositeKnown || ownTransferRefs.Contains(row.ReferenceId!));

            var cls = StatementClassifier.Classify(row, contacts, isOwnTransfer);
            var payee = row.Counterparty ?? row.CounterpartyUpi;
            var key = MakeKey(payee);

            int? categoryId = null;
            string? categoryName = null;
            var knownPayee = false;   // you already added this payee once

            // what the user decided for this payee earlier beats every guess
            if (!isOwnTransfer && key is not null && rules.TryGetValue((key, row.IsCredit), out var rule))
            {
                if (!string.IsNullOrWhiteSpace(rule.DisplayName))
                    payee = rule.DisplayName;

                if (rule.CategoryId is int rc)
                {
                    knownPayee = true;
                    categoryId = rc;
                    categoryName = categories.FirstOrDefault(c => c.Id == rc)?.Name;
                    cls = new Classification(row.IsCredit ? "Income" : "Expense", "High",
                        "You categorised this payee before", null);
                }
            }

            if (categoryId is null)
            {
                var hint = cls.CategoryHint ?? (cls.Type == "Emi" ? "EMI" : null);

                var found = CategoryResolver.Resolve(hint, row.IsCredit, categoryInfo)
                            ?? (cls.Type == "Emi" ? CategoryResolver.Resolve("Loan", false, categoryInfo) : null);

                if (found is int id)
                {
                    categoryId = id;
                    categoryName = categories.First(c => c.Id == id).Name;
                }
            }

            // no reference number: same day + amount + payee already exists -> maybe a duplicate, let the user decide
            var confidence = cls.Confidence;
            var reason = cls.Reason;

            if (string.IsNullOrWhiteSpace(row.ReferenceId) &&
                !knownKeys.Add(LooseKey(row.Date, row.Amount, payee)))
            {
                confidence = "Low";
                reason = "Possible duplicate of an existing entry. " + reason;
            }

            var detected = new DetectedTransaction
            {
                Amount = row.Amount,
                IsCredit = row.IsCredit,
                Payee = payee,
                MatchKey = key,
                SuggestedCategoryId = categoryId,
                CategoryHint = categoryName,
                ReferenceId = row.ReferenceId,
                AccountHint = row.AccountHint,
                AccountName = row.AccountName,
                Method = "UPI",
                RawBody = row.RawBlock,
                TransactionDate = row.Date,
                Status = DetectedStatus.Pending,
                Source = "Statement",
                SourceName = source,
                SuggestedType = cls.Type,
                Confidence = confidence,
                Reason = reason
            };

            // a payee you already added once (shop, cafe...) goes in directly - no need to review it again
            if (StatementImportSettings.AutoAddKnown &&
                knownPayee && !isOwnTransfer && categoryId is int knownCategory &&
                cls.Type is "Income" or "Expense")
            {
                try
                {
                    await _detected.AddKnownAsync(detected, knownCategory);
                    autoAdded++;
                    continue;
                }
                catch (Exception ex)
                {
                    // anything wrong (no account...) -> it simply waits in the inbox
                    System.Diagnostics.Debug.WriteLine($"[StatementImport] auto-add: {ex}");
                }
            }

            await _repo.AddAsync(detected);

            added++;

            if (confidence == "Low" || ReviewTypes.Contains(cls.Type))
                review++;
        }

        return new StatementImportResult(
            true, null,
            parsedRows.Count + skipped + unreadable,
            added, duplicates, skipped, unreadable, review, autoAdded,
            _accountLinks.TakeNewlyCreated(),
            loanStatements);
    }

    // ───────────────────────── helpers ─────────────────────────

    private async Task<List<ContactLite>> LoadContactsAsync()
    {
        var result = new List<ContactLite>();

        try
        {
            foreach (var c in await _contactRepo.FindAsync(c => true))
            {
                if (string.IsNullOrWhiteSpace(c.Name)) continue;

                var records = await _borrowLend.GetByContactAsync(c.Id);

                result.Add(new ContactLite(
                    c.Id,
                    c.Name,
                    records.Any(r => r.Type == "Lend" && r.PendingAmount > 0),
                    records.Any(r => r.Type == "Borrow" && r.PendingAmount > 0)));
            }
        }
        catch (Exception ex)
        {
            // contacts are only a hint - import still works without them
            System.Diagnostics.Debug.WriteLine($"[StatementImport] contacts: {ex}");
        }

        return result;
    }

    private static string? MakeKey(string? payee)
    {
        var key = payee?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(key) ? null : key;
    }

    private static string LooseKey(DateTime date, decimal amount, string? payee)
        => $"{date:yyyyMMdd}|{amount:0.##}|{(payee ?? string.Empty).Trim().ToLowerInvariant()}";
}
