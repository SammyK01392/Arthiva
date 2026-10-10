using System;
using System.Collections.Generic;
using System.Text;

using System.Security.Cryptography;
using System.Text.Json;
using MoneySpend.Data;
using MoneySpend.Models;
using SQLite;
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public interface IBackupDataService
{
    Task<BackupSnapshot> CreateSnapshotAsync();
    /// Atomic: either every table is replaced or nothing changes.
    Task RestoreSnapshotAsync(string json);
}

public class BackupDataService : IBackupDataService
{
    public const int SchemaVersion = 4;
    private readonly MoneySpendDatabase _db;

    // Derived/volatile data excluded from the change-detection hash so that
    // e.g. marking a notification read doesn't make Sync think data changed.
    private static readonly HashSet<string> HashExcludedKeys = new() { "notifications", "monthlySummaries" };

    private sealed record TableSpec(
        string Key,
        Func<SQLiteAsyncConnection, Task<object>> Read,
        Action<SQLiteConnection> Clear,
        Action<SQLiteConnection, JsonElement> Insert);

    private static TableSpec Spec<T>(string key) where T : new() => new(
        key,
        async c => (object)await c.Table<T>().ToListAsync(),
        c => c.DeleteAll<T>(),
        (c, el) =>
        {
            var items = JsonSerializer.Deserialize<List<T>>(el.GetRawText());
            if (items is null) return;
            // InsertOrReplace writes the PK column too (plain Insert skips
            // auto-increment PKs), so foreign keys stay valid after restore.
            foreach (var item in items) c.InsertOrReplace(item);
        });

    // Parents first. Keys 1–18 are identical to the existing Firebase keys.
    private static readonly TableSpec[] Tables =
    {
        Spec<UserProfile>("userProfile"),
        Spec<Account>("accounts"),
        Spec<Category>("categories"),
        Spec<Transaction>("transactions"),
        Spec<Contact>("contacts"),
        Spec<BorrowLend>("borrowLends"),
        Spec<BorrowLendTransaction>("borrowLendTransactions"),
        Spec<EmiMaster>("emiMasters"),
        Spec<EmiPayment>("emiPayments"),
        Spec<Bill>("bills"),
        Spec<BillPayment>("billPayments"),
        Spec<Budget>("budgets"),
        Spec<SavingGoal>("savingGoals"),
        Spec<GoalTransaction>("goalTransactions"),
        Spec<RecurringTransaction>("recurringTransactions"),
        Spec<Notification>("notifications"),
        Spec<Attachment>("attachments"),
        Spec<MonthlySummary>("monthlySummaries"),
        // Previously missing from backups:
        Spec<SplitGroup>("splitGroups"),
        Spec<SplitGroupMember>("splitGroupMembers"),
        Spec<SplitExpense>("splitExpenses"),
        Spec<SplitShare>("splitShares"),
        Spec<GroupSettlement>("groupSettlements"),
        Spec<SharedRequestLink>("sharedRequestLinks"),
        Spec<SharedSettlementLink>("sharedSettlementLinks"),
    };

    public BackupDataService(MoneySpendDatabase db) => _db = db;

    public async Task<BackupSnapshot> CreateSnapshotAsync()
    {
        var data = new Dictionary<string, object>();
        var rows = 0;
        foreach (var t in Tables)
        {
            var list = await t.Read(_db.Database);
            data[t.Key] = list;
            rows += ((System.Collections.ICollection)list).Count;
        }

        var hashable = data.Where(kv => !HashExcludedKeys.Contains(kv.Key))
                           .ToDictionary(kv => kv.Key, kv => kv.Value);
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(hashable)));

        data["backedUpAt"] = DateTime.UtcNow.ToString("O");
        data["schemaVersion"] = SchemaVersion;
        return new BackupSnapshot(JsonSerializer.Serialize(data), hash, rows);
    }

    public async Task RestoreSnapshotAsync(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new InvalidDataException("Backup data is corrupted.", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("accounts", out _))
                throw new InvalidDataException("This is not a MoneySpend backup.");

            if (root.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out var ver) && ver > SchemaVersion)
                throw new InvalidDataException("This backup is from a newer app version. Please update MoneySpend.");

            // Only touch tables that exist in the backup (older backups lack split tables).
            var present = Tables.Where(t => root.TryGetProperty(t.Key, out var e)
                                            && e.ValueKind == JsonValueKind.Array).ToList();

            try
            {
                await _db.Database.RunInTransactionAsync(conn =>
                {
                    foreach (var t in Enumerable.Reverse(present)) t.Clear(conn);      // children first
                    foreach (var t in present) t.Insert(conn, root.GetProperty(t.Key)); // parents first
                });
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Backup data is corrupted.", ex); // transaction already rolled back
            }
        }
    }
}