using System.Net.Http.Headers;
using MoneySpend.Data;

namespace MoneySpend.Services;

public interface IRealtimeListenerService
{
    bool IsRunning { get; }

    /// <summary>Idempotent. Starts streaming while the app is in the foreground; exits by itself if nobody is logged in.</summary>
    void Start();

    /// <summary>Idempotent. Stops streaming (app in background, signed out).</summary>
    void Stop();

    /// <summary>Raised (debounced) when the remote request or connection index changed. Subscribers should reconcile.</summary>
    event Action? Changed;
}

/// <summary>
/// Streams two small per-user nodes with the Realtime Database REST streaming protocol (Server-Sent Events):
///   /userRequests/{uid}    requestId → last-changed timestamp (written for BOTH participants on every change)
///   /userConnections/{uid} connection requests / accepts (so an invite is picked up and auto-accepted live)
/// The payload is deliberately ignored: any put/patch just means "go reconcile", and reconcile is
/// idempotent, so a missed or duplicated event can never corrupt anything.
/// </summary>
public sealed class RealtimeListenerService : IRealtimeListenerService
{
    private static readonly string[] Nodes = { "userRequests", "userConnections" };

    // The server sends a keep-alive roughly every 30 s. Silence for 90 s = dead connection.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    private readonly IFirebaseAuthService _auth;

    // No overall timeout: the response is meant to stay open for a long time.
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private int _debouncing;

    public event Action? Changed;

    public RealtimeListenerService(IFirebaseAuthService auth) => _auth = auth;

    public bool IsRunning
    {
        get { lock (_gate) return _cts is not null; }
    }

    public void Start()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        foreach (var node in Nodes)
        {
            var path = node;
            _ = Task.Run(() => RunAsync(path, token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
        }

        if (cts is null) return;

        try { cts.Cancel(); }
        finally { cts.Dispose(); }
    }

    private async Task RunAsync(string node, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var uid = await _auth.GetUidAsync();
                if (string.IsNullOrEmpty(uid))
                {
                    // Not logged in: nothing to listen to. Start() is called again on sign-in.
                    Stop();
                    return;
                }

                var receivedEvents = await StreamOnceAsync(node, uid, ct);
                if (receivedEvents) backoff = TimeSpan.FromSeconds(1); // it worked, so reconnect quickly
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Offline, DNS, token refresh failure… all handled by retrying with backoff.
                CrashLogger.Log(ex, $"Realtime.Stream.{node}");
            }

            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { return; }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
        }
    }

    /// <summary>One connection. Returns true if at least one data event arrived.</summary>
    private async Task<bool> StreamOnceAsync(string node, string uid, CancellationToken ct)
    {
        var token = await _auth.GetValidIdTokenAsync();
        if (string.IsNullOrEmpty(token)) return false;

        var url = $"{FirebaseConstants.DatabaseUrl}/{node}/{uid}.json?auth={Uri.EscapeDataString(token)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            // 401 usually = expired token. Force a refresh so the next attempt has a fresh one.
            if ((int)response.StatusCode == 401)
                await _auth.GetValidIdTokenAsync(forceRefresh: true);
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var gotData = false;
        string? eventName = null;

        while (!ct.IsCancellationRequested)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(IdleTimeout);

            string? line;
            try
            {
                line = await reader.ReadLineAsync(idle.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return gotData; // idle timeout → reconnect
            }

            if (line is null) return gotData; // server closed the stream → reconnect

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line[6..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var name = eventName;
            eventName = null;

            switch (name)
            {
                case "put":
                case "patch":
                    gotData = true;
                    RaiseChanged();
                    break;

                case "cancel":        // rules denied the read
                case "auth_revoked":  // token expired → reconnect with a fresh one
                    return gotData;

                // "keep-alive": nothing to do, the idle timer is reset on every line.
            }
        }

        return gotData;
    }

    /// <summary>Coalesces bursts (initial snapshots + our own writes) into one notification.</summary>
    private void RaiseChanged()
    {
        if (Interlocked.Exchange(ref _debouncing, 1) == 1) return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(Debounce);
            Interlocked.Exchange(ref _debouncing, 0);

            try { Changed?.Invoke(); }
            catch (Exception ex) { CrashLogger.Log(ex, "Realtime.Changed"); }
        });
    }
}
