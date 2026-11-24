using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Resources;
using waterfall.UI.Services;

namespace waterfall.UI.Controls;

/// <summary>
/// Expandable parse-site configuration card with checkbox toggle, optional subtitle,
/// expand/collapse chevron, and slotted per-site filter controls.
/// </summary>
[ContentProperty(nameof(BodyContent))]
public sealed class CollapsibleSiteCard : ContentView
{
    public static readonly BindableProperty SiteNameProperty =
        BindableProperty.Create(nameof(SiteName), typeof(string), typeof(CollapsibleSiteCard), string.Empty);

    public static readonly BindableProperty SubtitleProperty =
        BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(CollapsibleSiteCard));

    public static readonly BindableProperty IsSiteEnabledProperty =
        BindableProperty.Create(
            nameof(IsSiteEnabled),
            typeof(bool),
            typeof(CollapsibleSiteCard),
            false,
            propertyChanged: static (bindable, _, newValue) =>
                ((CollapsibleSiteCard)bindable).UpdateEnabledVisuals((bool)newValue));

    public static readonly BindableProperty ToggleSiteCommandProperty =
        BindableProperty.Create(nameof(ToggleSiteCommand), typeof(ICommand), typeof(CollapsibleSiteCard));

    public static readonly BindableProperty IsExpandedProperty =
        BindableProperty.Create(
            nameof(IsExpanded),
            typeof(bool),
            typeof(CollapsibleSiteCard),
            false,
            propertyChanged: static (bindable, _, newValue) =>
                ((CollapsibleSiteCard)bindable).UpdateExpandedVisuals((bool)newValue));

    public static readonly BindableProperty ToggleExpandCommandProperty =
        BindableProperty.Create(nameof(ToggleExpandCommand), typeof(ICommand), typeof(CollapsibleSiteCard));

    public static readonly BindableProperty BodyContentProperty =
        BindableProperty.Create(
            nameof(BodyContent),
            typeof(View),
            typeof(CollapsibleSiteCard),
            propertyChanged: static (bindable, _, newValue) =>
                ((CollapsibleSiteCard)bindable)._bodyHost.Content = (View?)newValue);

    private readonly Border _cardBorder;
    private readonly Border _checkboxBorder;
    private readonly Label _checkGlyph;
    private readonly Button _expandButton;
    private readonly ContentView _bodyHost;

    public CollapsibleSiteCard()
    {
        _checkGlyph = new Label
        {
            Text = Icons.Check,
            FontFamily = Icons.FontFamily,
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };
        _checkGlyph.SetDynamicResource(Label.TextColorProperty, "OnPrimary");

        _checkboxBorder = new Border
        {
            WidthRequest = 20,
            HeightRequest = 20,
            StrokeShape = new RoundRectangle { CornerRadius = 5 },
            StrokeThickness = 1.5,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            Content = _checkGlyph
        };
        _checkboxBorder.SetDynamicResource(Border.StrokeProperty, "Secondary");
        TapGestureRecognizer checkTap = new();
        checkTap.SetBinding(TapGestureRecognizer.CommandProperty, static (CollapsibleSiteCard v) => v.ToggleSiteCommand, source: this);
        _checkboxBorder.GestureRecognizers.Add(checkTap);

        Label titleLabel = new()
        {
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "White");
        titleLabel.SetBinding(Label.TextProperty, static (CollapsibleSiteCard v) => v.SiteName, source: this);

        Label subtitleLabel = new()
        {
            FontSize = 11,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };
        subtitleLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");
        subtitleLabel.SetBinding(Label.TextProperty, static (CollapsibleSiteCard v) => v.Subtitle, source: this);

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Subtitle))
            {
                subtitleLabel.IsVisible = !string.IsNullOrEmpty(Subtitle);
            }
        };

        HorizontalStackLayout titleGroup = new()
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, subtitleLabel }
        };
        TapGestureRecognizer titleTap = new();
        titleTap.SetBinding(TapGestureRecognizer.CommandProperty, static (CollapsibleSiteCard v) => v.ToggleSiteCommand, source: this);
        titleGroup.GestureRecognizers.Add(titleTap);

        _expandButton = new Button
        {
            Text = Icons.ChevronDown,
            WidthRequest = 30,
            HeightRequest = 30,
            MinimumWidthRequest = 30,
            MinimumHeightRequest = 30
        };
        MountedPageHelper.ApplyStyle(_expandButton, "GhostIconButton");
        _expandButton.SetBinding(Button.CommandProperty, static (CollapsibleSiteCard v) => v.ToggleExpandCommand, source: this);

        Grid headerGrid = new()
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(_checkboxBorder, 0);
        Grid.SetColumn(titleGroup, 1);
        Grid.SetColumn(_expandButton, 2);
        headerGrid.Children.Add(_checkboxBorder);
        headerGrid.Children.Add(titleGroup);
        headerGrid.Children.Add(_expandButton);

        _bodyHost = new ContentView { IsVisible = false };

        _cardBorder = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 11 },
            StrokeThickness = 1,
            Padding = new Thickness(11, 9),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children = { headerGrid, _bodyHost }
            }
        };

        Content = _cardBorder;
        ApplyThemeAndState();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        ThemeService.PaletteChanged -= OnPaletteChanged;
        ThemeService.PaletteChanged += OnPaletteChanged;
        ApplyThemeAndState();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        ThemeService.PaletteChanged -= OnPaletteChanged;
    }

    private void OnPaletteChanged(object? sender, EventArgs e)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(ApplyThemeAndState);
            return;
        }

        ApplyThemeAndState();
    }

    public string SiteName
    {
        get => (string)GetValue(SiteNameProperty);
        set => SetValue(SiteNameProperty, value);
    }

    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public bool IsSiteEnabled
    {
        get => (bool)GetValue(IsSiteEnabledProperty);
        set => SetValue(IsSiteEnabledProperty, value);
    }

    public ICommand? ToggleSiteCommand
    {
        get => (ICommand?)GetValue(ToggleSiteCommandProperty);
        set => SetValue(ToggleSiteCommandProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public ICommand? ToggleExpandCommand
    {
        get => (ICommand?)GetValue(ToggleExpandCommandProperty);
        set => SetValue(ToggleExpandCommandProperty, value);
    }

    public View? BodyContent
    {
        get => (View?)GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    private void UpdateEnabledVisuals(bool enabled) => ApplyThemeAndState();

    private void UpdateExpandedVisuals(bool expanded)
    {
        _bodyHost.IsVisible = expanded;
        _expandButton.Text = expanded ? Icons.ChevronUp : Icons.ChevronDown;
        ApplyThemeAndState();
    }

    private void ApplyThemeAndState()
    {
        Color primary = GetThemeColor("Primary", "#D4745E");
        Color onPrimary = GetThemeColor("OnPrimary", "#FFFFFF");
        Color secondary = GetThemeColor("Secondary", "#34302C");
        Color cardBorder = GetThemeColor("CardBorder", "#2D2A26");
        Color cardBg = GetThemeColor("DarkBg2", "#191817");
        Color textMuted = GetThemeColor("TextMuted", "#767068");

        bool enabled = IsSiteEnabled;
        _checkGlyph.IsVisible = enabled;
        _checkGlyph.TextColor = onPrimary;

        _cardBorder.Background = new SolidColorBrush(cardBg);
        _cardBorder.Stroke = new SolidColorBrush(enabled ? primary : cardBorder);

        _checkboxBorder.Stroke = new SolidColorBrush(enabled ? primary : secondary);
        _checkboxBorder.Background = enabled ? new SolidColorBrush(primary) : Brush.Transparent;

        _expandButton.TextColor = IsExpanded ? primary : textMuted;
    }

    private static Color GetThemeColor(string key, string fallbackHex) =>
        Application.Current?.Resources.TryGetValue(key, out object? value) == true && value is Color color
            ? color
            : Color.FromArgb(fallbackHex);
}
