using Microsoft.Maui.Controls;

namespace Arthiva.Views.Components;

public partial class SummaryCardView : ContentView
{
    public static readonly BindableProperty CardTitleProperty =
        BindableProperty.Create(nameof(CardTitle), typeof(string), typeof(SummaryCardView), string.Empty);

    public static readonly BindableProperty CardAmountProperty =
        BindableProperty.Create(nameof(CardAmount), typeof(string), typeof(SummaryCardView), string.Empty);

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(SummaryCardView), Colors.Transparent);

    public static readonly BindableProperty CardCaptionProperty =
        BindableProperty.Create(nameof(CardCaption), typeof(string), typeof(SummaryCardView), string.Empty);

    // NAYA: SVG Icon Source
    public static readonly BindableProperty IconSourceProperty =
        BindableProperty.Create(nameof(IconSource), typeof(string), typeof(SummaryCardView), string.Empty);

    public static readonly BindableProperty IconBackgroundColorProperty =
        BindableProperty.Create(nameof(IconBackgroundColor), typeof(Color), typeof(SummaryCardView), Colors.Transparent);

    // NAYA: transaction count ke aage coloured dot
    public static readonly BindableProperty ShowDotProperty =
        BindableProperty.Create(nameof(ShowDot), typeof(bool), typeof(SummaryCardView), false);

    public static readonly BindableProperty DotColorProperty =
        BindableProperty.Create(nameof(DotColor), typeof(Color), typeof(SummaryCardView), Colors.Transparent);

    public string CardTitle { get => (string)GetValue(CardTitleProperty); set => SetValue(CardTitleProperty, value); }
    public string CardAmount { get => (string)GetValue(CardAmountProperty); set => SetValue(CardAmountProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public string CardCaption { get => (string)GetValue(CardCaptionProperty); set => SetValue(CardCaptionProperty, value); }
    public string IconSource { get => (string)GetValue(IconSourceProperty); set => SetValue(IconSourceProperty, value); }
    public Color IconBackgroundColor { get => (Color)GetValue(IconBackgroundColorProperty); set => SetValue(IconBackgroundColorProperty, value); }
    public bool ShowDot { get => (bool)GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }
    public Color DotColor { get => (Color)GetValue(DotColorProperty); set => SetValue(DotColorProperty, value); }

    public SummaryCardView()
    {
        InitializeComponent();
    }
}