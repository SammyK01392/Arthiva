using SQLite;
using Arthiva.Models;
// Explicit alias — required because .NET MAUI's SDK auto-injects a global
// using for Microsoft.Maui.ApplicationModel.Communication, which also has a
// "Contact" type. Without this alias, "Contact" below would silently bind to
// the wrong (MAUI) type and CreateTableAsync<Contact>() would create a table
// for the wrong class.
using Contact = Arthiva.Models.Contact;

namespace Arthiva.Data;

public class ArthivaDatabase
{
    private readonly SQLiteAsyncConnection _database;

    public ArthivaDatabase(string dbPath)
    {
        _database = new SQLiteAsyncConnection(dbPath);
    }

    public SQLiteAsyncConnection Database => _database;

    public async Task InitializeAsync()
    {
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

        await SeedData.SeedAsync(_database);
    }
}