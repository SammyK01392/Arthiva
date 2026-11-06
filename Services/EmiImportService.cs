using System.Text;
using System.Text.RegularExpressions;
using MoneySpend.Models;

namespace MoneySpend.Services;

public sealed record EmiImportPlan(
    EmiStatement Statement,
    int? EmiId,
    string Title,
    string AccountText,
    string MatchReason,
    bool Ambiguous,
    List<EmiInstalment> ToAdd,
    int AlreadyRecorded)
{
    public bool IsNew => EmiId is null && !Ambiguous;

    /// <summary>Plain words: what is about to happen.</summary>
    public string Summary
    {
        get
        {
            var sb = new StringBuilder();

            if (IsNew)
            {
                sb.AppendLine($"New EMI: {Title}");
                sb.AppendLine($"₹{Statement.InstalmentAmount:N0} x {Statement.TenureMonths} months (loan ₹{Statement.LoanAmount:N0}, {Statement.InterestRate ?? 0:0.##}%)");
                sb.AppendLine($"{ToAdd.Count} paid instalments will be recorded (₹{ToAdd.Sum(i => i.PaidAmount ?? 0):N0})");
            }
            else
            {
                sb.AppendLine($"Already in your EMIs: {Title}");
                sb.AppendLine($"({MatchReason})");
                sb.AppendLine($"{ToAdd.Count} new payment(s) will be added, {AlreadyRecorded} already recorded");
            }

            if (!string.IsNullOrEmpty(AccountText))
                sb.AppendLine($"Pays from: {AccountText}");

            return sb.ToString().TrimEnd();
        }
    }
}

public sealed record EmiImportResult(string Summary, int PaymentsAdded, int LinkedToExisting, bool Created);

public interface IEmiImportService
{
    /// <summary>What would happen? (new EMI, or which payments are missing). Changes nothing.</summary>
    Task<EmiImportPlan> PlanAsync(EmiStatement statement);

    /// <param name="affectBalance">true = also create the Expense in the account; false = history only.</param>
    Task<EmiImportResult> ApplyAsync(EmiImportPlan plan, bool affectBalance);
}

public class EmiImportService : IEmiImportService
{
    private readonly IEmiService _emi;
    private readonly IGenericRepository<EmiPayment> _paymentRepo;
    private readonly IGenericRepository<Transaction> _txRepo;
    private readonly IGenericRepository<Category> _categoryRepo;
    private readonly IGenericRepository<DetectedTransaction> _detectedRepo;
    private readonly ITransactionService _transactions;
    private readonly IAccountLinkService _accountLinks;

    public EmiImportService(
        IEmiService emi,
        IGenericRepository<EmiPayment> paymentRepo,
        IGenericRepository<Transaction> txRepo,
        IGenericRepository<Category> categoryRepo,
        IGenericRepository<DetectedTransaction> detectedRepo,
        ITransactionService transactions,
        IAccountLinkService accountLinks)
    {
        _emi = emi;
        _paymentRepo = paymentRepo;
        _txRepo = txRepo;
        _categoryRepo = categoryRepo;
        _detectedRepo = detectedRepo;
        _transactions = transactions;
        _accountLinks = accountLinks;
    }

    // ─────────────────────────────────────────────────────────
    //  PLAN (changes nothing)
    // ─────────────────────────────────────────────────────────
    public async Task<EmiImportPlan> PlanAsync(EmiStatement st)
    {
        var all = await _emi.GetAllAsync(includeCompleted: true);

        var existing = all
            .Select(e => new ExistingEmi(
                e.Id, $"{e.Title} {e.LoanProvider}", e.LoanReferenceNo, e.EmiAmount,
                e.TotalInstallment, e.TotalAmount, e.StartDate))
            .ToList();

        var match = EmiMatcher.Find(st, existing);

        var accountText = string.IsNullOrWhiteSpace(st.RepaymentAccountLast4)
            ? string.Empty
            : $"{AccountNaming.Tidy(st.RepaymentBank)} ••{st.RepaymentAccountLast4}".Trim();

        // several EMIs look alike: do not guess
        if (match.EmiId is null && match.Confidence == "Low")
            return new EmiImportPlan(st, null, TitleFor(st), accountText, match.Reason, true, new List<EmiInstalment>(), 0);

        if (match.EmiId is int id)
        {
            var emi = all.First(e => e.Id == id);

            var recorded = (await _emi.GetPaymentsAsync(id))
                .Where(p => p.Status == "Paid")
                .Select(p => new ExistingPayment(p.PaymentDate, p.Amount));

            var missing = EmiMatcher.MissingPayments(st, recorded);

            return new EmiImportPlan(st, id, emi.Title, accountText, match.Reason, false,
                missing, st.PaidCount - missing.Count);
        }

        return new EmiImportPlan(st, null, TitleFor(st), accountText, match.Reason, false,
            st.Instalments.Where(i => i.Paid).ToList(), 0);
    }

