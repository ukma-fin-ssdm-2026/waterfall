using Microsoft.Maui.Controls.Shapes;

namespace waterfall.UI.Controls;

/// <summary>
/// Anchored popup overlay container with tap-outside dismissal and themed Warm Editorial card chrome,
/// shared by the sort menu and overflow (⋮) menu layers.
/// </summary>
[ContentProperty(nameof(MenuContent))]
public sealed class AnchoredPopupMenu : ContentView
{
    public static readonly BindableProperty MenuMarginProperty =
        BindableProperty.Create(
            nameof(MenuMargin),
            typeof(Thickness),
            typeof(AnchoredPopupMenu),
            new Thickness(0, 4, 12, 0),
            propertyChanged: static (bindable, _, newValue) =>
                ((AnchoredPopupMenu)bindable)._card.Margin = (Thickness)newValue);

    public static readonly BindableProperty MenuWidthProperty =
        BindableProperty.Create(
            nameof(MenuWidth),
            typeof(double),
            typeof(AnchoredPopupMenu),
            205d,
            propertyChanged: static (bindable, _, newValue) =>
                ((AnchoredPopupMenu)bindable)._card.WidthRequest = (double)newValue);

    public static readonly BindableProperty MenuContentProperty =
        BindableProperty.Create(
            nameof(MenuContent),
            typeof(View),
            typeof(AnchoredPopupMenu),
            propertyChanged: static (bindable, _, newValue) =>
                ((AnchoredPopupMenu)bindable)._card.Content = (View?)newValue);

    private readonly Border _card;

    public AnchoredPopupMenu()
    {
        IsVisible = false;

        BoxView dismissBackdrop = new() { BackgroundColor = Colors.Transparent };
        TapGestureRecognizer dismissTap = new();
        dismissTap.Tapped += (_, _) => IsVisible = false;
        dismissBackdrop.GestureRecognizers.Add(dismissTap);

        _card = new Border
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = MenuMargin,
            WidthRequest = MenuWidth,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            StrokeThickness = 1,
            Padding = 6,
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 6),
                Radius = 18,
                Opacity = 0.32f
            }
        };
        _card.SetDynamicResource(Border.StrokeProperty, "CardBorder");
        _card.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");

        Content = new Grid
        {
            Children = { dismissBackdrop, _card }
        };
    }

    public Thickness MenuMargin
    {
        get => (Thickness)GetValue(MenuMarginProperty);
        set => SetValue(MenuMarginProperty, value);
    }

    public double MenuWidth
    {
        get => (double)GetValue(MenuWidthProperty);
        set => SetValue(MenuWidthProperty, value);
    }

    public View? MenuContent
    {
        get => (View?)GetValue(MenuContentProperty);
        set => SetValue(MenuContentProperty, value);
    }

    /// <summary>Hides the menu if currently visible and returns whether it handled dismissal.</summary>
    public bool DismissIfOpen()
    {
        if (!IsVisible)
        {
            return false;
        }

        IsVisible = false;
        return true;
    }
}
