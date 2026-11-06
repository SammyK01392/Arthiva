using System.Text.RegularExpressions;

namespace MoneySpend.Services;

/// <summary>A saved contact, with what is still open between you and them.</summary>
public sealed record ContactLite(int Id, string Name, bool HasOpenLend, bool HasOpenBorrow);

/// <param name="Type">Income, Expense, Emi, Borrow, Lend, ReturnPaid, ReturnReceived, Transfer</param>
/// <param name="Confidence">High, Medium or Low (Low = needs review)</param>
public sealed record Classification(string Type, string Confidence, string Reason, string? CategoryHint);

/// <summary>Finds money that moved between YOUR OWN accounts (the same UTR shows up as sent AND received).</summary>
public static class StatementPairing
{
    public static HashSet<string> OwnTransferRefs(IEnumerable<StatementRow> rows)
        => rows
            .Where(r => !string.IsNullOrWhiteSpace(r.ReferenceId))
            .GroupBy(r => r.ReferenceId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(r => r.IsCredit) && g.Any(r => !r.IsCredit))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Rule based (no AI): decides what a statement row probably is.</summary>
public static class StatementClassifier
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex IncomeWords = new(
        @"\b(?:salary|payroll|wages?|cashback|refund|interest|rewards?|dividend|bonus|stipend|incentive)\b", Opt);

    private static readonly Regex EmiWords = new(
        @"\b(?:emi|nach|ecs|mandate|loan|installment|instalment|repayment|bajaj|finserv|fullerton|home credit|hdb)\b", Opt);

    private static readonly Regex TransferWords = new(
        @"\b(?:self|own account|add(?:ed)? money|money added|wallet (?:top ?up|load)|to (?:your )?bank|withdraw|load wallet|credit card bill|card bill)\b", Opt);

    private static readonly Regex BusinessWords = new(
        @"\b(?:store|stores|mart|cafe|restaurant|hotel|pharmacy|medical|medicals|enterprise|enterprises|traders|trading|foods?|bakery|bakers?|sweets|" +
        @"kirana|supermarket|supermarts?|petrol|pump|fuel|services|solutions|technologies|pvt|private|ltd|limited|llp|inc|bank|finance|insurance|" +
        @"hospital|clinic|school|college|vidyalay|vidyalaya|academy|electronics|garments|textiles|fashion|salon|saloon|parlour|dairy|general|agency|" +
        @"corporation|company|commerce|shop|centre|center|market|bazaar|mall|travels|tours|gas|electricity|recharge|stationery|hardware|" +
        @"jewellers|motors|fruits|vegetables|tea|chai|juice|tailors?|tailoring|perfumes?|upahar|gruha|bhavan|bhojanalay|dhaba|veg|pan)\b", Opt);

    private static readonly Regex MerchantUpi = new(
        @"paytmqr|paytm\.s|bharatpe|\.pos\b|razorpay|billdesk|merchant|@ptys|@paytm\b|gpay-|phonepe\.|swiggy|zomato|amazon|flipkart", Opt);

    private static readonly Regex Titles = new(@"^(?:mr|mrs|ms|miss|dr|shri|smt|sri|shree)\.?\s+", Opt);

    // order matters: the first match wins. Names are the categories in SeedData (+ Groceries / Personal Care).
    private static readonly (Regex Rx, string Category)[] CategoryRules =
    {
        (new(@"swiggy|zomato|restaurant|cafe|bakery|bakers?|\bfoods?\b|dominos|kfc|mcdonald|pizza|biryani|sweets|dhaba|juice|\btea\b|chai|hotel|upahar|gruha|bhojanalay|\bveg\b|milkshake", Opt), "Food"),
        (new(@"\bmart\b|kirana|supermarkets?|bigbasket|blinkit|blink commerce|zepto|instamart|grocery|dmart|vegetable|fruits|dairy|milk", Opt), "Groceries"),
        (new(@"petrol|fuel|\bpump\b|indian ?oil|bharat ?petroleum|\bhpcl\b", Opt), "Fuel"),
        (new(@"uber|\bola\b|rapido|irctc|redbus|metro|fastag|indigo|airlines|railway|\bbus\b", Opt), "Travel"),
        (new(@"airtel|\bjio\b|vodafone|\bvi\b|bsnl|recharge|prepaid|postpaid|\bdth\b|tata ?play", Opt), "Mobile Recharge"),
        (new(@"broadband|wi-?fi|fibernet", Opt), "Internet"),
        (new(@"electricity|bescom|tsspdcl|msedcl|discom|\bpower\b", Opt), "Electricity"),
        (new(@"amazon|flipkart|myntra|ajio|meesho|nykaa|\bstores?\b|\bmall\b|fashion|garments|tailors?|perfumes?", Opt), "Shopping"),
        (new(@"pharmacy|medical|medicals|hospital|clinic|apollo|netmeds|pharmeasy|\blab\b|diagnostic|dental", Opt), "Medical"),
        (new(@"netflix|spotify|hotstar|bookmyshow|prime video|youtube|gaming|cinema|\bpvr\b|inox", Opt), "Entertainment"),
        (new(@"school|college|tuition|byju|unacademy|course|\bfees?\b|vidyalay|vidyalaya|academy", Opt), "Education"),
        (new(@"salon|saloon|parlour|\bspa\b|barber", Opt), "Personal Care"),
    };