    // ─────────────────────────────────────────────────────────
    //  APPLY
    // ─────────────────────────────────────────────────────────
    public async Task<EmiImportResult> ApplyAsync(EmiImportPlan plan, bool affectBalance)
    {
        if (plan.Ambiguous)
            throw new InvalidOperationException("More than one EMI looks like this one. Open the right EMI and add the payments there.");

        var st = plan.Statement;

        // the account the EMI is paid from (made automatically from the account number)
        int? accountId = null;

        if (!string.IsNullOrWhiteSpace(st.RepaymentAccountLast4))
            accountId = await _accountLinks.ResolveAsync(st.RepaymentAccountLast4, st.RepaymentBank);

        // ---- 1. the EMI itself ----
        EmiMaster emi;
        var created = false;

        if (plan.EmiId is int id)
        {
            emi = await _emi.GetByIdAsync(id) ?? throw new InvalidOperationException("The EMI was not found.");

            // fill what was empty, never overwrite what you typed
            if (string.IsNullOrWhiteSpace(emi.LoanReferenceNo)) emi.LoanReferenceNo = st.LoanNumber;
            if (emi.AccountId is null) emi.AccountId = accountId;
        }
        else
        {
            var firstDue = st.FirstDueDate ?? st.LoanDate ?? DateTime.Today;

            emi = new EmiMaster
            {
                Title = plan.Title,
                LoanProvider = ShortLender(st.Lender),
                TotalAmount = st.LoanAmount,
                EmiAmount = st.InstalmentAmount,
                InterestRate = st.InterestRate ?? 0,
                TotalInstallment = st.TenureMonths,
                PaidInstallment = 0,
                DueDay = firstDue.Day,
                // your schedule counts months from the FIRST DUE date (instalment 1 = that month)
                StartDate = firstDue,
                EndDate = st.EndDate,
                AccountId = accountId,
                Status = "Active",
                LoanReferenceNo = st.LoanNumber,
                Notes = BuildNotes(st)
            };

            await _emi.CreateAsync(emi);

            if (emi.Id == 0)
            {
                emi = (await _emi.GetAllAsync(includeCompleted: true))
                    .OrderByDescending(e => e.Id)
                    .FirstOrDefault(e => e.Title == plan.Title)
                    ?? throw new InvalidOperationException("Could not create the EMI.");
            }

            created = true;
        }

        // ---- 2. payments (we add them ourselves so history and balance can be chosen separately) ----
        var categories = await _categoryRepo.FindAsync(c => true);
        var categoryId = CategoryResolver.Resolve("EMI", false,
            categories.Where(c => !string.IsNullOrWhiteSpace(c.Name)).Select(c => (c.Id, c.Name!, (string?)c.Type)).ToList()) ?? 0;

        var lenderWord = (emi.LoanProvider ?? emi.Title).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(t => t.Length >= 3);

        // expenses that may already be in the books for these payments (e.g. from bank SMS)
        var from = plan.ToAdd.Select(i => i.PaidDate ?? i.DueDate).DefaultIfEmpty(DateTime.Today).Min().Date.AddDays(-4);
        var to = plan.ToAdd.Select(i => i.PaidDate ?? i.DueDate).DefaultIfEmpty(DateTime.Today).Max().Date.AddDays(5);

        var candidates = (await _txRepo.FindAsync(t =>
                !t.IsDeleted && t.TransactionType == "Expense" && t.TransactionDate >= from && t.TransactionDate <= to))
            .ToList();

        var takenTxnIds = (await _emi.GetPaymentsAsync(emi.Id))
            .Where(p => p.TransactionId.HasValue)
            .Select(p => p.TransactionId!.Value)
            .ToHashSet();

        var pendingDetected = (await _detectedRepo.FindAsync(d => d.Status == DetectedStatus.Pending && !d.IsCredit))
            .ToList();

        var added = 0;
        var linked = 0;

        foreach (var i in plan.ToAdd.OrderBy(x => x.Number))
        {
            var paidOn = i.PaidDate ?? i.DueDate;
            var amount = i.PaidAmount ?? i.Amount;

            var payment = new EmiPayment
            {
                EmiId = emi.Id,
                Amount = amount,
                InstallmentNo = i.Number,
                PaymentDate = paidOn,
                DueDate = i.DueDate,
                Status = "Paid",
                Remarks = RemarkFor(st, i),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _paymentRepo.AddAsync(payment);
            added++;

            // is the money already in the books? then link it, never add it twice
            var existingTxn = candidates.FirstOrDefault(t =>
                !takenTxnIds.Contains(t.Id) &&
                Math.Abs(t.Amount - amount) <= 0.5m &&
                Math.Abs((t.TransactionDate.Date - paidOn.Date).TotalDays) <= 3 &&
                (t.SourceType == "EMI" ||
                 (categoryId != 0 && t.CategoryId == categoryId) ||
                 (lenderWord is not null &&
                  $"{t.Payee} {t.Description}".Contains(lenderWord, StringComparison.OrdinalIgnoreCase))));

            int? txnId = null;

            if (existingTxn is not null)
            {
                txnId = existingTxn.Id;
                takenTxnIds.Add(existingTxn.Id);
                linked++;
            }
            else if (affectBalance && (emi.AccountId ?? accountId) is int payFrom)
            {
                var txn = new Transaction
                {
                    AccountId = payFrom,
                    CategoryId = categoryId,
                    Amount = amount,
                    TransactionType = "Expense",
                    TransactionDate = paidOn,
                    Payee = emi.LoanProvider,
                    PaymentMethod = "Auto debit",
                    Description = $"{emi.Title} - Installment #{i.Number}",
                    SourceType = "EMI",
                    SourceReferenceId = emi.Id
                };

                await _transactions.AddTransactionAsync(txn);
                txnId = txn.Id > 0 ? txn.Id : null;
            }

            if (txnId is not null)
            {
                payment.TransactionId = txnId;
                await _paymentRepo.UpdateAsync(payment);

                // the same debit may be waiting in the Detected Payments inbox (from SMS): it is done now
                foreach (var d in pendingDetected.Where(d =>
                             Math.Abs(d.Amount - amount) <= 0.5m &&
                             Math.Abs((d.TransactionDate.Date - paidOn.Date).TotalDays) <= 3 &&
                             (d.SuggestedType == "Emi" ||
                              (lenderWord is not null &&
                               $"{d.Payee} {d.RawBody}".Contains(lenderWord, StringComparison.OrdinalIgnoreCase)))).ToList())
                {
                    d.Status = DetectedStatus.Added;
                    d.TransactionId = txnId;
                    await _detectedRepo.UpdateAsync(d);
                    pendingDetected.Remove(d);
                }
            }
        }

        // ---- 3. counters, exactly like RecordPayment / DeletePayment keep them ----
        var allPayments = await _emi.GetPaymentsAsync(emi.Id);
        emi.PaidInstallment = allPayments.Count;
        emi.Status = emi.PaidInstallment >= emi.TotalInstallment ? "Completed" : "Active";
        await _emi.UpdateAsync(emi);

        var sb = new StringBuilder();
        sb.AppendLine(created ? $"Created EMI: {emi.Title}" : $"Updated EMI: {emi.Title}");
        sb.AppendLine($"{added} payment(s) added ({emi.PaidInstallment} of {emi.TotalInstallment} paid)");

        if (linked > 0) sb.AppendLine($"{linked} matched an expense already in your books (not added again)");
        if (!affectBalance) sb.AppendLine("Account balance was not changed.");

        return new EmiImportResult(sb.ToString().TrimEnd(), added, linked, created);
    }

    // ───────────────── helpers ─────────────────
    private static string ShortLender(string? lender)
        => Regex.Replace(lender ?? string.Empty, @"\s+(?:Limited|Ltd\.?|Private|Pvt\.?)$", string.Empty, RegexOptions.IgnoreCase).Trim();

    private static string TitleFor(EmiStatement st)
    {
        var lender = ShortLender(st.Lender);
        var product = AccountNaming.Tidy(st.Product);

        if (string.IsNullOrEmpty(lender)) lender = "Loan";
        return string.IsNullOrEmpty(product) ? $"{lender} loan" : $"{lender} - {product}";
    }

    private static string BuildNotes(EmiStatement st)
    {
        var parts = new List<string> { "Created from statement" };

        if (!string.IsNullOrWhiteSpace(st.Dealer)) parts.Add($"Dealer: {st.Dealer}");
        if (st.Outstanding is decimal o) parts.Add($"Outstanding ₹{o:N0}");

        var text = string.Join(" • ", parts);
        return text.Length > 500 ? text[..500] : text;
    }

    private static string RemarkFor(EmiStatement st, EmiInstalment i)
    {
        var remark = $"From statement • loan {st.LoanNumber}";

        // the first instalment of this loan includes a one-time charge (2,085 instead of 1,849)
        if (i.Number == 1 && st.FirstInstalmentAmount is decimal first && first > st.InstalmentAmount)
            remark += $" • includes ₹{first - st.InstalmentAmount:N0} one-time charges";

        return remark.Length > 500 ? remark[..500] : remark;
    }
}
