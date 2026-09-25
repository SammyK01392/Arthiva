namespace Arthiva.Controls;

/// <summary>
/// Self-contained 4-digit PIN entry control: dot indicators + a custom
/// numeric keypad. Deliberately keeps the entered PIN only in a private
/// field (never bound to a public string property, never put in a Label,
/// never logged) so it can't accidentally leak into bindings, breakpoints
/// left in release builds, or view-model state that outlives the page.
/// </summary>
public partial class PinPadView : ContentView
{
    public const int PinLength = 4;

    private string _pin = string.Empty;

    /// <summary>Raised on every digit/backspace change with the PIN entered so far.</summary>
    public event EventHandler<string>? PinChanged;

    public PinPadView()
    {
        InitializeComponent();
        RenderDots();
    }

    /// <summary>Clears the entered PIN and resets the dot indicators.</summary>
    public void Clear()
    {
        _pin = string.Empty;
        RenderDots();
    }

    private void OnDigitTapped(object? sender, TappedEventArgs e)
    {
        if (_pin.Length >= PinLength)
            return;

        if (sender is TapGestureRecognizer { CommandParameter: string digit })
        {
            _pin += digit;
            RenderDots();
            PinChanged?.Invoke(this, _pin);
        }
    }

    private void OnBackspaceTapped(object? sender, TappedEventArgs e)
    {
        if (_pin.Length == 0)
            return;

        _pin = _pin[..^1];
        RenderDots();
        PinChanged?.Invoke(this, _pin);
    }

    private void RenderDots()
    {
        SetDot(Dot1, _pin.Length > 0);
        SetDot(Dot2, _pin.Length > 1);
        SetDot(Dot3, _pin.Length > 2);
        SetDot(Dot4, _pin.Length > 3);
    }

    private static void SetDot(Border dot, bool filled)
        => dot.BackgroundColor = filled ? Color.FromArgb("#FF6B00") : Colors.Transparent;
}
