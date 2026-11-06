namespace MoneySpend.Services;

/// <summary>
/// Pure logic (no database): which of YOUR categories fits a statement row?
/// Looks only at categories of the right type (Income for money in, Expense for money out).
/// </summary>
public static class CategoryResolver
{
    // the classifier says e.g. "Groceries"; if you do not have that category, try these in order
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Groceries"] = new[] { "Groceries", "Grocery", "Food" },
        ["Medical"] = new[] { "Medical", "Health", "Healthcare" },
        ["Personal Care"] = new[] { "Personal Care", "Salon", "Grooming" },
        ["Mobile Recharge"] = new[] { "Mobile Recharge", "Recharge", "Bills", "Mobile" },
        ["Internet"] = new[] { "Internet", "Broadband", "Bills" },
        ["Electricity"] = new[] { "Electricity", "Bills", "Utilities" },
        ["Cashback & Rewards"] = new[] { "Cashback & Rewards", "Cashback", "Rewards", "Other Income", "Gift" },
        ["Refund"] = new[] { "Refund", "Other Income" },
        ["Interest"] = new[] { "Interest" },
    };

    private static readonly string[] ExpenseDefaults = { "others", "other", "miscellaneous", "misc", "uncategorized", "general" };
    private static readonly string[] IncomeDefaults = { "other income", "others", "other", "miscellaneous", "cashback & rewards", "cashback" };

    public static int? Resolve(string? hint, bool isCredit, IReadOnlyList<(int Id, string Name, string? Type)> categories)
    {
        if (string.IsNullOrWhiteSpace(hint)) return null;

        var pool = Pool(isCredit, categories);
        var candidates = Aliases.TryGetValue(hint, out var list) ? list : new[] { hint };

        foreach (var name in candidates)
        {
            var exact = pool.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exact.Id != 0) return exact.Id;
        }

        // loose match: "Mobile" inside "Mobile Recharge" and the other way round
        foreach (var name in candidates)
        {
            var loose = pool.FirstOrDefault(c =>
                c.Name.Length >= 4 && name.Length >= 4 &&
                (c.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                 name.Contains(c.Name, StringComparison.OrdinalIgnoreCase)));

            if (loose.Id != 0) return loose.Id;
        }

        return null;
    }

    /// <summary>"Others"-type category of the right kind. null = there is none (the row then stays in the inbox).</summary>
    public static int? DefaultFor(bool isCredit, IReadOnlyList<(int Id, string Name, string? Type)> categories)
    {
        var pool = Pool(isCredit, categories);

        foreach (var wanted in isCredit ? IncomeDefaults : ExpenseDefaults)
        {
            var match = pool.FirstOrDefault(c => string.Equals(c.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            if (match.Id != 0) return match.Id;
        }

        return null;
    }

    private static List<(int Id, string Name, string? Type)> Pool(
        bool isCredit, IReadOnlyList<(int Id, string Name, string? Type)> categories)
    {
        var wanted = isCredit ? "Income" : "Expense";

        return categories
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) &&
                        (string.IsNullOrWhiteSpace(c.Type) || string.Equals(c.Type, wanted, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
