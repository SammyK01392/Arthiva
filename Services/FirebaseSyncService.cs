using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using MoneySpend.Data;
using MoneySpend.Models;
using Contact = MoneySpend.Models.Contact;

namespace MoneySpend.Services;

public class FirebaseSyncService : IFirebaseSyncService
{
    private readonly MoneySpendDatabase _db;
    private readonly IFirebaseAuthService _authService;
    private readonly HttpClient _http = new();

    private readonly IBackupDataService _backupData;

    public FirebaseSyncService(MoneySpendDatabase db, IFirebaseAuthService authService, IBackupDataService backupData)
    {
        _db = db;
        _authService = authService;
        _backupData = backupData;
    }

    public async Task BackupAsync(IProgress<string>? progress = null)
    {
        var (idToken, uid) = await GetSessionOrThrowAsync();

        progress?.Report("Reading local data...");
        var snapshot = await _backupData.CreateSnapshotAsync();

        progress?.Report("Uploading to cloud...");
        var content = new StringContent(snapshot.Json, Encoding.UTF8, "application/json");
        var url = $"{FirebaseConstants.DatabaseUrl}/users/{uid}/backup.json?auth={idToken}";
        var response = await _http.PutAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Backup failed: {err}");
        }
        progress?.Report("Backup complete.");
    }

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

        progress?.Report("Restoring from cloud...");
        try { await _backupData.RestoreSnapshotAsync(json); }
        catch (InvalidDataException ex) { throw new InvalidOperationException($"Restore failed: {ex.Message}"); }

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