using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Services;

namespace waterfall.UI.Controls;

/// <summary>
/// Theme-aware pill toggle switch built with MAUI Borders so Dark, AMOLED, and Light palettes
/// update immediately on Windows (WinUI) and Android without native ToggleSwitch VSM caching glitches.
/// </summary>
public sealed class ToggleSwitchControl : ContentView
{
    private const double TrackWidth = 44;
    private const double TrackHeight = 24;
    private const double ThumbSize = 18;
    private const double ThumbOffX = 2;
    private const double ThumbOnX = TrackWidth - ThumbSize - 4;

    public static readonly BindableProperty IsToggledProperty =
        BindableProperty.Create(
            nameof(IsToggled),
            typeof(bool),
            typeof(ToggleSwitchControl),
            false,
            BindingMode.TwoWay,
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((ToggleSwitchControl)bindable).OnIsToggledChanged((bool)newValue));

    private readonly Border _track;
    private readonly Border _thumb;

    public event EventHandler<ToggledEventArgs>? Toggled;

    public ToggleSwitchControl()
    {
        _thumb = new Border
        {
            WidthRequest = ThumbSize,
            HeightRequest = ThumbSize,
            StrokeShape = new RoundRectangle { CornerRadius = ThumbSize / 2 },
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            TranslationX = ThumbOffX
        };

        _track = new Border
        {
            WidthRequest = TrackWidth,
            HeightRequest = TrackHeight,
            StrokeShape = new RoundRectangle { CornerRadius = TrackHeight / 2 },
            StrokeThickness = 1,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Content = _thumb
        };

        TapGestureRecognizer tap = new();
        tap.Tapped += (_, _) => IsToggled = !IsToggled;
        _track.GestureRecognizers.Add(tap);

        Content = _track;
        ApplyVisuals(animate: false);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public bool IsToggled
    {
        get => (bool)GetValue(IsToggledProperty);
        set => SetValue(IsToggledProperty, value);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        ThemeService.PaletteChanged -= OnPaletteChanged;
        ThemeService.PaletteChanged += OnPaletteChanged;
        ApplyVisuals(animate: false);
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        ThemeService.PaletteChanged -= OnPaletteChanged;
    }

    private void OnPaletteChanged(object? sender, EventArgs e)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => ApplyVisuals(animate: false));
            return;
        }

        ApplyVisuals(animate: false);
    }

    private void OnIsToggledChanged(bool value)
    {
        ApplyVisuals(animate: IsLoaded);
        Toggled?.Invoke(this, new ToggledEventArgs(value));
    }

    private void ApplyVisuals(bool animate)
    {
        Color primary = GetThemeColor("Primary", "#D4745E");
        Color onPrimary = GetThemeColor("OnPrimary", "#FFFFFF");
        Color tonalSurface = GetThemeColor("TonalSurface", "#2C2926");
        Color border = GetThemeColor("Secondary", "#34302C");
        Color textSecondary = GetThemeColor("TextSecondary", "#ABA49A");

        bool on = IsToggled;
        _track.Background = new SolidColorBrush(on ? primary : tonalSurface);
        _track.Stroke = new SolidColorBrush(on ? primary : border);
        _thumb.Background = new SolidColorBrush(on ? onPrimary : textSecondary);

        double targetX = on ? ThumbOnX : ThumbOffX;
        if (animate)
        {
            _thumb.CancelAnimations();
            _ = _thumb.TranslateToAsync(targetX, 0, 120, Easing.CubicOut);
        }
        else
        {
            _thumb.TranslationX = targetX;
        }
    }

    private static Color GetThemeColor(string key, string fallbackHex) =>
        Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is Color color
            ? color
            : Color.FromArgb(fallbackHex);
}
