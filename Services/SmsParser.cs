using System.Globalization;
using System.Text.RegularExpressions;

namespace MoneySpend.Services;

/// <summary>Result of reading one bank/UPI SMS (money OUT or money IN).</summary>
public sealed record ParsedSms(
    decimal Amount,
    bool IsCredit,
    string? Payee,
    string? ReferenceId,
    string? AccountHint,
    string? Method);

/// <summary>
/// Pure C# (no Android dependency) so it can be unit-tested anywhere.
/// Returns null for anything that is not a real debit/credit SMS
/// (OTPs, promos, failed/reversed payments, future dues, balance info...).
/// </summary>
public static class SmsParser
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // ---- things we never want -------------------------------------------------
    private static readonly Regex Ignore = new(
        @"\botp\b|one[\s-]?time|verification code|\bcvv\b|will be debited|to be debited|" +
        @"payment request|collect request|requested money|has requested|\bfailed\b|\bdeclined\b|" +
        @"unsuccessful|reversed|\bdue (?:on|by|date)\b|\bminimum due\b|credit limit|available limit|avl limit|" +
        @"payment of [^.]{0,40}received|\bstatement\b|\bpre-?approved\b|\bloan offer\b",
        Opt);

    // A real bank/UPI alert always mentions an account, card, UPI or wallet.
    private static readonly Regex AccountCue = new(
        @"\ba/?c\b|\bacct\b|\baccount\b|\bcard\b|creditcard|\bupi\b|\bvpa\b|\bwallet\b|\bbank\b", Opt);

    // ---- direction ------------------------------------------------------------
    private static readonly Regex DebitWord = new(
        @"\b(?:debited|debit|spent|withdrawn|purchase|paid|sent|payment of|used for|txn of|transaction of)\b", Opt);

    private static readonly Regex CreditWord = new(
        @"\b(?:credited|received|deposited)\b", Opt);

    // ---- amount ---------------------------------------------------------------
    private static readonly Regex AmountCurrency = new(
        @"(?:rs\.?|inr|₹)\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)", Opt);

    // SBI style: "debited by 500.0" / "credited by 500.0"
    private static readonly Regex AmountAfterVerb = new(
        @"(?:debited|credited|spent|paid|sent|withdrawn|deposited|received)\s*(?:by|for|of|with)?\s*([0-9][0-9,]*(?:\.[0-9]{1,2})?)", Opt);

    // ---- reference ------------------------------------------------------------
    private static readonly Regex RefKeyword = new(
        @"(?:ref|rrn|utr|upi)[a-z .:/#\-]{0,20}?(?<r>\d{9,18})", Opt);

    private static readonly Regex Ref12 = new(@"\b(?<r>\d{12})\b");

    // ---- payee: money OUT (tried in order) ------------------------------------
    private static readonly Regex[] DebitPayeePatterns =
    {
        new(@"\bto\s+vpa\s+(?<p>[\w.\-]+@[\w.]+)", Opt),
        new(@";\s*(?<p>[A-Za-z][A-Za-z .&'\-]{1,40}?)\s+credited", Opt),
        new(@"\bto\s+(?<p>[A-Za-z0-9@][A-Za-z0-9@ ._&'\-]{1,40}?)(?=\s+(?:on|ref|refno|upi|utr|txn|via|using|from|at|for|dt|date)\b|\s*[(.,;]|\s*$)", Opt),
        new(@"upi/p2[amp]/\d+/(?<p>[A-Za-z0-9][A-Za-z0-9 &._\-]{1,40})", Opt),
        new(@"\b(?:at|@)\s+(?<p>[A-Za-z0-9][A-Za-z0-9 &._'\-]{1,40}?)(?=\s+(?:on|ref|via|using)\b|\s*[(.,;]|\s*$)", Opt),
    };

    // ---- payee: money IN ------------------------------------------------------
    private static readonly Regex[] CreditPayeePatterns =
    {
        new(@"\b(?:by|from)\s+vpa\s+(?<p>[\w.\-]+@[\w.]+)", Opt),
        new(@"upi/p2[amp]/\d+/(?<p>[A-Za-z0-9][A-Za-z0-9 &._\-]{1,40})", Opt),
        new(@"\bfrom\s+(?<p>[A-Za-z0-9@][A-Za-z0-9@ ._&'\-]{1,40}?)(?=\s+(?:on|ref|refno|upi|utr|txn|via|using|to|at|for|dt|date)\b|\s*[(.,;]|\s*$)", Opt),
    };

    // ---- account hint ---------------------------------------------------------
    private static readonly Regex AccountLast = new(
        @"(?:a/?c(?:ct)?|account|card)\s*(?:no\.?|number)?\s*[:\-]?\s*(?:ending(?:\s+with)?\s*)?[xX*.]*\s*(?<d>\d{3,4})\b", Opt);

    private static readonly string[] BadPayees = { "your account", "account", "you", "self" };
    private static readonly string[] BadPayeePrefixes = { "a/c", "ac ", "acct", "account", "rs", "inr" };

    public static ParsedSms? Parse(string? sender, string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        // Bank SMS come from alphanumeric headers (AX-HDFCBK). Plain phone numbers are people.
        if (string.IsNullOrWhiteSpace(sender) || !sender.Any(char.IsLetter)) return null;

        // "Credit Card" must not count as a credit
        var text = Regex.Replace(body, @"credit\s*card", "creditcard", Opt);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        if (Ignore.IsMatch(text)) return null;
        if (!AccountCue.IsMatch(text)) return null;

        var d = DebitWord.Match(text);
        var c = CreditWord.Match(text);

        if (!d.Success && !c.Success) return null;

        // whichever word comes FIRST decides the direction
        // (ICICI debit SMS says "debited ... ; RAHUL credited" -> still a debit)
        var isCredit = c.Success && (!d.Success || c.Index < d.Index);

        var amount = ReadAmount(AmountCurrency.Match(text)) ?? ReadAmount(AmountAfterVerb.Match(text));
        if (amount is null || amount <= 0 || amount > 10_000_000m) return null;

        return new ParsedSms(
            amount.Value,
            isCredit,
            ReadPayee(text, isCredit),
            ReadRef(text),
            AccountLast.Match(text) is { Success: true } a ? a.Groups["d"].Value : null,
            ReadMethod(text));
    }

    private static decimal? ReadAmount(Match m)
    {
        if (!m.Success) return null;

        var raw = m.Groups[1].Value.Replace(",", "");
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? ReadRef(string text)
    {
        var m = RefKeyword.Match(text);
        if (m.Success) return m.Groups["r"].Value;

        m = Ref12.Match(text);
        return m.Success ? m.Groups["r"].Value : null;
    }

    private static string? ReadPayee(string text, bool isCredit)
    {
        foreach (var rx in isCredit ? CreditPayeePatterns : DebitPayeePatterns)
        {
            var m = rx.Match(text);
            if (!m.Success) continue;

            var p = m.Groups["p"].Value.Trim().Trim('.', ',', ';', '-', ' ');
            if (p.Length < 2) continue;
            if (BadPayees.Any(b => p.Equals(b, StringComparison.OrdinalIgnoreCase))) continue;
            if (BadPayeePrefixes.Any(b => p.StartsWith(b, StringComparison.OrdinalIgnoreCase))) continue;

            return p.Length > 100 ? p[..100] : p;
        }

        return null;
    }

    private static string ReadMethod(string text)
    {
        if (Regex.IsMatch(text, @"wallet|paytm balance", Opt)) return "Wallet";
        if (Regex.IsMatch(text, @"\bupi\b|\bvpa\b", Opt)) return "UPI";
        if (Regex.IsMatch(text, @"\bcard\b|creditcard", Opt)) return "Card";
        if (Regex.IsMatch(text, @"\bimps\b", Opt)) return "IMPS";
        if (Regex.IsMatch(text, @"\bneft\b", Opt)) return "NEFT";
        if (Regex.IsMatch(text, @"\brtgs\b", Opt)) return "RTGS";
        return "Bank";
    }
}
