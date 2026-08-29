using Microsoft.Maui.Controls;

namespace Arthiva.Views.Components;

public partial class SummaryCardView : ContentView
{
    public static readonly BindableProperty CardTitleProperty =
        BindableProperty.Create(
            nameof(CardTitle),
            typeof(string),
            typeof(SummaryCardView),
            string.Empty);

    public static readonly BindableProperty CardAmountProperty =
        BindableProperty.Create(
            nameof(CardAmount),
            typeof(string),
            typeof(SummaryCardView),
            string.Empty);

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(
            nameof(AccentColor),
            typeof(Color),
            typeof(SummaryCardView),
            Colors.Transparent);

    public string CardTitle
    {
        get => (string)GetValue(CardTitleProperty);
        set => SetValue(CardTitleProperty, value);
    }

    public string CardAmount
    {
        get => (string)GetValue(CardAmountProperty);
        set => SetValue(CardAmountProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public SummaryCardView()
    {
        InitializeComponent();
    }
}