using System.Globalization;
using System.Text.RegularExpressions;

namespace MoneySpend.Services;

/// <param name="Number">Instalment number (1, 2, 3 ...)</param>
public sealed record EmiInstalment(int Number, DateTime DueDate, decimal Amount, bool Paid, DateTime? PaidDate, decimal? PaidAmount);

/// <summary>Everything a loan / EMI statement of account tells us.</summary>
public sealed record EmiStatement(
    string? Lender,
    string? LoanNumber,
    string? CustomerName,
    string? Product,
    string? Dealer,
    decimal LoanAmount,
    decimal? InterestRate,
    int TenureMonths,
    decimal InstalmentAmount,
    decimal? FirstInstalmentAmount,
    DateTime? LoanDate,
    DateTime? FirstDueDate,
    DateTime? EndDate,
    decimal? TotalRepaid,
    decimal? Outstanding,
    int? FutureInstalments,
    string? Status,
    string? RepaymentBank,
    string? RepaymentAccountLast4,
    List<EmiInstalment> Instalments)
{
    public int PaidCount => Instalments.Count(i => i.Paid);
}

/// <summary>
/// Pure C# (no MAUI): reads the TEXT LINES of a loan "Statement of Account" PDF
/// (written for the Bajaj Finance layout; other lenders need their own sample).
/// </summary>
public static class EmiStatementParser
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex DateLine = new(@"^(?<d>\d{2}-[A-Za-z]{3}-\d{4})\s+(?<rest>.*)$", Opt);
    private static readonly Regex Decimal = new(@"-?\d[\d,]*\.\d{2}", Opt);

    public static bool LooksLikeLoanStatement(IEnumerable<string> lines)
    {
        var text = string.Join("\n", lines);

        return Regex.IsMatch(text, @"STATEMENT OF ACCOUNT FOR", Opt) &&
               Regex.IsMatch(text, @"LOAN TRANSACTION DETAILS", Opt) &&
               Regex.IsMatch(text, @"Due for Installment", Opt);
    }

    public static EmiStatement? Parse(IEnumerable<string> rawLines)
    {
        var lines = rawLines.Select(l => Regex.Replace(l ?? string.Empty, @"\s+", " ").Trim())
                            .Where(l => l.Length > 0)
                            .ToList();

        if (!LooksLikeLoanStatement(lines)) return null;

        var text = string.Join("\n", lines);

        var loanAmount = Money(text, @"(?<!Outstanding )Loan Amount \(₹\)\s+(?<v>[\d,]+(?:\.\d+)?)");
        var instalment = Money(text, @"(?<!First )Instalment Amount \(₹\)\s+(?<v>[\d,]+\.\d{2})");
        var tenure = Int(text, @"Loan Tenure \(In Months\)\s+(?<v>\d+)");

        if (loanAmount is null || instalment is null || tenure is null) return null;

        // ---- repayment bank: "STATE BANK OF INDIA" then "xxxxxxxxxxxxx5264" ----
        string? bank = null, last4 = null;

        var bankMatch = Regex.Match(text, @"Repayment Bank A/c Details\s+(?<b>[A-Z][A-Z ]+?)(?=\s+Interest Start|\n)", Opt);
        if (bankMatch.Success) bank = bankMatch.Groups["b"].Value.Trim();

        var digits = Regex.Match(text, @"(?m)^x+(?<d>\d{4})\b", Opt);
        if (digits.Success) last4 = digits.Groups["d"].Value;

        // ---- dealer: one line + a continuation line ("GANESH TRADELECTRO PVT" / "LTD # WARDHA") ----
        string? dealer = null;
        var dealerIndex = lines.FindIndex(l => l.StartsWith("Dealer / Merchant Name", StringComparison.OrdinalIgnoreCase));

        if (dealerIndex >= 0)
        {
            var first = Regex.Replace(lines[dealerIndex], @"^Dealer / Merchant Name\s+", string.Empty, Opt);
            first = Regex.Replace(first, @"\s+Instalment Repayment End Date.*$", string.Empty, Opt).Trim();

            // the name continues on a later line ("LTD # WARDHA"); the two page columns can mix the order,
            // so look at the next few lines for an all-capitals line
            var next = lines.Skip(dealerIndex + 1).Take(3)
                .FirstOrDefault(l => Regex.IsMatch(l, @"^[A-Z][A-Z0-9 #.&\-]*$"));

            dealer = next is null ? first : $"{first} {next}".Trim();
        }

        var instalments = ReadInstalments(lines);

        return new EmiStatement(
            Lender: Regex.Match(text, @"values are as per (?<n>.+?)['’]s records", Opt) is { Success: true } lm
                ? lm.Groups["n"].Value.Trim()
                : null,
            LoanNumber: Str(text, @"STATEMENT OF ACCOUNT FOR\s+(?<v>\S+)"),
            CustomerName: Str(text, @"(?m)^Name\s+(?<v>[A-Z][A-Z .]+?)(?=\s+Current Rate|$)"),
            Product: Str(text, @"Product Type\s+(?<v>[A-Z][A-Z ]+?)(?=\s+Loan Creation|\n)"),
            Dealer: dealer,
            LoanAmount: loanAmount.Value,
            InterestRate: Money(text, @"Current Rate of Interest Per Annum\s+(?<v>\d+(?:\.\d+)?)%"),
            TenureMonths: tenure.Value,
            InstalmentAmount: instalment.Value,
            FirstInstalmentAmount: Money(text, @"First Instalment Amount \(₹\)\s+(?<v>[\d,]+\.\d{2})"),
            LoanDate: Date(text, @"Loan Creation Date\s+(?<v>\d{2}-[A-Za-z]{3}-\d{4})"),
            FirstDueDate: Date(text, @"First Instalment Due Date\s+(?<v>\d{2}-[A-Za-z]{3}-\d{4})"),
            EndDate: Date(text, @"Instalment Repayment End Date\s+(?<v>\d{2}-[A-Za-z]{3}-\d{4})"),
            TotalRepaid: Money(text, @"Total Loan Amount Repaid \(₹\)\s+(?<v>[\d,]+\.\d{2})"),
            Outstanding: Money(text, @"Total Outstanding Amount \(₹\)\*?\s+(?<v>[\d,]+\.\d{2})"),
            FutureInstalments: Int(text, @"Future Instalment Number \(In Months\)\s+(?<v>\d+)"),
            Status: Str(text, @"Loan Status\s+(?<v>[A-Za-z]+)"),
            RepaymentBank: bank,
            RepaymentAccountLast4: last4,
            Instalments: instalments);
    }

    // ───────────────── the transaction table ─────────────────
    private static List<EmiInstalment> ReadInstalments(List<string> lines)
    {
        // a row = a line that starts with a date + the lines under it until the next date
        var starts = lines
            .Select((l, i) => (l, i, m: DateLine.Match(l)))
            .Where(x => x.m.Success)
            .ToList();

        var dues = new List<(int Number, DateTime Date, decimal Amount)>();
        var receipts = new List<(DateTime Date, decimal Amount)>();

        for (var s = 0; s < starts.Count; s++)
        {
            var from = starts[s].i;
            var to = s + 1 < starts.Count ? starts[s + 1].i : lines.Count;
            var block = string.Join(" ", lines.Skip(from).Take(to - from));

            var date = DateTime.ParseExact(starts[s].m.Groups["d"].Value, "dd-MMM-yyyy", CultureInfo.InvariantCulture);
            var numbers = Decimal.Matches(block)
                .Select(m => decimal.Parse(m.Value.Replace(",", ""), CultureInfo.InvariantCulture))
                .ToList();

            // columns: Due, Paid, BounceDue, BouncePaid, PenalDue, PenalPaid, Balance
            if (numbers.Count < 2) continue;

            var due = Regex.Match(block, @"Due for Installment (?<n>\d+)", Opt);

            if (due.Success)
            {
                dues.Add((int.Parse(due.Groups["n"].Value), date, numbers[0]));
            }
            else if (Regex.IsMatch(block, @"Amount Received for.*\bCleared\b", Opt) ||
                     Regex.IsMatch(block, @"Payment Received vide", Opt))
            {
                // money that really came in towards an instalment (a "Bounced" one did not)
                receipts.Add((date, numbers[1]));
            }
        }

        // pair the n-th payment with the n-th instalment (the statement lists them in order)
        var ordered = dues.OrderBy(d => d.Number).ToList();
        var paid = receipts.OrderBy(r => r.Date).ToList();

        return ordered
            .Select((d, i) => i < paid.Count
                ? new EmiInstalment(d.Number, d.Date, d.Amount, true, paid[i].Date, paid[i].Amount)
                : new EmiInstalment(d.Number, d.Date, d.Amount, false, null, null))
            .ToList();
    }

    // ───────────────── small helpers ─────────────────
    private static string? Str(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, Opt);
        return m.Success ? m.Groups["v"].Value.Trim() : null;
    }

    private static decimal? Money(string text, string pattern)
    {
        var raw = Str(text, pattern)?.Replace(",", "");
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static int? Int(string text, string pattern)
        => int.TryParse(Str(text, pattern), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateTime? Date(string text, string pattern)
        => DateTime.TryParseExact(Str(text, pattern), "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
