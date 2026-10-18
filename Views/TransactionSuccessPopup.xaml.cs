using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace MoneySpend.Controls;

/// <summary>
/// Reusable success popup: Income / Expense / Borrow / Lend / EMI / Bill / Split,
/// Create aur Edit dono ke liye.
///
/// XAML (page ke root Grid me, sabse last):
///   xmlns:controls="clr-namespace:MoneySpend.Controls"
///   <controls:TransactionSuccessPopup x:Name="SuccessPopup" />
///
/// Code:
///   await SuccessPopup.ShowAsync(TransactionType.Income, 500m);
///   await SuccessPopup.ShowAsync(TransactionType.Emi, 3200m, TransactionMode.Edit);
/// </summary>
public partial class TransactionSuccessPopup : ContentView
{
    private static readonly CultureInfo InCulture = new("en-IN");
    private static readonly PathGeometryConverter GeoConverter = new();

    private TaskCompletionSource? _closeTcs;
    private bool _closing;

    public bool CloseOnBackdropTap { get; set; } = true;
    public event EventHandler? Closed;

    public TransactionSuccessPopup()
    {
        InitializeComponent();
        ZIndex = 999;
    }

    /// <summary>Popup dikhata hai; Task popup band hone par complete hota hai.</summary>
    /// <param name="autoCloseMs">0 = auto-close nahi.</param>
    public async Task ShowAsync(
        TransactionType type,
        decimal amount,
        TransactionMode mode = TransactionMode.Create,
        int autoCloseMs = 0,
        string? amountCaption = null,
        string? subtitle = null)
    {
        Apply(type, amount, mode, amountCaption, subtitle);

        _closing = false;
        _closeTcs = new TaskCompletionSource();
        Overlay.Opacity = 0;
        Card.Scale = 0.85;
        IsVisible = true;

        await Task.WhenAll(
            Overlay.FadeTo(1, 150),
            Card.ScaleTo(1, 220, Easing.SpringOut));

        if (autoCloseMs > 0)
            _ = AutoCloseAsync(autoCloseMs);

        await _closeTcs.Task;
    }

    public async Task CloseAsync()
    {
        if (_closing || !IsVisible) return;
        _closing = true;

        await Task.WhenAll(
            Overlay.FadeTo(0, 140),
            Card.ScaleTo(0.9, 140, Easing.CubicIn));

        IsVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
        _closeTcs?.TrySetResult();
    }

    private async Task AutoCloseAsync(int ms)
    {
        await Task.Delay(ms);
        await MainThread.InvokeOnMainThreadAsync(CloseAsync);
    }

    // ---------- type -> look ----------
    private void Apply(TransactionType type, decimal amount, TransactionMode mode,
                       string? amountCaption, string? subtitle)
    {
        var t = TransactionThemes.Get(type);
        var verb = mode == TransactionMode.Create ? "Added" : "Updated";

        TitleLabel.Text = $"{t.Title} {verb}!";
        SubtitleLabel.Text = subtitle ?? (mode == TransactionMode.Create
            ? "Your transaction has been saved\nsuccessfully."
            : "Your transaction has been updated\nsuccessfully.");

        IconRing.Fill = new SolidColorBrush(t.AccentSoft);
        IconCircle.Fill = new SolidColorBrush(t.Accent);
        MainIcon.Data = (Geometry)GeoConverter.ConvertFromInvariantString(t.IconPath)!;

        AmountBox.BackgroundColor = t.AccentBg;
        AmountBox.Stroke = new SolidColorBrush(t.AccentSoft);
        AmountIconBg.Fill = new SolidColorBrush(t.AccentSoft);
        AmountIcon.Data = (Geometry)GeoConverter.ConvertFromInvariantString(t.AmountIconPath)!;
        AmountIcon.Fill = new SolidColorBrush(t.Accent);

        AmountCaption.Text = amountCaption ?? (mode == TransactionMode.Create ? "Amount Added" : "Amount Updated");
        AmountLabel.TextColor = t.Accent;
        AmountLabel.Text = $"{t.AmountSign} ₹{amount.ToString("N2", InCulture)}";

        WaveBack.Fill = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(t.AccentSoft, 0f),
                new GradientStop(t.Accent.WithAlpha(0.5f), 1f)
            }, new Point(0, 0), new Point(1, 0));
        WaveFront.Fill = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(t.Accent.WithAlpha(0.55f), 0f),
                new GradientStop(t.AccentSoft, 1f)
            }, new Point(0, 0), new Point(1, 0));

        BuildConfetti(t.Accent);
    }

    private void BuildConfetti(Color accent)
    {
        ConfettiLayer.Children.Clear();
        var colors = new[] { accent, Color.FromArgb("#F97316"), Color.FromArgb("#3B82F6"), Color.FromArgb("#22C55E") };

        (double x, double y, double rot)[] spots =
        {
            (0.16, 0.28, 35), (0.24, 0.70, -20), (0.30, 0.12, 60), (0.12, 0.55, 10),
            (0.86, 0.26, -40), (0.78, 0.66, 25), (0.70, 0.10, 50), (0.90, 0.60, -15),
        };

        for (int i = 0; i < spots.Length; i++)
        {
            var piece = new BoxView
            {
                Color = colors[i % colors.Length],
                CornerRadius = 1.5,
                WidthRequest = 4,
                HeightRequest = i % 2 == 0 ? 10 : 5,
                Rotation = spots[i].rot
            };
            AbsoluteLayout.SetLayoutFlags(piece, AbsoluteLayoutFlags.PositionProportional);
            AbsoluteLayout.SetLayoutBounds(piece, new Rect(spots[i].x, spots[i].y, -1, -1));
            ConfettiLayer.Children.Add(piece);
        }
    }

    // ---------- gestures ----------
    private async void OnCloseTapped(object? s, TappedEventArgs e) => await CloseAsync();
    private async void OnBackdropTapped(object? s, TappedEventArgs e)
    {
        if (CloseOnBackdropTap) await CloseAsync();
    }
    private void OnCardTapped(object? s, TappedEventArgs e) { }
}