    public static Classification Classify(
        StatementRow row, IReadOnlyList<ContactLite> contacts, bool isOwnTransfer = false)
    {
        // 0. the same UTR was SENT in one statement and RECEIVED in another = your own accounts
        if (isOwnTransfer)
            return new("Transfer", "High", "Same UTR is both sent and received: money moved between your own accounts", null);

        var who = Normalize($"{row.Counterparty} {row.CounterpartyUpi} {row.Description}");

        // 1. Income-type words on money received
        // (also look at the whole block: "Cashback from PhonePe" is only in the original text)
        if (row.IsCredit && IncomeWords.IsMatch($"{who} {row.RawBlock}"))
            return new("Income", "High", "Salary / cashback / refund / interest keyword",
                IncomeCategoryFor($"{who} {row.RawBlock}"));

        // 2. EMI / loan repayments
        if (!row.IsCredit && EmiWords.IsMatch(who))
        {
            var strong = Regex.IsMatch(who, @"\b(?:emi|nach|ecs|mandate)\b", Opt);
            return new("Emi", strong ? "High" : "Medium", "EMI / loan keyword", null);
        }

        // 3. money moving between your own accounts / wallet / card bill
        if (TransferWords.IsMatch(who))
            return new("Transfer", "Medium", "Looks like a move between your own accounts or a card bill", null);

        // 4. a saved contact?
        var contact = FindContact(row, contacts);

        if (contact is not null)
        {
            if (!row.IsCredit)
            {
                return contact.HasOpenBorrow
                    ? new("ReturnPaid", "Medium", $"{contact.Name} is in your contacts and you still owe them", null)
                    : new("Lend", "Medium", $"{contact.Name} is in your contacts (money sent to a person)", null);
            }

            return contact.HasOpenLend
                ? new("ReturnReceived", "Medium", $"{contact.Name} is in your contacts and owes you", null)
                : new("Borrow", "Medium", $"{contact.Name} is in your contacts (money received from a person)", null);
        }

        // 5. shop / business (known words, QR/merchant UPI id, or a category keyword)
        var merchantText = Normalize($"{row.Counterparty} {row.CounterpartyUpi}");
        var category = CategoryFor(merchantText);

        if (MerchantUpi.IsMatch(merchantText) || BusinessWords.IsMatch(merchantText) || category is not null)
        {
            if (row.IsCredit)
                return new("Income", "Medium", "Money received from a business", null);

            return new("Expense", "High", "Shop / business", category);
        }

        // 6. looks like a private person
        if (LooksLikePerson(row.Counterparty, row.CounterpartyUpi))
        {
            return row.IsCredit
                ? new("Borrow", "Low", "Looks like a person, not in your contacts", null)
                : new("Lend", "Low", "Looks like a person, not in your contacts", null);
        }

        // 7. unknown
        return row.IsCredit
            ? new("Income", "Low", "Unknown sender", null)
            : new("Expense", "Low", "Unknown payee", null);
    }

    private static string? IncomeCategoryFor(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:salary|payroll|wages?)\b", Opt)) return "Salary";
        if (Regex.IsMatch(text, @"\b(?:interest|dividend)\b", Opt)) return "Interest";
        if (Regex.IsMatch(text, @"\b(?:bonus|incentive|stipend)\b", Opt)) return "Bonus";
        if (Regex.IsMatch(text, @"\brefund", Opt)) return "Refund";
        if (Regex.IsMatch(text, @"\b(?:cashback|rewards?)\b", Opt)) return "Cashback & Rewards";
        return null;
    }

    private static string Normalize(string s) => s.Replace('_', ' ');

    private static string? CategoryFor(string text)
    {
        foreach (var (rx, category) in CategoryRules)
            if (rx.IsMatch(text)) return category;

        return null;
    }

    private static bool LooksLikePerson(string? name, string? upi)
    {
        if (upi is not null && Regex.IsMatch(upi, @"^\d{10}@", Opt)) return true;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Contains('@')) return Regex.IsMatch(name, @"^\d{10}@", Opt);

        name = Titles.Replace(name.Trim(), string.Empty);
        if (BusinessWords.IsMatch(name)) return false;

        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return tokens.Length is >= 1 and <= 4 &&
               tokens.All(t => t.All(c => char.IsLetter(c) || c == '.')) &&
               tokens.Any(t => t.Length >= 3);
    }

    private static ContactLite? FindContact(StatementRow row, IReadOnlyList<ContactLite> contacts)
    {
        var target = Tokens(row.Counterparty);
        if (target.Count == 0) return null;

        foreach (var c in contacts)
        {
            var t = Tokens(c.Name);
            if (t.Count == 0) continue;

            // every word of the saved name must be in the statement name ("Rahul" in "RAHUL SHARMA")
            if (t.All(target.Contains)) return c;
        }

        return null;
    }

    private static HashSet<string> Tokens(string? s)
        => Titles.Replace((s ?? string.Empty).Trim(), string.Empty)
            .ToLowerInvariant()
            .Split(new[] { ' ', '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 3 && x.All(char.IsLetter))
            .ToHashSet();
}
