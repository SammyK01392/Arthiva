using System;
using System.Collections.Generic;
using System.Text;

using System.Security.Cryptography;
using System.Text.Json;
using MoneySpend.Data;

namespace MoneySpend.Services;

public interface IGoogleAuthService
{
    Task<GoogleAccount> SignInAsync();
    Task SignOutAsync();
    Task<bool> IsSignedInAsync();
    Task<string?> GetAccountEmailAsync();
    Task<string> GetValidAccessTokenAsync(bool forceRefresh = false);
}

public class GoogleAuthService : IGoogleAuthService
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    private const string KeyAccess = "google_access_token";
    private const string KeyRefresh = "google_refresh_token";
    private const string KeyExpires = "google_token_expires_at";
    private const string KeyEmail = "google_account_email";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private sealed record TokenResponse(string AccessToken, string? RefreshToken, int ExpiresIn, string? IdToken, string? Scope);

    public async Task<GoogleAccount> SignInAsync()
    {
        DriveGuard.EnsureOnline();

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var url = $"{AuthEndpoint}" +
                  $"?client_id={Uri.EscapeDataString(GoogleDriveConstants.AndroidClientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(GoogleDriveConstants.RedirectUri)}" +
                  $"&response_type=code" +
                  $"&scope={Uri.EscapeDataString(GoogleDriveConstants.Scopes)}" +
                  $"&code_challenge={challenge}&code_challenge_method=S256" +
                  $"&state={state}&access_type=offline&prompt=consent%20select_account";

        WebAuthenticatorResult result;
        try
        {
            result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(url),
                CallbackUrl = new Uri(GoogleDriveConstants.RedirectUri)
            });
        }
        catch (OperationCanceledException)
        {
            throw new DriveBackupException(DriveErrorKind.Cancelled, "Sign-in was cancelled.");
        }

        if (result.Properties.TryGetValue("error", out var err))
            throw new DriveBackupException(
                err == "access_denied" ? DriveErrorKind.Cancelled : DriveErrorKind.Api,
                err == "access_denied" ? "Sign-in was cancelled." : $"Google sign-in failed ({err}).");

        if (!result.Properties.TryGetValue("state", out var returnedState) || returnedState != state)
            throw new DriveBackupException(DriveErrorKind.Api, "Sign-in response could not be verified. Please try again.");

        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            throw new DriveBackupException(DriveErrorKind.Api, "Google did not return an authorization code.");

        var tokens = await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = GoogleDriveConstants.AndroidClientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = GoogleDriveConstants.RedirectUri,
            ["grant_type"] = "authorization_code"
        });

        // The consent screen lets users untick individual permissions.
        if (tokens.Scope is null || !tokens.Scope.Contains("drive.appdata"))
            throw new DriveBackupException(DriveErrorKind.PermissionDenied,
                "Google Drive permission was not granted. Please allow access to continue.");

        if (string.IsNullOrEmpty(tokens.RefreshToken))
            throw new DriveBackupException(DriveErrorKind.Api,
                "Google did not return a refresh token. Remove MoneySpend from your Google account's connected apps and try again.");

        var (email, name) = ParseIdToken(tokens.IdToken);
        await SecureStorage.SetAsync(KeyAccess, tokens.AccessToken);
        await SecureStorage.SetAsync(KeyRefresh, tokens.RefreshToken);
        await SecureStorage.SetAsync(KeyExpires, ExpiryFrom(tokens.ExpiresIn).ToString());
        await SecureStorage.SetAsync(KeyEmail, email ?? "Google account");
        return new GoogleAccount(email ?? "Google account", name);
    }

    public async Task<string> GetValidAccessTokenAsync(bool forceRefresh = false)
    {
        await _refreshLock.WaitAsync();
        try
        {
            var access = await SecureStorage.GetAsync(KeyAccess);
            var refresh = await SecureStorage.GetAsync(KeyRefresh);
            if (string.IsNullOrEmpty(refresh))
                throw new DriveBackupException(DriveErrorKind.NotSignedIn, "Please connect your Google account first.");

            var expiresOk = long.TryParse(await SecureStorage.GetAsync(KeyExpires), out var exp)
                            && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < exp;
            if (!forceRefresh && !string.IsNullOrEmpty(access) && expiresOk)
                return access;

            try
            {
                var t = await PostTokenAsync(new Dictionary<string, string>
                {
                    ["client_id"] = GoogleDriveConstants.AndroidClientId,
                    ["refresh_token"] = refresh,
                    ["grant_type"] = "refresh_token"
                });
                await SecureStorage.SetAsync(KeyAccess, t.AccessToken);
                await SecureStorage.SetAsync(KeyExpires, ExpiryFrom(t.ExpiresIn).ToString());
                return t.AccessToken;
            }
            catch (DriveBackupException ex) when (ex.Kind == DriveErrorKind.AuthExpired)
            {
                ClearTokens(); // revoked or expired — user must sign in again
                throw;
            }
        }
        finally { _refreshLock.Release(); }
    }

    public async Task<bool> IsSignedInAsync()
        => !string.IsNullOrEmpty(await SecureStorage.GetAsync(KeyRefresh));

    public Task<string?> GetAccountEmailAsync() => SecureStorage.GetAsync(KeyEmail);

    public async Task SignOutAsync()
    {
        try
        {
            var refresh = await SecureStorage.GetAsync(KeyRefresh);
            if (!string.IsNullOrEmpty(refresh) && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                await _http.PostAsync(RevokeEndpoint,
                    new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh }));
        }
        catch { /* best effort — local sign-out below always happens */ }
        ClearTokens();
    }

    private static void ClearTokens()
    {
        SecureStorage.Remove(KeyAccess);
        SecureStorage.Remove(KeyRefresh);
        SecureStorage.Remove(KeyExpires);
        SecureStorage.Remove(KeyEmail);
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form)
    {
        HttpResponseMessage resp;
        try { resp = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form)); }
        catch (HttpRequestException ex)
        { throw new DriveBackupException(DriveErrorKind.Offline, "Couldn't reach Google. Check your internet connection.", ex); }
        catch (TaskCanceledException ex)
        { throw new DriveBackupException(DriveErrorKind.Api, "Google took too long to respond. Please try again.", ex); }

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!resp.IsSuccessStatusCode)
        {
            var error = root.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            throw error == "invalid_grant"
                ? new DriveBackupException(DriveErrorKind.AuthExpired, "Your Google session expired. Please connect again.")
                : new DriveBackupException(DriveErrorKind.Api, $"Google authentication failed ({error}).");
        }

        return new TokenResponse(
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
            root.TryGetProperty("expires_in", out var x) ? x.GetInt32() : 3600,
            root.TryGetProperty("id_token", out var i) ? i.GetString() : null,
            root.TryGetProperty("scope", out var s) ? s.GetString() : null);
    }

    private static long ExpiryFrom(int expiresIn)
        => DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60).ToUnixTimeSeconds();

    private static string Base64Url(byte[] b)
        => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (string? email, string? name) ParseIdToken(string? jwt)
    {
        try
        {
            var p = jwt!.Split('.')[1].Replace('-', '+').Replace('_', '/');
            p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
            using var d = JsonDocument.Parse(Convert.FromBase64String(p));
            return (d.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null,
                    d.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null);
        }
        catch { return (null, null); }
    }
}