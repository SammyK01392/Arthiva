namespace Arthiva.Controls;

/// <summary>
/// Self-contained 4-digit PIN entry control.
/// Keeps the entered PIN private and only exposes changes through events.
/// </summary>
public partial class PinPadView : ContentView
{
    public const int PinLength = 4;

    private string _pin = string.Empty;

    /// <summary>
    /// Raised whenever user enters or removes a digit.
    /// </summary>
    public event EventHandler<string>? PinChanged;

    /// <summary>
    /// Raised automatically when all 4 digits are entered.
    /// </summary>
    public event EventHandler<string>? PinCompleted;


    public PinPadView()
    {
        InitializeComponent();

        RenderDots();
    }


    /// <summary>
    /// Clears the currently entered PIN.
    /// </summary>
    public void Clear()
    {
        _pin = string.Empty;

        RenderDots();

        PinChanged?.Invoke(this, _pin);
    }


    /// <summary>
    /// Handles numeric keypad tap.
    /// </summary>
    private void OnDigitTapped(object? sender, TappedEventArgs e)
    {
        // Only allow 4 digits
        if (_pin.Length >= PinLength)
            return;


        // CommandParameter comes through TappedEventArgs.Parameter
        var digit = e.Parameter?.ToString();

        if (string.IsNullOrWhiteSpace(digit))
            return;


        // Safety check: only numeric single digit
        if (digit.Length != 1 || !char.IsDigit(digit[0]))
            return;


        // Add digit
        _pin += digit;


        // Update indicator dots
        RenderDots();


        // Notify parent
        PinChanged?.Invoke(this, _pin);


        // PIN completed
        if (_pin.Length == PinLength)
        {
            PinCompleted?.Invoke(this, _pin);
        }
    }


    /// <summary>
    /// Removes the last entered digit.
    /// </summary>
    private void OnBackspaceTapped(object? sender, TappedEventArgs e)
    {
        if (_pin.Length == 0)
            return;


        // Remove last digit
        _pin = _pin[..^1];


        // Update dots
        RenderDots();


        // Notify parent
        PinChanged?.Invoke(this, _pin);
    }


    /// <summary>
    /// Updates PIN indicator dots.
    /// </summary>
    private void RenderDots()
    {
        SetDot(Dot1, _pin.Length >= 1);
        SetDot(Dot2, _pin.Length >= 2);
        SetDot(Dot3, _pin.Length >= 3);
        SetDot(Dot4, _pin.Length >= 4);
    }


    /// <summary>
    /// Fills or empties indicator dot.
    /// </summary>
    private static void SetDot(Border dot, bool filled)
    {
        dot.BackgroundColor = filled
            ? Color.FromArgb("#FF6B00")
            : Colors.Transparent;
    }
}