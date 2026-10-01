using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Arthiva.Data;
using Arthiva.Models;
using Contact = Arthiva.Models.Contact;

namespace Arthiva.Services;

public class FirebaseSyncService : IFirebaseSyncService
{
    private readonly ArthivaDatabase _db;
    private readonly IFirebaseAuthService _authService;
    private readonly HttpClient _http = new();

    public FirebaseSyncService(ArthivaDatabase db, IFirebaseAuthService authService)
    {
        _db = db;
        _authService = authService;
    }

    // ── BACKUP ─────────────────────────────────────────────
    public async Task BackupAsync(IProgress<string>? progress = null)
    {
        var (idToken, uid) = await GetSessionOrThrowAsync();

        progress?.Report("Reading local data...");

        var backupPayload = new Dictionary<string, object>
        {
            ["backedUpAt"] = DateTime.UtcNow.ToString("O"),
            ["userProfile"] = await _db.Database.Table<UserProfile>().ToListAsync(),
            ["accounts"] = await _db.Database.Table<Account>().ToListAsync(),
            ["categories"] = await _db.Database.Table<Category>().ToListAsync(),
            ["transactions"] = await _db.Database.Table<Transaction>().ToListAsync(),
            ["contacts"] = await _db.Database.Table<Contact>().ToListAsync(),
            ["borrowLends"] = await _db.Database.Table<BorrowLend>().ToListAsync(),
            ["borrowLendTransactions"] = await _db.Database.Table<BorrowLendTransaction>().ToListAsync(),
            ["emiMasters"] = await _db.Database.Table<EmiMaster>().ToListAsync(),
            ["emiPayments"] = await _db.Database.Table<EmiPayment>().ToListAsync(),
            ["bills"] = await _db.Database.Table<Bill>().ToListAsync(),
            ["billPayments"] = await _db.Database.Table<BillPayment>().ToListAsync(),
            ["budgets"] = await _db.Database.Table<Budget>().ToListAsync(),
            ["savingGoals"] = await _db.Database.Table<SavingGoal>().ToListAsync(),
            ["goalTransactions"] = await _db.Database.Table<GoalTransaction>().ToListAsync(),
            ["recurringTransactions"] = await _db.Database.Table<RecurringTransaction>().ToListAsync(),
            ["notifications"] = await _db.Database.Table<Notification>().ToListAsync(),
            ["attachments"] = await _db.Database.Table<Attachment>().ToListAsync(),
            ["monthlySummaries"] = await _db.Database.Table<MonthlySummary>().ToListAsync(),
        };

        progress?.Report("Uploading to cloud...");

        var json = JsonSerializer.Serialize(backupPayload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"{FirebaseConstants.DatabaseUrl}/users/{uid}/backup.json?auth={idToken}";
        var response = await _http.PutAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Backup failed: {err}");
        }

        progress?.Report("Backup complete.");
    }

    // ── RESTORE ────────────────────────────────────────────
    public async Task RestoreAsync(IProgress<string>? progress = null)
    {
        var (idToken, uid) = await GetSessionOrThrowAsync();

        progress?.Report("Fetching cloud data...");

        var url = $"{FirebaseConstants.DatabaseUrl}/users/{uid}/backup.json?auth={idToken}";
        var response = await _http.GetAsync(url);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Restore failed: could not reach server.");

        var json = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            throw new InvalidOperationException("No backup found in the cloud.");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        progress?.Report("Clearing local data...");

        // Destructive restore: local data replaced entirely by cloud copy.
        // Order matters where foreign keys exist — delete children before parents.
        await _db.Database.DeleteAllAsync<Notification>();
        await _db.Database.DeleteAllAsync<Attachment>();
        await _db.Database.DeleteAllAsync<MonthlySummary>();
        await _db.Database.DeleteAllAsync<GoalTransaction>();
        await _db.Database.DeleteAllAsync<SavingGoal>();
        await _db.Database.DeleteAllAsync<RecurringTransaction>();
        await _db.Database.DeleteAllAsync<BillPayment>();
        await _db.Database.DeleteAllAsync<Bill>();
        await _db.Database.DeleteAllAsync<EmiPayment>();
        await _db.Database.DeleteAllAsync<EmiMaster>();
        await _db.Database.DeleteAllAsync<BorrowLendTransaction>();
        await _db.Database.DeleteAllAsync<BorrowLend>();
        await _db.Database.DeleteAllAsync<Contact>();
        await _db.Database.DeleteAllAsync<Transaction>();
        await _db.Database.DeleteAllAsync<Budget>();
        await _db.Database.DeleteAllAsync<Category>();
        await _db.Database.DeleteAllAsync<Account>();
        await _db.Database.DeleteAllAsync<UserProfile>();

        progress?.Report("Restoring from cloud...");

        await RestoreTableAsync<UserProfile>(root, "userProfile");
        await RestoreTableAsync<Account>(root, "accounts");
        await RestoreTableAsync<Category>(root, "categories");
        await RestoreTableAsync<Transaction>(root, "transactions");
        await RestoreTableAsync<Contact>(root, "contacts");
        await RestoreTableAsync<BorrowLend>(root, "borrowLends");
        await RestoreTableAsync<BorrowLendTransaction>(root, "borrowLendTransactions");
        await RestoreTableAsync<EmiMaster>(root, "emiMasters");
        await RestoreTableAsync<EmiPayment>(root, "emiPayments");
        await RestoreTableAsync<Bill>(root, "bills");
        await RestoreTableAsync<BillPayment>(root, "billPayments");
        await RestoreTableAsync<Budget>(root, "budgets");
        await RestoreTableAsync<SavingGoal>(root, "savingGoals");
        await RestoreTableAsync<GoalTransaction>(root, "goalTransactions");
        await RestoreTableAsync<RecurringTransaction>(root, "recurringTransactions");
        await RestoreTableAsync<Notification>(root, "notifications");
        await RestoreTableAsync<Attachment>(root, "attachments");
        await RestoreTableAsync<MonthlySummary>(root, "monthlySummaries");

        progress?.Report("Restore complete.");
    }

    private async Task RestoreTableAsync<T>(JsonElement root, string propertyName) where T : new()
    {
        if (!root.TryGetProperty(propertyName, out var arrayElement) || arrayElement.ValueKind != JsonValueKind.Array)
            return;

        var items = JsonSerializer.Deserialize<List<T>>(arrayElement.GetRawText());
        if (items is null || items.Count == 0)
            return;

        // Insert with existing primary keys preserved so foreign-key
        // relationships between tables (e.g. Transaction.AccountId) stay intact.
        await _db.Database.InsertAllAsync(items);
    }

    public async Task<DateTime?> GetLastBackupTimeAsync()
    {
        try
        {
            var (idToken, uid) = await GetSessionOrThrowAsync();
            var url = $"{FirebaseConstants.DatabaseUrl}/users/{uid}/backup/backedUpAt.json?auth={idToken}";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body) || body == "null") return null;

            var isoString = JsonSerializer.Deserialize<string>(body);
            return DateTime.TryParse(isoString, out var dt) ? dt.ToLocalTime() : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(string idToken, string uid)> GetSessionOrThrowAsync()
    {
        var idToken = await _authService.GetValidIdTokenAsync();
        if (string.IsNullOrEmpty(idToken))
            throw new InvalidOperationException("Please login first.");

        var uid = await SecureStorage.GetAsync("firebase_uid");
        if (string.IsNullOrEmpty(uid))
            throw new InvalidOperationException("Please login first.");

        return (idToken, uid);
    }
}