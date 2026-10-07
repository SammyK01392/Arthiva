using System;
using System.Collections.Generic;
using System.Text;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MoneySpend.Data;

namespace MoneySpend.Services;

public interface IGoogleDriveService
{
    Task<DriveFileInfo?> FindBackupAsync();
    Task<DriveFileInfo> UploadBackupAsync(byte[] data, string contentHash, string deviceName, DriveFileInfo? existing);
    Task<byte[]> DownloadAsync(string fileId);
}

public class GoogleDriveService : IGoogleDriveService
{
    private const string FilesUrl = "https://www.googleapis.com/drive/v3/files";
    private const string UploadUrl = "https://www.googleapis.com/upload/drive/v3/files";
    private const string FileFields = "id,name,modifiedTime,size,appProperties";

    private readonly IGoogleAuthService _auth;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public GoogleDriveService(IGoogleAuthService auth) => _auth = auth;

    /// Returns the newest backup file. If duplicates exist (e.g. a past race),
    /// the older ones are deleted so there is only ever one file.
    public async Task<DriveFileInfo?> FindBackupAsync()
    {
        var q = $"name = '{GoogleDriveConstants.BackupFileName}' and trashed = false";
        var url = $"{FilesUrl}?spaces=appDataFolder&q={Uri.EscapeDataString(q)}" +
                  $"&orderBy=modifiedTime%20desc&pageSize=10&fields=files({FileFields})";

        using var resp = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var files = doc.RootElement.GetProperty("files").EnumerateArray().Select(ParseFile).ToList();
        if (files.Count == 0) return null;

        foreach (var dup in files.Skip(1))
        {
            try { using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{FilesUrl}/{dup.Id}")); }
            catch { /* cleanup is best-effort */ }
        }
        return files[0];
    }

    public async Task<DriveFileInfo> UploadBackupAsync(byte[] data, string contentHash, string deviceName, DriveFileInfo? existing)
    {
        var meta = new Dictionary<string, object>
        {
            ["name"] = GoogleDriveConstants.BackupFileName,
            ["appProperties"] = new Dictionary<string, string>
            {
                ["contentHash"] = contentHash,
                ["device"] = deviceName.Length > 60 ? deviceName[..60] : deviceName
            }
        };
        if (existing is null) meta["parents"] = new[] { "appDataFolder" };

        var url = existing is null
            ? $"{UploadUrl}?uploadType=multipart&fields={FileFields}"
            : $"{UploadUrl}/{existing.Id}?uploadType=multipart&fields={FileFields}";
        var method = existing is null ? HttpMethod.Post : HttpMethod.Patch;
        var metaJson = JsonSerializer.Serialize(meta);

        // Content objects can't be re-sent, so build fresh on every attempt.
        using var resp = await SendAsync(() =>
        {
            var multi = new MultipartContent("related");
            multi.Add(new StringContent(metaJson, Encoding.UTF8, "application/json"));
            var bin = new ByteArrayContent(data);
            bin.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multi.Add(bin);
            return new HttpRequestMessage(method, url) { Content = multi };
        });

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return ParseFile(doc.RootElement);
    }

    public async Task<byte[]> DownloadAsync(string fileId)
    {
        using var resp = await SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, $"{FilesUrl}/{fileId}?alt=media"));
        return await resp.Content.ReadAsByteArrayAsync();
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build)
    {
        DriveGuard.EnsureOnline();
        for (var attempt = 0; ; attempt++)
        {
            var token = await _auth.GetValidAccessTokenAsync(forceRefresh: attempt > 0);
            using var req = build();
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage resp;
            try { resp = await _http.SendAsync(req); }
            catch (HttpRequestException ex)
            { throw new DriveBackupException(DriveErrorKind.Offline, "Couldn't reach Google Drive. Check your internet connection.", ex); }
            catch (TaskCanceledException ex)
            { throw new DriveBackupException(DriveErrorKind.Api, "Google Drive took too long to respond. Please try again.", ex); }

            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                resp.Dispose();   // token may have been revoked/expired early — refresh once and retry
                continue;
            }
            if (!resp.IsSuccessStatusCode)
            {
                var ex = await MapErrorAsync(resp);
                resp.Dispose();
                throw ex;
            }
            return resp;
        }
    }

    private static async Task<DriveBackupException> MapErrorAsync(HttpResponseMessage resp)
    {
        string? reason = null, message = null;
        try
        {
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var err = doc.RootElement.GetProperty("error");
            message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (err.TryGetProperty("errors", out var arr) && arr.GetArrayLength() > 0)
                reason = arr[0].TryGetProperty("reason", out var r) ? r.GetString() : null;
        }
        catch { /* non-JSON body */ }

        return (int)resp.StatusCode switch
        {
            401 => new(DriveErrorKind.AuthExpired, "Your Google session expired. Please connect again."),
            403 when reason == "storageQuotaExceeded" =>
                new(DriveErrorKind.QuotaExceeded, "Your Google Drive storage is full."),
            403 when reason is "rateLimitExceeded" or "userRateLimitExceeded" =>
                new(DriveErrorKind.Api, "Too many requests to Google Drive. Please wait a minute and retry."),
            403 => new(DriveErrorKind.PermissionDenied, "Google Drive access was denied. Please reconnect your account."),
            404 => new(DriveErrorKind.NoBackup, "The backup file was not found on Google Drive."),
            429 or >= 500 => new(DriveErrorKind.Api, "Google Drive is temporarily unavailable. Please try again later."),
            _ => new(DriveErrorKind.Api, $"Google Drive error: {message ?? resp.ReasonPhrase}")
        };
    }

    private static DriveFileInfo ParseFile(JsonElement e)
    {
        string? hash = null, device = null;
        if (e.TryGetProperty("appProperties", out var props))
        {
            hash = props.TryGetProperty("contentHash", out var h) ? h.GetString() : null;
            device = props.TryGetProperty("device", out var d) ? d.GetString() : null;
        }
        return new DriveFileInfo(
            e.GetProperty("id").GetString()!,
            DateTime.Parse(e.GetProperty("modifiedTime").GetString()!, null,
                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime(),
            e.TryGetProperty("size", out var s) && long.TryParse(s.GetString(), out var sz) ? sz : 0,
            hash, device);
    }
}
