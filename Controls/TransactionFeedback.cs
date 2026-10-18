using Plugin.Maui.Audio;

namespace MoneySpend.Controls;

/// <summary>
/// Success sound + haptic. Popup isko khud call karta hai, pages me kuch nahi karna.
///
/// Sound files (Build Action = MauiAsset, Resources/Raw me):
///   success_in.wav   -> paisa aaya  (theme ka AmountSign "+": Income, Lend)
///   success_out.wav  -> paisa gaya  (theme ka AmountSign "-": Expense, Borrow, EMI, Bill, Split)
/// Sign TransactionTheme se aata hai, isliye wahan sign badlo to sound apne aap badal jata hai.
///
/// Settings page me toggle: TransactionFeedback.SoundEnabled = true/false
/// </summary>
public static class TransactionFeedback
{
    private const string SoundPrefKey = "tx_success_sound_enabled";
    private const string HapticPrefKey = "tx_success_haptic_enabled";

    public static bool SoundEnabled
    {
        get => Preferences.Default.Get(SoundPrefKey, true);
        set => Preferences.Default.Set(SoundPrefKey, value);
    }

    public static bool HapticEnabled
    {
        get => Preferences.Default.Get(HapticPrefKey, true);
        set => Preferences.Default.Set(HapticPrefKey, value);
    }

    private static string FileFor(TransactionType type)
        => TransactionThemes.Get(type).AmountSign == "+"
            ? "success_in.wav"
            : "success_out.wav";

    /// <summary>Fire-and-forget: kabhi exception nahi fenkta, popup ko kabhi nahi rokta.</summary>
    public static void Play(TransactionType type)
    {
        if (HapticEnabled) TryHaptic();
        if (SoundEnabled) _ = PlaySoundAsync(FileFor(type));
    }

    private static void TryHaptic()
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch { /* device support nahi karta — ignore */ }
    }

    private static async Task PlaySoundAsync(string fileName)
    {
        try
        {
            var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
            var player = AudioManager.Current.CreatePlayer(stream);

            // Khatam hone par player aur stream free karo
            player.PlaybackEnded += (_, _) =>
            {
                player.Dispose();
                stream.Dispose();
            };

            player.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Success sound failed: {ex.Message}");
        }
    }
}
