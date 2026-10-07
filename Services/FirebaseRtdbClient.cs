using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MoneySpend.Data;

namespace MoneySpend.Services;

public enum FirebaseErrorKind
{
    Offline,            // no network / timeout   → safe to queue + retry later
    Unauthorized,       // not logged in
    PermissionDenied,   // rules rejected it (also what RTDB returns as 401)
    PreconditionFailed, // conditional write lost (HTTP 412)
    Server,             // 5xx / 429
    Other
}

public sealed class FirebaseRtdbException : Exception
{
    public FirebaseErrorKind Kind { get; }
    public HttpStatusCode? StatusCode { get; }

    public FirebaseRtdbException(FirebaseErrorKind kind, string message,
        HttpStatusCode? status = null, Exception? inner = null) : base(message, inner)
    {
        Kind = kind;
        StatusCode = status;
    }
}

public interface IFirebaseRtdbClient
{
    Task<T?> GetAsync<T>(string path);
    Task PutAsync(string path, object? value);
    Task PatchAsync(string path, object value);
    Task DeleteAsync(string path);

    /// <summary>Atomic multi-path update from the DB root. null value = delete that path.</summary>
    Task UpdateAsync(IDictionary<string, object?> multiPath);

    /// <summary>PUT only if nothing exists there (if-match: null_etag). false = already taken.</summary>
    Task<bool> PutIfAbsentAsync(string path, object value);
}

/// <summary>
/// Minimal authenticated wrapper over the Realtime Database REST API.
/// Handles token fetch, one forced-refresh retry on 401, and maps failures to
/// FirebaseRtdbException so callers (and the later outbox) can tell
/// "offline" from "denied" from "lost a race".
/// </summary>
public class FirebaseRtdbClient : IFirebaseRtdbClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Use as a property value: Firebase replaces it with server time (ms).</summary>
    public static readonly object ServerTimestamp = new Dictionary<string, string> { [".sv"] = "timestamp" };

    private readonly IFirebaseAuthService _auth;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public FirebaseRtdbClient(IFirebaseAuthService auth) => _auth = auth;

    public async Task<T?> GetAsync<T>(string path)
    {
        using var resp = await SendAsync(t => new HttpRequestMessage(HttpMethod.Get, BuildUrl(path, t, false)));
        var body = await resp.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body) || body == "null") return default;
        return JsonSerializer.Deserialize<T>(body, Json);
    }

    public async Task PutAsync(string path, object? value)
    {
        var json = JsonSerializer.Serialize(value, Json);
        using var _ = await SendAsync(t => new HttpRequestMessage(HttpMethod.Put, BuildUrl(path, t, true))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    public async Task PatchAsync(string path, object value)
    {
        var json = JsonSerializer.Serialize(value, Json);
        using var _ = await SendAsync(t => new HttpRequestMessage(HttpMethod.Patch, BuildUrl(path, t, true))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    public async Task DeleteAsync(string path)
    {
        using var _ = await SendAsync(t => new HttpRequestMessage(HttpMethod.Delete, BuildUrl(path, t, true)));
    }

    public async Task UpdateAsync(IDictionary<string, object?> multiPath)
    {
        var json = SerializeMultiPath(multiPath);
        using var _ = await SendAsync(t => new HttpRequestMessage(HttpMethod.Patch, BuildUrl("", t, true))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    public async Task<bool> PutIfAbsentAsync(string path, object value)
    {
        var json = JsonSerializer.Serialize(value, Json);
        try
        {
            using var _ = await SendAsync(t =>
            {
                var req = new HttpRequestMessage(HttpMethod.Put, BuildUrl(path, t, true))
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("if-match", "null_etag");
                return req;
            });
            return true;
        }
        catch (FirebaseRtdbException ex) when (ex.Kind == FirebaseErrorKind.PreconditionFailed)
        {
            return false;
        }
    }

    // ─────────────────────────────────────────────

    // NOTE: the ID token travels as ?auth= (what the existing code already does).
    // It can show up in proxy logs; the .NET exceptions we throw never include the URL.
    private static string BuildUrl(string path, string token, bool silent)
        => $"{FirebaseConstants.DatabaseUrl}/{path.Trim('/')}.json?auth={Uri.EscapeDataString(token)}"
           + (silent ? "&print=silent" : string.Empty);

    private static string SerializeMultiPath(IDictionary<string, object?> updates)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var (key, value) in updates)
            {
                w.WritePropertyName(key);
                if (value is null) w.WriteNullValue();
                else JsonSerializer.Serialize(w, value, value.GetType(), Json);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> build)
    {
        for (var attempt = 0; ; attempt++)
        {
            string? token;
            try { token = await _auth.GetValidIdTokenAsync(forceRefresh: attempt > 0); }
            catch (HttpRequestException ex)
            { throw new FirebaseRtdbException(FirebaseErrorKind.Offline, "Couldn't reach the server.", null, ex); }
            catch (TaskCanceledException ex)
            { throw new FirebaseRtdbException(FirebaseErrorKind.Offline, "The server took too long to respond.", null, ex); }

            if (string.IsNullOrEmpty(token))
                throw new FirebaseRtdbException(FirebaseErrorKind.Unauthorized, "Please login first.");

            using var req = build(token);

            HttpResponseMessage resp;
            try { resp = await _http.SendAsync(req); }
            catch (HttpRequestException ex)
            { throw new FirebaseRtdbException(FirebaseErrorKind.Offline, "Couldn't reach the server.", null, ex); }
            catch (TaskCanceledException ex)
            { throw new FirebaseRtdbException(FirebaseErrorKind.Offline, "The server took too long to respond.", null, ex); }

            // RTDB answers 401 both for an expired token and for a rules denial:
            // refresh once; if it is still 401 it's the rules.
            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                resp.Dispose();
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var code = resp.StatusCode;
                resp.Dispose();
                throw (int)code switch
                {
                    412 => new FirebaseRtdbException(FirebaseErrorKind.PreconditionFailed, "Conflict.", code),
                    401 or 403 => new FirebaseRtdbException(FirebaseErrorKind.PermissionDenied, "Permission denied.", code),
                    429 or >= 500 => new FirebaseRtdbException(FirebaseErrorKind.Server, "Server is temporarily unavailable.", code),
                    _ => new FirebaseRtdbException(FirebaseErrorKind.Other, $"Request failed ({(int)code}).", code)
                };
            }

            return resp;
        }
    }
}
