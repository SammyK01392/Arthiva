using MoneySpend.Models;
using SQLite;

namespace MoneySpend.Data;

public static class SeedData
{
    public static async Task SeedAsync(SQLiteAsyncConnection db)
    {
        await SeedAccountsAsync(db);
        await SeedCategoriesAsync(db);

        // NEW: runs on every start, adds only what is missing (safe for phones that already have data)
        await EnsureImportCategoriesAsync(db);
    }

    private static async Task SeedAccountsAsync(SQLiteAsyncConnection db)
    {
        var accountCount = await db.Table<Account>().CountAsync();

        if (accountCount > 0)
            return;

        var accounts = new List<Account>
        {
            new()
            {
                Name = "Cash Wallet",
                Type = "Cash",
                OpeningBalance = 0,
                CurrentBalance = 0,
                IsDefault = true,
                IsActive = true
            },

            new()
            {
                Name = "Bank Account",
                Type = "Bank",
                OpeningBalance = 0,
                CurrentBalance = 0,
                IsActive = true
            },

            new()
            {
                Name = "UPI Wallet",
                Type = "Wallet",
                OpeningBalance = 0,
                CurrentBalance = 0,
                IsActive = true
            }
        };

        await db.InsertAllAsync(accounts);
    }

    private static async Task SeedCategoriesAsync(SQLiteAsyncConnection db)
    {
        var categoryCount = await db.Table<Category>().CountAsync();

        if (categoryCount > 0)
            return;

        var categories = new List<Category>
        {
            // Income
            new() { Name = "Salary", Type = "Income", Icon = "salary", Color = "#4CAF50", IsDefault = true },
            new() { Name = "Bonus", Type = "Income", Icon = "bonus", Color = "#2196F3", IsDefault = true },
            new() { Name = "Interest", Type = "Income", Icon = "interest", Color = "#009688", IsDefault = true },
            new() { Name = "Gift", Type = "Income", Icon = "gift", Color = "#9C27B0", IsDefault = true },

            // Expense
            new() { Name = "Food", Type = "Expense", Icon = "food", Color = "#FF9800", IsDefault = true },
            new() { Name = "Fuel", Type = "Expense", Icon = "fuel", Color = "#795548", IsDefault = true },
            new() { Name = "Travel", Type = "Expense", Icon = "travel", Color = "#3F51B5", IsDefault = true },
            new() { Name = "Shopping", Type = "Expense", Icon = "shopping", Color = "#E91E63", IsDefault = true },
            new() { Name = "Medical", Type = "Expense", Icon = "medical", Color = "#F44336", IsDefault = true },
            new() { Name = "Rent", Type = "Expense", Icon = "rent", Color = "#607D8B", IsDefault = true },
            new() { Name = "Electricity", Type = "Expense", Icon = "electricity", Color = "#FFC107", IsDefault = true },
            new() { Name = "Internet", Type = "Expense", Icon = "internet", Color = "#00BCD4", IsDefault = true },
            new() { Name = "Mobile Recharge", Type = "Expense", Icon = "mobile", Color = "#673AB7", IsDefault = true },
            new() { Name = "EMI", Type = "Expense", Icon = "emi", Color = "#F57C00", IsDefault = true },
            new() { Name = "Entertainment", Type = "Expense", Icon = "movie", Color = "#8BC34A", IsDefault = true },
            new() { Name = "Education", Type = "Expense", Icon = "education", Color = "#009688", IsDefault = true },
            new() { Name = "Insurance", Type = "Expense", Icon = "insurance", Color = "#455A64", IsDefault = true }
        };

        await db.InsertAllAsync(categories);
    }

    /// <summary>
    /// Categories that statement / screenshot import needs to file entries properly:
    ///  - Groceries, Personal Care      : shops that are not "Food" or "Shopping"
    ///  - Others (expense)              : the safe place for a shop the app cannot guess
    ///  - Cashback &amp; Rewards, Refund, Other Income : money received from PhonePe / Google Pay etc.
    /// Icons reuse the ones you already have - change them to your own if you like.
    /// </summary>
    private static async Task EnsureImportCategoriesAsync(SQLiteAsyncConnection db)
    {
        var existing = await db.Table<Category>().ToListAsync();

        var wanted = new List<Category>
        {
            new() { Name = "Groceries", Type = "Expense", Icon = "shopping", Color = "#4CAF50", IsDefault = true },
            new() { Name = "Personal Care", Type = "Expense", Icon = "shopping", Color = "#EC407A", IsDefault = true },
            new() { Name = "Others", Type = "Expense", Icon = "rent", Color = "#9E9E9E", IsDefault = true },

            new() { Name = "Cashback & Rewards", Type = "Income", Icon = "gift", Color = "#FF9800", IsDefault = true },
            new() { Name = "Refund", Type = "Income", Icon = "interest", Color = "#00ACC1", IsDefault = true },
            new() { Name = "Other Income", Type = "Income", Icon = "bonus", Color = "#78909C", IsDefault = true }
        };

        var missing = wanted
            .Where(w => !existing.Any(e =>
                string.Equals(e.Name?.Trim(), w.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Type, w.Type, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count > 0)
            await db.InsertAllAsync(missing);
    }
}
