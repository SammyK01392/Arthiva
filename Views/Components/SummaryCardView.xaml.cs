using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace MoneySpend.Views.Components;

public partial class SummaryCardView : ContentView
{
    // Content
    public static readonly BindableProperty CardTitleProperty =
        BindableProperty.Create(nameof(CardTitle), typeof(string), typeof(SummaryCardView), string.Empty);
    public static readonly BindableProperty CardAmountProperty =
        BindableProperty.Create(nameof(CardAmount), typeof(string), typeof(SummaryCardView), string.Empty);
    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(SummaryCardView), Colors.Transparent);
    public static readonly BindableProperty CardCaptionProperty =
        BindableProperty.Create(nameof(CardCaption), typeof(string), typeof(SummaryCardView), string.Empty);
    public static readonly BindableProperty IconSourceProperty =
        BindableProperty.Create(nameof(IconSource), typeof(string), typeof(SummaryCardView), string.Empty);
    public static readonly BindableProperty IconBackgroundColorProperty =
        BindableProperty.Create(nameof(IconBackgroundColor), typeof(Color), typeof(SummaryCardView), Colors.Transparent);
    public static readonly BindableProperty ShowDotProperty =
        BindableProperty.Create(nameof(ShowDot), typeof(bool), typeof(SummaryCardView), false);
    public static readonly BindableProperty DotColorProperty =
        BindableProperty.Create(nameof(DotColor), typeof(Color), typeof(SummaryCardView), Colors.Transparent);

    // Adjustable size / look
    public static readonly BindableProperty CardPaddingProperty =
        BindableProperty.Create(nameof(CardPadding), typeof(Thickness), typeof(SummaryCardView), new Thickness(12));
    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(SummaryCardView), 36d);
    public static readonly BindableProperty IconImageSizeProperty =
        BindableProperty.Create(nameof(IconImageSize), typeof(double), typeof(SummaryCardView), 16d);
    public static readonly BindableProperty TitleFontSizeProperty =
        BindableProperty.Create(nameof(TitleFontSize), typeof(double), typeof(SummaryCardView), 11.5d);
    public static readonly BindableProperty AmountFontSizeProperty =
        BindableProperty.Create(nameof(AmountFontSize), typeof(double), typeof(SummaryCardView), 16d);
    public static readonly BindableProperty CaptionFontSizeProperty =
        BindableProperty.Create(nameof(CaptionFontSize), typeof(double), typeof(SummaryCardView), 10d);
    public static readonly BindableProperty CardCornerRadiusProperty =
        BindableProperty.Create(nameof(CardCornerRadius), typeof(double), typeof(SummaryCardView), 18d,
            propertyChanged: (b, _, n) => ((SummaryCardView)b).ApplyCorner((double)n));

    public string CardTitle { get => (string)GetValue(CardTitleProperty); set => SetValue(CardTitleProperty, value); }
    public string CardAmount { get => (string)GetValue(CardAmountProperty); set => SetValue(CardAmountProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public string CardCaption { get => (string)GetValue(CardCaptionProperty); set => SetValue(CardCaptionProperty, value); }
    public string IconSource { get => (string)GetValue(IconSourceProperty); set => SetValue(IconSourceProperty, value); }
    public Color IconBackgroundColor { get => (Color)GetValue(IconBackgroundColorProperty); set => SetValue(IconBackgroundColorProperty, value); }
    public bool ShowDot { get => (bool)GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }
    public Color DotColor { get => (Color)GetValue(DotColorProperty); set => SetValue(DotColorProperty, value); }

    public Thickness CardPadding { get => (Thickness)GetValue(CardPaddingProperty); set => SetValue(CardPaddingProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public double IconImageSize { get => (double)GetValue(IconImageSizeProperty); set => SetValue(IconImageSizeProperty, value); }
    public double TitleFontSize { get => (double)GetValue(TitleFontSizeProperty); set => SetValue(TitleFontSizeProperty, value); }
    public double AmountFontSize { get => (double)GetValue(AmountFontSizeProperty); set => SetValue(AmountFontSizeProperty, value); }
    public double CaptionFontSize { get => (double)GetValue(CaptionFontSizeProperty); set => SetValue(CaptionFontSizeProperty, value); }
    public double CardCornerRadius { get => (double)GetValue(CardCornerRadiusProperty); set => SetValue(CardCornerRadiusProperty, value); }

    public SummaryCardView()
    {
        InitializeComponent();
        ApplyCorner(CardCornerRadius);
    }

    private void ApplyCorner(double radius)
        => CardBorder.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(radius) };
}