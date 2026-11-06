namespace MoneySpend.Services;

public sealed record AccountInfo(int Id, string Name, string? Type);

/// <summary>Pure logic (no database): names for auto-created accounts, and "do I already have it?".</summary>
public static class AccountNaming
{
    public const string GiftHint = "GIFT";

    /// <summary>"IDBI Bank" + 9847 -> "IDBI Bank ••9847";  no bank name -> "Bank ••5264".</summary>
    public static string NameFor(string hint, string? bankName)
    {
        if (hint == GiftHint) return string.IsNullOrWhiteSpace(bankName) ? "Gift Card" : bankName.Trim();

        // "STATE BANK OF INDIA" -> "State Bank of India" (names that are not ALL CAPS stay as they are)
        return $"{(string.IsNullOrWhiteSpace(bankName) ? "Bank" : Tidy(bankName))} ••{hint}";
    }

    private static readonly HashSet<string> KeepUpper = new(StringComparer.OrdinalIgnoreCase)
    {
        "SBI", "HDFC", "ICICI", "IDBI", "PNB", "BOB", "UCO", "RBL", "IDFC", "AU", "YES", "UPI", "NEFT", "EMI", "LTD", "PVT"
    };

    /// <summary>"STATE BANK OF INDIA" -> "State Bank of India". Names that are not ALL CAPS are left alone ("IDBI Bank").</summary>
    public static string Tidy(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        name = name.Trim();
        if (name != name.ToUpperInvariant()) return name;

        var words = name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w =>
        {
            if (KeepUpper.Contains(w)) return w.ToUpperInvariant();
            if (w is "of" or "and" or "the") return w;
            return char.ToUpperInvariant(w[0]) + w[1..];
        });

        return string.Join(' ', words);
    }

    public static string TypeFor(string hint) => hint == GiftHint ? "Wallet" : "Bank";

    /// <summary>
    /// Re-use an account you already made instead of creating a second one:
    ///  1. its name already has these digits ("HDFC 0422"), or for gift card a name with "gift"
    ///  2. exactly ONE account has the bank's name in it ("IDBI" in "IDBI Bank")
    /// Accounts already linked to ANOTHER account number are never taken.
    /// </summary>
    public static int? FindExisting(
        string hint, string? bankName, IReadOnlyList<AccountInfo> accounts, ISet<int> linkedToOthers)
    {
        var free = accounts.Where(a => !linkedToOthers.Contains(a.Id)).ToList();

        var byDigits = hint == GiftHint
            ? free.Where(a => a.Name.Contains("gift", StringComparison.OrdinalIgnoreCase)).ToList()
            : free.Where(a => a.Name.Contains(hint, StringComparison.Ordinal)).ToList();

        if (byDigits.Count == 1) return byDigits[0].Id;
        if (byDigits.Count > 1 || hint == GiftHint) return null;

        var word = BankWord(bankName);
        if (word is null) return null;

        var byBank = free
            .Where(a => a.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return byBank.Count == 1 ? byBank[0].Id : null;
    }

    /// <summary>"IDBI Bank" -> "IDBI",  "State Bank of India" -> "State".</summary>
    private static string? BankWord(string? bankName)
    {
        if (string.IsNullOrWhiteSpace(bankName)) return null;

        return bankName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(t => t.Length >= 3 && !t.Equals("bank", StringComparison.OrdinalIgnoreCase));
    }
}
