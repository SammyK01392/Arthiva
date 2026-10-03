using CommunityToolkit.Mvvm.Messaging;

namespace MoneySpend.Services;

public sealed record DataChangedMessage(Type EntityType);

public static class DataChangeNotifier
{
    public static void Publish<T>() =>
        WeakReferenceMessenger.Default.Send(new DataChangedMessage(typeof(T)));
}

/// ViewModel ke andar ek field ki tarah rakho. DB change aate hi
/// (burst ko 60ms mein ek reload bana ke) reload callback chalata hai.
public sealed class AutoRefresh : IRecipient<DataChangedMessage>
{
    private const int DebounceMs = 60;

    private readonly Func<Task> _reload;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private volatile bool _dirty;

    /// Pehli load ke baad true karo.
    public bool Enabled { get; set; }

    public AutoRefresh(Func<Task> reload)
    {
        _reload = reload;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public void Receive(DataChangedMessage message)
    {
        if (!Enabled) return;
        _dirty = true;
        _ = DebouncedAsync();
    }

    private async Task DebouncedAsync()
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _cts, cts)?.Cancel();
        try
        {
            await Task.Delay(DebounceMs, cts.Token);
            await RunAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunAsync()
    {
        await _gate.WaitAsync();
        try
        {
            while (_dirty)
            {
                _dirty = false;
                await MainThread.InvokeOnMainThreadAsync(_reload);
            }
        }
        catch (Exception ex) { CrashLogger.Log(ex, "AutoRefresh"); }
        finally { _gate.Release(); }
    }

    /// VM khud refresh maange (jaise tab/filter badalne par) — wahi debounce aur gate use hota hai
    public void Request()
    {
        if (!Enabled) return;
        _dirty = true;
        _ = DebouncedAsync();
    }
}