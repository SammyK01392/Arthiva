using System.Globalization;
using System.Text.RegularExpressions;

namespace MoneySpend.Services;

/// <summary>One transaction read from a statement / screenshot.</summary>
public sealed record StatementRow(
    DateTime Date,
    decimal Amount,
    bool IsCredit,
    string? Counterparty,
    string? CounterpartyUpi,
    string? Description,
    string? ReferenceId,
    string RawBlock,
    string? AccountHint = null,
    string? AccountName = null);

public sealed record StatementParseResult(List<StatementRow> Rows, int Skipped, int Unreadable);

/// <summary>
/// Pure C# (no Android/MAUI dependency): turns the TEXT LINES of a PhonePe / Paytm / GPay-style
/// statement (PDF text or OCR of a screenshot) into transactions. Failed/pending rows are skipped.
/// </summary>
public static class StatementParser
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string Mon =
        @"jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sept?(?:ember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?";

    // order matters: dates WITH a year first, then no-year formats (anchored to line start)
    private static readonly Regex[] DateRegexes =
    {
        new(@"(?<!\d)(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})(?!\d)", Opt),                                              // 2026-10-06
        new($@"\b(?<mon>{Mon})\.?\s+(?<d>\d{{1,2}})(?:st|nd|rd|th)?,?\s+(?<y>\d{{4}})\b", Opt),                      // Oct 06, 2026
        new($@"(?<!\d)(?<d>\d{{1,2}})(?:st|nd|rd|th)?[\s\-/,]+(?<mon>{Mon})\.?,?[\s\-/,]+(?<y>\d{{4}})\b", Opt),     // 06 Oct 2026
        new($@"(?<!\d)(?<d>\d{{1,2}})-(?<mon>{Mon})-(?<y>\d{{2}})\b", Opt),                                          // 06-Oct-26
        new(@"(?<!\d)(?<d>\d{1,2})[/\-.](?<m>\d{1,2})[/\-.](?<y>\d{4}|\d{2})(?!\d)", Opt),                          // 06/10/2026
        new($@"^(?:[a-z]{{3}},?\s+)?(?<d>\d{{1,2}})(?:st|nd|rd|th)?\s+(?<mon>{Mon})\b", Opt),                        // 06 Oct   (no year)
        new($@"^(?:[a-z]{{3}},?\s+)?(?<mon>{Mon})\.?\s+(?<d>\d{{1,2}})(?:st|nd|rd|th)?\b", Opt),                     // Oct 6    (no year)
    };

    private static readonly Regex TimeRx = new(@"\b(?<h>\d{1,2}):(?<mi>\d{2})(?::\d{2})?\s*(?<ap>am|pm)?\b", Opt);

    private static readonly Regex AmountRx = new(
        @"(?<sign>[-+−–])?\s*(?:₹|rs\.?|inr)\s*(?<a>\d[\d,]*(?:\.\d{1,2})?)", Opt);

    private static readonly Regex BalanceWord = new(@"\bbal(?:ance)?\b|\blimit\b|\bavailable\b|\bclosing\b|\bopening\b", Opt);

    private static readonly Regex FailedRx = new(
        @"\b(?:failed|declined|unsuccessful|cancell?ed|reversed|expired|rejected)\b", Opt);

    private static readonly Regex PendingRx = new(@"\b(?:pending|processing|in progress|awaiting)\b", Opt);

    private static readonly Regex DirectionToken = new(@"\b(?<t>debit(?:ed)?|dr|credit(?:ed)?|cr)\b", Opt);

    private static readonly Regex DebitPhrase = new(
        @"\b(?:paid to|sent to|payment to|payment of|transferred to|money sent|bill paid|recharge|purchase|spent|withdrawn)\b", Opt);

    private static readonly Regex CreditPhrase = new(
        @"\b(?:received from|money received|received|refund|cashback|reward|interest|deposited|money added)\b", Opt);

    private static readonly Regex CounterpartyRx = new(
        @"(?:paid to|sent to|payment to|transferred to|money sent to|money transferred to|received from|money received from|credited by|refund from|cashback from|rewards? from)\s+(?<n>.+?)" +
        @"(?=\s+(?:debit|credit|transaction|txn|utr|upi|paid by|credited to|debited from|ref|order|tag|note|remarks|via)\b|\s+(?:rs\.?|inr|₹)|\s+[-+−–]\s*(?:rs|₹|inr)|\s+\d{9,}|$)",
        Opt);

    private static readonly Regex UpiIdRx = new(@"(?<![\w.\-])(?<u>[\w.\-]{2,}@[a-z][a-z0-9.\-]{1,})", Opt);

    private static readonly Regex RefKeyNum = new(
        @"(?:utr|rrn|upi\s*ref(?:erence)?(?:\s*(?:no|number|id))?|ref(?:erence)?\s*(?:no|number|id)?)\D{0,20}?(?<![\d])(?<r>\d{12})(?![\d@])", Opt);

    private static readonly Regex Ref12 = new(@"(?<![\d])(?<r>\d{12})(?![\d@])");

    private static readonly Regex TxnIdRx = new(
        @"(?:transaction\s*id|txn\s*id|order\s*id)\s*[:#]?\s*(?<r>[A-Za-z0-9]{8,30})", Opt);

    private static readonly Regex PageFooter = new(
        @"^(?:powered by\s+)?page\s+\d+\s+of\s+\d+$", Opt);

    private static readonly Regex TableHeader = new(
        @"^date\b.*\b(?:transaction|details)\b.*\bamount\b", Opt);

    // "Paid by IDBI Bank 9847" -> bank "IDBI Bank", digits 9847 ;  "Paid by XXXXXXXXXX020422" -> digits 0422
    private static readonly Regex AccountHintRx = new(
        @"(?:paid by|paid to|credited to|debited from)\s+(?<bank>(?:[A-Za-z]+\s+){0,3})[Xx\d]*?(?<d>\d{4})\b", Opt);

    private static readonly Regex GiftCardCredit = new(@"credited to\s+gift\s*card", Opt);

    private static readonly Regex Boilerplate = new(
        @"^(?:page\s+\d+(?:\s+of\s+\d+)?|transaction statement.*|date\s+(?:&\s*time\s+)?transaction.*|.*statement period.*|.*generated (?:on|at).*|" +
        @".*statement for.*|disclaimer.*|this is a system generated.*|.*end of statement.*|.*total (?:debit|credit|money).*|" +
        @"note:.*|received\. any payments.*|\d{1,2}\s+[a-z]+,?\s+\d{4}\s*-\s*\d{1,2}\s+[a-z]+,?\s+\d{4}.*)$", Opt);

    public static StatementParseResult Parse(IEnumerable<string> rawLines, DateTime today)
    {
        var lines = new List<string>();
        var skipping = false;   // between "Page N of M" and the next table header = page header junk

        foreach (var raw in rawLines)
        {
            var l = Clean(raw);
            if (l.Length == 0) continue;

            if (PageFooter.IsMatch(l))
            {
                skipping = true;
                continue;
            }

            if (skipping)
            {
                if (TableHeader.IsMatch(l))
                {
                    skipping = false;
                    continue;
                }

                // safety: no table header on the next page -> stop skipping at the first real date line
                if (TryReadDate(l, today, out _, out var firstAt) && firstAt <= 6)
                    skipping = false;
                else
                    continue;
            }

            if (Boilerplate.IsMatch(l)) continue;

            lines.Add(l);
        }

        var starts = new List<(int Index, DateTime Date)>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (TryReadDate(lines[i], today, out var date, out var at) && at <= 6)
                starts.Add((i, date));
        }

        var rows = new List<StatementRow>();
        int skipped = 0, unreadable = 0;

        for (var s = 0; s < starts.Count; s++)
        {
            var from = starts[s].Index;
            var to = s + 1 < starts.Count ? starts[s + 1].Index : lines.Count;
            var block = lines.Skip(from).Take(to - from).ToList();

            switch (ParseBlock(block, starts[s].Date, out var row))
            {
                case BlockResult.Row: rows.Add(row!); break;
                case BlockResult.Skipped: skipped++; break;
                case BlockResult.Unreadable: unreadable++; break;
            }
        }

        return new StatementParseResult(rows, skipped, unreadable);
    }

    private enum BlockResult { Row, Skipped, Unreadable, Ignore }

    private static BlockResult ParseBlock(List<string> block, DateTime date, out StatementRow? row)
    {
        row = null;

        var text = Regex.Replace(string.Join(" ", block), @"credit\s*card", "creditcard", Opt);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // time (first 3 lines)
        var head = string.Join(" ", block.Take(3));
        var tm = TimeRx.Match(head);
        if (tm.Success)
        {
            var h = int.Parse(tm.Groups["h"].Value, CultureInfo.InvariantCulture);
            var mi = int.Parse(tm.Groups["mi"].Value, CultureInfo.InvariantCulture);
            var ap = tm.Groups["ap"].Value.ToLowerInvariant();

            if (ap == "pm" && h < 12) h += 12;
            if (ap == "am" && h == 12) h = 0;

            if (h is >= 0 and < 24 && mi is >= 0 and < 60)
                date = date.Date.AddHours(h).AddMinutes(mi);
        }

        var looksLikeTxn = Regex.IsMatch(text, @"\b(?:paid|received|sent|debit|credit|refund|cashback|transfer)", Opt);

        if (FailedRx.IsMatch(text) || PendingRx.IsMatch(text))
            return looksLikeTxn ? BlockResult.Skipped : BlockResult.Ignore;

        // ---- amount (first currency amount that is not a balance) ----
        decimal? amount = null;
        char? sign = null;

        foreach (Match m in AmountRx.Matches(text))
        {
            var before = text[Math.Max(0, m.Index - 25)..m.Index];
            if (BalanceWord.IsMatch(before)) continue;

            if (decimal.TryParse(m.Groups["a"].Value.Replace(",", ""), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var a) && a > 0)
            {
                amount = a;

                var sg = m.Groups["sign"].Value;
                if (sg.Length > 0) sign = sg[0] is '+' ? '+' : '-';

                break;
            }
        }

        if (amount is null)
            return looksLikeTxn ? BlockResult.Unreadable : BlockResult.Ignore;

        // ---- direction: sign > DEBIT/CREDIT word > phrase ----
        bool? isCredit = sign switch { '+' => true, '-' => false, _ => null };

        if (isCredit is null)
        {
            var t = DirectionToken.Match(text);
            if (t.Success)
                isCredit = t.Groups["t"].Value.StartsWith("c", StringComparison.OrdinalIgnoreCase);
        }

        if (isCredit is null)
        {
            var d = DebitPhrase.Match(text);
            var c = CreditPhrase.Match(text);

            if (d.Success || c.Success)
                isCredit = c.Success && (!d.Success || c.Index < d.Index);
        }

        if (isCredit is null) return looksLikeTxn ? BlockResult.Unreadable : BlockResult.Ignore;

        // ---- counterparty ----
        string? counterparty = null;
        var cm = CounterpartyRx.Match(text);
        if (cm.Success) counterparty = CleanName(cm.Groups["n"].Value);

        var upi = UpiIdRx.Match(text) is { Success: true } um ? um.Groups["u"].Value : null;

        if (counterparty is null && block.Count > 1)
        {
            // fallback: first text line after the date/time lines
            foreach (var original in block.Skip(1))
            {
                var l = TimeRx.Replace(original, " ").Trim();   // drop "09:44 pm"

                if (l.Length < 2 || !l.Any(char.IsLetter)) continue;
                if (AmountRx.IsMatch(l)) continue;
                if (Regex.IsMatch(l, @"^(?:transaction|txn|utr|upi|order|paid by|credited to|debited from|paid to|tag|note|debit|credit)", Opt)) continue;

                counterparty = CleanName(l);
                break;
            }
        }

        // ---- reference (prefer the 12-digit UTR so it matches bank SMS) ----
        string? reference = null;

        var rm = RefKeyNum.Match(text);
        if (rm.Success) reference = rm.Groups["r"].Value;

        if (reference is null)
        {
            var r12 = Ref12.Match(text);
            if (r12.Success) reference = r12.Groups["r"].Value;
        }

        if (reference is null)
        {
            var tx = TxnIdRx.Match(text);
            if (tx.Success) reference = tx.Groups["r"].Value;
        }

        string? accountHint = null;
        string? accountName = null;

        if (GiftCardCredit.IsMatch(text))
        {
            // PhonePe cashback goes to the PhonePe gift card, not to a bank
            accountHint = "GIFT";
            accountName = "PhonePe Gift Card";
        }
        else if (AccountHintRx.Match(text) is { Success: true } am)
        {
            accountHint = am.Groups["d"].Value;

            var bank = Regex.Replace(am.Groups["bank"].Value, @"\s+", " ").Trim();
            if (Regex.IsMatch(bank, @"\bbank\b", Opt)) accountName = bank;
        }

        var who = counterparty ?? upi;

        var description = who is null
            ? (text.Length > 120 ? text[..120] : text)
            : (isCredit.Value ? "Received from " : "Paid to ") + who;

        row = new StatementRow(date, amount.Value, isCredit.Value, counterparty, upi, description, reference,
            text.Length > 600 ? text[..600] : text,
            accountHint,
            accountName);

        return BlockResult.Row;
    }

    // ───────────────────────── helpers ─────────────────────────

    private static string Clean(string s)
        => Regex.Replace((s ?? string.Empty).Replace('\u00A0', ' '), @"\s+", " ").Trim();

    private static string? CleanName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;

        var n = Regex.Replace(s, @"\(.*?\)", " ");
        n = Regex.Replace(n, @"\s+", " ").Trim().Trim('.', ',', ':', ';', '-', ' ');

        if (n.Length < 2) return null;
        if (Regex.IsMatch(n, @"^x{2,}\d*$", Opt)) return null;   // masked account like XXXX1234
        if (!n.Any(char.IsLetter)) return null;

        return n.Length > 100 ? n[..100] : n;
    }

    private static bool TryReadDate(string line, DateTime today, out DateTime date, out int index)
    {
        date = default;
        index = int.MaxValue;

        for (var r = 0; r < DateRegexes.Length; r++)
        {
            var m = DateRegexes[r].Match(line);
            if (!m.Success) continue;

            int day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            int month;

            if (m.Groups["mon"].Success)
                month = MonthNumber(m.Groups["mon"].Value);
            else
                month = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);

            int year;

            if (m.Groups["y"].Success)
            {
                year = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
                if (year < 100) year += 2000;
            }
            else
            {
                year = today.Year;
            }

            try
            {
                var d = new DateTime(year, month, day);

                // no year in the text: a date in the future must be last year's
                if (!m.Groups["y"].Success && d > today.Date.AddDays(1))
                    d = d.AddYears(-1);

                date = d;
                index = m.Index;
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                // not a real date, try the next pattern
            }
        }

        return false;
    }

    private static int MonthNumber(string name)
        => name.Length >= 3
            ? new[] { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" }
                .ToList().IndexOf(name[..3].ToLowerInvariant()) + 1
            : 0;
}
