using System.Net;
using System.Text;
using System.Text.Json;
using MoneySpend.Data;
using MoneySpend.Models;

namespace MoneySpend.Services;

public class FirebaseAuthService : IFirebaseAuthService
{
    private const string SignUpUrl =
        $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={FirebaseConstants.WebApiKey}";
    private const string SignInUrl =
        $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FirebaseConstants.WebApiKey}";
    private const string RefreshUrl =
        $"https://securetoken.googleapis.com/v1/token?key={FirebaseConstants.WebApiKey}";
    private const string ResetPasswordUrl =
        $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseConstants.WebApiKey}";

    // SecureStorage keys (unchanged)
    private const string KeyIdToken = "firebase_id_token";
    private const string KeyRefreshToken = "firebase_refresh_token";
    private const string KeyUid = "firebase_uid";
    private const string KeyTokenExpiresAt = "firebase_token_expires_at";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // CHANGED: the realtime listener, the (later) outbox worker and normal UI
    // calls will all ask for a token at the same time. Without this lock they
    // would refresh concurrently and race on SecureStorage.
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public event EventHandler? SignedIn;
    public event EventHandler? SignedOut;

    public async Task<FirebaseAuthResult> SignUpAsync(string email, string password)
        => await PostAuthAsync(SignUpUrl, email, password);

    public async Task<FirebaseAuthResult> SignInAsync(string email, string password)
        => await PostAuthAsync(SignInUrl, email, password);

    public async Task SendPasswordResetAsync(string email)
    {
        var payload = new { requestType = "PASSWORD_RESET", email };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(ResetPasswordUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var errorCode = TryParseErrorMessage(body);
            throw new InvalidOperationException(MapFirebaseError(errorCode));
        }
    }

    private async Task<FirebaseAuthResult> PostAuthAsync(string url, string email, string password)
    {
        var payload = new { email, password, returnSecureToken = true };
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = TryParseErrorMessage(body);
            throw new InvalidOperationException(MapFirebaseError(errorMessage));
        }

        var result = JsonSerializer.Deserialize<FirebaseAuthResult>(body)
            ?? throw new InvalidOperationException("Unexpected response from server.");

        await _tokenLock.WaitAsync();
        try { await SaveSessionAsync(result); }
        finally { _tokenLock.Release(); }

        SignedIn?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private static string TryParseErrorMessage(string body)
    {
        try
        {
            var err = JsonSerializer.Deserialize<FirebaseErrorResponse>(body);
            return err?.Error?.Message ?? "UNKNOWN_ERROR";
        }
        catch
        {
            return "UNKNOWN_ERROR";
        }
    }

    // Firebase returns machine-readable codes like EMAIL_EXISTS, INVALID_PASSWORD —
    // translate the common ones into messages a user can actually act on.
    private static string MapFirebaseError(string code) => code switch
    {
        "EMAIL_EXISTS" => "Ye email pehle se registered hai. Login try karein.",
        "EMAIL_NOT_FOUND" => "Ye email registered nahi hai. Pehle sign up karein.",
        "INVALID_PASSWORD" or "INVALID_LOGIN_CREDENTIALS" => "Email ya password galat hai.",
        "WEAK_PASSWORD : Password should be at least 6 characters" => "Password kam se kam 6 characters ka hona chahiye.",
        _ when code.StartsWith("WEAK_PASSWORD") => "Password kam se kam 6 characters ka hona chahiye.",
        "TOO_MANY_ATTEMPTS_TRY_LATER" => "Bahut zyada attempts. Thodi der baad try karein.",
        _ => "Kuch galat ho gaya. Dubara try karein."
    };

    private async Task SaveSessionAsync(FirebaseAuthResult result)
    {
        var expiresInSeconds = int.TryParse(result.ExpiresIn, out var s) ? s : 3600;
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds - 60); // 60s safety margin

        await SecureStorage.SetAsync(KeyIdToken, result.IdToken);
        await SecureStorage.SetAsync(KeyRefreshToken, result.RefreshToken);
        await SecureStorage.SetAsync(KeyUid, result.Uid);
        await SecureStorage.SetAsync(KeyTokenExpiresAt, expiresAt.ToUnixTimeSeconds().ToString());
    }

    public async Task<string?> GetValidIdTokenAsync(bool forceRefresh = false)
    {
        await _tokenLock.WaitAsync();
        try
        {
            // Read inside the lock: a caller that waited may find the token
            // already refreshed by whoever held the lock before it.
            var idToken = await SecureStorage.GetAsync(KeyIdToken);
            var refreshToken = await SecureStorage.GetAsync(KeyRefreshToken);
            var expiresAtStr = await SecureStorage.GetAsync(KeyTokenExpiresAt);

            if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(refreshToken))
                return null;

            var expiresAt = long.TryParse(expiresAtStr, out var e)
                ? DateTimeOffset.FromUnixTimeSeconds(e)
                : DateTimeOffset.MinValue;

            if (!forceRefresh && DateTimeOffset.UtcNow < expiresAt)
                return idToken; // still valid

            // securetoken expects form-urlencoded (documented format).
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            });
            var response = await _http.PostAsync(RefreshUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                // CHANGED: only a definitive "this refresh token is dead"
                // (4xx other than 429) signs the user out. A 5xx / rate limit
                // is transient and must not log anyone out.
                var code = response.StatusCode;
                if (code is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    await SignOutAsync();
                    return null;
                }
                throw new HttpRequestException($"Token refresh failed ({(int)code}).");
            }

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var newIdToken = doc.RootElement.GetProperty("id_token").GetString() ?? string.Empty;
            var newRefreshToken = doc.RootElement.GetProperty("refresh_token").GetString() ?? string.Empty;
            var expiresIn = doc.RootElement.GetProperty("expires_in").GetString() ?? "3600";

            var newExpiresAt = DateTimeOffset.UtcNow.AddSeconds(int.Parse(expiresIn) - 60);
            await SecureStorage.SetAsync(KeyIdToken, newIdToken);
            await SecureStorage.SetAsync(KeyRefreshToken, newRefreshToken);
            await SecureStorage.SetAsync(KeyTokenExpiresAt, newExpiresAt.ToUnixTimeSeconds().ToString());

            return newIdToken;
        }
        finally { _tokenLock.Release(); }
    }

    public Task<string?> GetUidAsync() => SecureStorage.GetAsync(KeyUid);

    public Task<bool> IsLoggedInAsync()
        => SecureStorage.GetAsync(KeyUid).ContinueWith(t => !string.IsNullOrEmpty(t.Result));

    public Task SignOutAsync()
    {
        SecureStorage.Remove(KeyIdToken);
        SecureStorage.Remove(KeyRefreshToken);
        SecureStorage.Remove(KeyUid);
        SecureStorage.Remove(KeyTokenExpiresAt);
        SignedOut?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
