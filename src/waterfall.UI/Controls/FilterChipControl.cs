using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Services;

namespace waterfall.UI.Controls;

/// <summary>
/// Theme-aware borderless tonal pill chip (90/8/2) built with MAUI Border + Label
/// so live Dark / AMOLED / Light theme switches never desync DataTrigger DynamicResource setters
/// or leak native WinUI Button VSM hover highlights.
/// </summary>
public sealed class FilterChipControl : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(FilterChipControl), string.Empty);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(FilterChipControl));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(FilterChipControl));

    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.Create(
            nameof(IsActive),
            typeof(bool),
            typeof(FilterChipControl),
            false,
            propertyChanged: static (bindable, _, _) => ((FilterChipControl)bindable).ApplyVisuals());

    public static readonly BindableProperty IsAccentTextProperty =
        BindableProperty.Create(
            nameof(IsAccentText),
            typeof(bool),
            typeof(FilterChipControl),
            false,
            propertyChanged: static (bindable, _, _) => ((FilterChipControl)bindable).ApplyVisuals());

    private readonly Border _pill;
    private readonly Label _label;

    public FilterChipControl()
    {
        _label = new Label
        {
            FontFamily = "OpenSansSemibold",
            FontSize = 11.5,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            BackgroundColor = Colors.Transparent
        };
        _label.SetBinding(Label.TextProperty, static (FilterChipControl v) => v.Text, source: this);

        _pill = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            StrokeThickness = 0,
            Padding = new Thickness(12, 0),
            HeightRequest = 30,
            MinimumHeightRequest = 30,
            VerticalOptions = LayoutOptions.Center,
            Content = _label
        };

        TapGestureRecognizer tap = new();
        tap.SetBinding(TapGestureRecognizer.CommandProperty, static (FilterChipControl v) => v.Command, source: this);
        tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, static (FilterChipControl v) => v.CommandParameter, source: this);
        _pill.GestureRecognizers.Add(tap);

        Content = _pill;
        ApplyVisuals();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool IsAccentText
    {
        get => (bool)GetValue(IsAccentTextProperty);
        set => SetValue(IsAccentTextProperty, value);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        ThemeService.PaletteChanged -= OnPaletteChanged;
        ThemeService.PaletteChanged += OnPaletteChanged;
        ApplyVisuals();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        ThemeService.PaletteChanged -= OnPaletteChanged;
    }

    private void OnPaletteChanged(object? sender, EventArgs e)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(ApplyVisuals);
            return;
        }

        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        if (IsActive)
        {
            Color inkBg = GetThemeColor("InkPillBg", "#ECE7DF");
            Color inkText = GetThemeColor("InkPillText", "#191817");
            _pill.Background = new SolidColorBrush(inkBg);
            _label.TextColor = inkText;
        }
        else
        {
            Color tonalBg = GetThemeColor("TonalSurface", "#2C2926");
            Color textColor = IsAccentText
                ? GetThemeColor("Primary", "#D4745E")
                : GetThemeColor("TextSecondary", "#ABA49A");
            _pill.Background = new SolidColorBrush(tonalBg);
            _label.TextColor = textColor;
        }
    }

    private static Color GetThemeColor(string key, string fallbackHex) =>
        Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is Color color
            ? color
            : Color.FromArgb(fallbackHex);
}
