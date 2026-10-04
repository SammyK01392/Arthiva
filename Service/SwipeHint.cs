using Microsoft.Maui.Handlers;

namespace MoneySpend.Services;

public static class SwipeHint
{
    private const string CountKey = "swipe_hint_count"; // Settings isi key ko reset karta hai
    private const int MaxShows = 3;                      // sirf pehli 3 baar
    private static bool _doneThisSession;                // ek app-run mein ek hi baar

    public static void Register()
    {
        SwipeViewHandler.Mapper.AppendToMapping("SwipeHint", (handler, view) =>
        {
            if (_doneThisSession || view is not SwipeView sv) return;
            if (Preferences.Get(CountKey, 0) >= MaxShows) return;
            sv.Loaded += OnLoaded;
        });
    }

    private static async void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is not SwipeView sv) return;
        sv.Loaded -= OnLoaded;
        if (_doneThisSession) return;
        _doneThisSession = true;

        try
        {
            await Task.Delay(700); // layout settle hone do

            OpenSwipeItem? side =
                sv.RightItems?.Count > 0 ? OpenSwipeItem.RightItems :
                sv.LeftItems?.Count > 0 ? OpenSwipeItem.LeftItems : null;
            if (side is null) return;

            sv.Open(side.Value, true);
            await Task.Delay(900);
            sv.Close(true);

            Preferences.Set(CountKey, Preferences.Get(CountKey, 0) + 1);
        }
        catch { /* hint fail ho to app na ruke */ }
    }
}