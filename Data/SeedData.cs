using Arthiva.Models;
using SQLite;

namespace Arthiva.Data;

public static class SeedData
{
    public static async Task SeedAsync(SQLiteAsyncConnection db)
    {
        await SeedAccountsAsync(db);
        await SeedCategoriesAsync(db);
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
            new()
            {
                Name = "Salary",
                Type = "Income",
                Icon = "salary",
                Color = "#4CAF50",
                IsDefault = true
            },

            new()
            {
                Name = "Bonus",
                Type = "Income",
                Icon = "bonus",
                Color = "#2196F3",
                IsDefault = true
            },

            new()
            {
                Name = "Interest",
                Type = "Income",
                Icon = "interest",
                Color = "#009688",
                IsDefault = true
            },

            new()
            {
                Name = "Gift",
                Type = "Income",
                Icon = "gift",
                Color = "#9C27B0",
                IsDefault = true
            },

            // Expense
            new()
            {
                Name = "Food",
                Type = "Expense",
                Icon = "food",
                Color = "#FF9800",
                IsDefault = true
            },

            new()
            {
                Name = "Fuel",
                Type = "Expense",
                Icon = "fuel",
                Color = "#795548",
                IsDefault = true
            },

            new()
            {
                Name = "Travel",
                Type = "Expense",
                Icon = "travel",
                Color = "#3F51B5",
                IsDefault = true
            },

            new()
            {
                Name = "Shopping",
                Type = "Expense",
                Icon = "shopping",
                Color = "#E91E63",
                IsDefault = true
            },

            new()
            {
                Name = "Medical",
                Type = "Expense",
                Icon = "medical",
                Color = "#F44336",
                IsDefault = true
            },

            new()
            {
                Name = "Rent",
                Type = "Expense",
                Icon = "rent",
                Color = "#607D8B",
                IsDefault = true
            },

            new()
            {
                Name = "Electricity",
                Type = "Expense",
                Icon = "electricity",
                Color = "#FFC107",
                IsDefault = true
            },

            new()
            {
                Name = "Internet",
                Type = "Expense",
                Icon = "internet",
                Color = "#00BCD4",
                IsDefault = true
            },

            new()
            {
                Name = "Mobile Recharge",
                Type = "Expense",
                Icon = "mobile",
                Color = "#673AB7",
                IsDefault = true
            },

            new()
            {
                Name = "EMI",
                Type = "Expense",
                Icon = "emi",
                Color = "#F57C00",
                IsDefault = true
            },

            new()
            {
                Name = "Entertainment",
                Type = "Expense",
                Icon = "movie",
                Color = "#8BC34A",
                IsDefault = true
            },

            new()
            {
                Name = "Education",
                Type = "Expense",
                Icon = "education",
                Color = "#009688",
                IsDefault = true
            },

            new()
            {
                Name = "Insurance",
                Type = "Expense",
                Icon = "insurance",
                Color = "#455A64",
                IsDefault = true
            }
        };

        await db.InsertAllAsync(categories);
    }
}