using SQLite;
using MoneySpend.Models;
// Explicit alias — required because .NET MAUI's SDK auto-injects a global
// using for Microsoft.Maui.ApplicationModel.Communication, which also has a
// "Contact" type. Without this alias, "Contact" below would silently bind to
// the wrong (MAUI) type and CreateTableAsync<Contact>() would create a table
// for the wrong class.
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Data;

public class MoneySpendDatabase
{
    private readonly SQLiteAsyncConnection _database;

    public MoneySpendDatabase(string dbPath)
    {
        _database = new SQLiteAsyncConnection(dbPath);
    }

    public SQLiteAsyncConnection Database => _database;

    public async Task InitializeAsync()
    {
        // CHANGED: WAL mode — writes aur reads ek doosre ko block nahi karte,
        // isse refresh ke time list/dashboard queries jaldi chalti hain.
        // journal_mode ek row return karta hai, isliye ExecuteScalarAsync use kiya hai.
        await _database.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;");
        await _database.ExecuteAsync("PRAGMA synchronous=NORMAL;");

        await _database.CreateTableAsync<UserProfile>();
        await _database.CreateTableAsync<Account>();
        await _database.CreateTableAsync<Category>();
        await _database.CreateTableAsync<Transaction>();
        await _database.CreateTableAsync<Contact>();
        await _database.CreateTableAsync<BorrowLend>();
        await _database.CreateTableAsync<BorrowLendTransaction>();
        await _database.CreateTableAsync<EmiMaster>();
        await _database.CreateTableAsync<EmiPayment>();
        await _database.CreateTableAsync<Bill>();
        await _database.CreateTableAsync<BillPayment>();
        await _database.CreateTableAsync<Budget>();
        await _database.CreateTableAsync<SavingGoal>();
        await _database.CreateTableAsync<GoalTransaction>();
        await _database.CreateTableAsync<RecurringTransaction>();
        await _database.CreateTableAsync<Notification>();
        await _database.CreateTableAsync<Attachment>();
        await _database.CreateTableAsync<MonthlySummary>();
        await _database.CreateTableAsync<SplitExpense>();
        await _database.CreateTableAsync<SplitShare>();
        await _database.CreateTableAsync<SplitGroup>();
        await _database.CreateTableAsync<SplitGroupMember>();
        await _database.CreateTableAsync<GroupSettlement>();

        await SeedData.SeedAsync(_database);

        // One-time, idempotent: rows created before the sync feature existed
        // get NULL for the new SyncId column after auto-migration. Give them
        // a stable id now so they're immediately eligible to sync once a
        // Google account is ever linked. Safe to run on every launch — once
        // every row has a SyncId, the query returns nothing and this is a
        // no-op read with no writes.
        await BackfillTransactionSyncIdsAsync();
    }

    private async Task BackfillTransactionSyncIdsAsync()
    {
        // Filtered at the SQL level (WHERE SyncId IS NULL) rather than
        // loading the whole table into memory — matters once a user has
        // thousands of historical transactions.
        var rowsMissingSyncId = await _database.Table<Transaction>()
            .Where(t => t.SyncId == null)
            .ToListAsync();

        if (rowsMissingSyncId.Count == 0)
            return;

        foreach (var row in rowsMissingSyncId)
        {
            row.SyncId = Guid.NewGuid().ToString();
        }

        await _database.UpdateAllAsync(rowsMissingSyncId);
    }
}