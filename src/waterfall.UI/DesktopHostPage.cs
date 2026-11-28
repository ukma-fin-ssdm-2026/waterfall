using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Controls;
using waterfall.UI.Resources;
using waterfall.UI.Services;
using waterfall.UI.Views;
using Waterfall.Client.Interfaces;
using Waterfall.Client.ViewModels;

namespace waterfall.UI;

/// <summary>
/// High-performance Desktop navigation host (Windows):
/// keeps visited page visual trees alive in RAM and switches tabs via IsVisible (under 16ms, 1 frame)
/// instead of WinUI Shell detaching/recreating native handlers on every FlyoutItem click.
/// </summary>
public sealed class DesktopHostPage : ContentPage, IAppNavigationHost
{
    private sealed record NavEntry(string Route, string Label, string Glyph, Type PageType, bool IsFooter);
    private sealed record RailRow(Border Container, Border Indicator);

    public static DesktopHostPage? Current { get; private set; }

    private readonly IServiceProvider _services;
    private readonly IAuthTokenStore? _tokens;
    private readonly ThemeService _themeService;
    private readonly Label _roleBadgeLabel;
    private readonly Label _pageTitleLabel;
    private readonly VerticalStackLayout _topNavStack;
    private readonly VerticalStackLayout _footerNavStack;
    private readonly Grid _contentHost;

    private readonly Dictionary<string, NavEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RailRow> _railRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MountedPage> _mounted = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _visibleRoutes = new(StringComparer.OrdinalIgnoreCase);

    private string _activeRoute = string.Empty;
    private string? _hoveredRoute;
    private bool _isAnalyst;

    public DesktopHostPage(
        IServiceProvider services,
        IAuthTokenStore? tokens,
        IMessenger? messenger,
        ThemeService themeService)
    {
        Current = this;
        _services = services;
        _tokens = tokens;
        _themeService = themeService;
        Title = "Waterfall";
        this.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg2");

        _roleBadgeLabel = new Label
        {
            Text = "Гостьовий режим",
            FontSize = 10.5
        };
        _roleBadgeLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        _pageTitleLabel = new Label
        {
            Text = "Каталог",
            FontFamily = "OpenSansSemibold",
            FontSize = 24,
            Margin = new Thickness(18, 12, 18, 4),
            VerticalOptions = LayoutOptions.Center
        };
        _pageTitleLabel.SetDynamicResource(Label.TextColorProperty, "White");

        _topNavStack = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(6, 4) };
        _footerNavStack = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(6, 6, 6, 12) };
        _contentHost = new Grid();

        Content = BuildLayout();
        RebuildRail(isAnalyst: false);

        _themeService.ThemeChanged += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateRailSelectionVisuals);

        if (messenger is not null)
        {
            messenger.Register<DesktopHostPage, UserAuthStateChangedMessage>(this, static (host, message) =>
            {
                _ = host.RefreshRoleAsync();
            });
        }

        _ = NavigateToAsync("catalog");
        _ = RefreshRoleAsync();
    }

    private Grid BuildLayout()
    {
        Grid root = new()
        {
            ColumnDefinitions = [new ColumnDefinition(206), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 0
        };

        Grid railGrid = new()
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)]
        };
        railGrid.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkRail");

        View header = BuildBrandHeader();
        Grid.SetRow(header, 0);
        railGrid.Children.Add(header);

        ScrollView navScroll = new()
        {
            Content = _topNavStack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };
        Grid.SetRow(navScroll, 1);
        railGrid.Children.Add(navScroll);

        Grid.SetRow(_footerNavStack, 2);
        railGrid.Children.Add(_footerNavStack);

        Grid.SetColumn(railGrid, 0);
        root.Children.Add(railGrid);

        Grid mainArea = new()
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)]
        };
        mainArea.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg2");

        Grid.SetRow(_pageTitleLabel, 0);
        mainArea.Children.Add(_pageTitleLabel);

        Grid.SetRow(_contentHost, 1);
        mainArea.Children.Add(_contentHost);

        Grid.SetColumn(mainArea, 1);
        root.Children.Add(mainArea);

        return root;
    }

    private View BuildBrandHeader()
    {
        WaterfallLogo logoMark = new()
        {
            WidthRequest = 28,
            HeightRequest = 28,
            VerticalOptions = LayoutOptions.Center
        };

        Label appTitle = new()
        {
            Text = "Waterfall",
            FontFamily = "OpenSansSemibold",
            FontSize = 14
        };
        appTitle.SetDynamicResource(Label.TextColorProperty, "White");

        BoxView divider = new() { HeightRequest = 1 };
        divider.SetDynamicResource(BoxView.ColorProperty, "DividerColor");

        return new VerticalStackLayout
        {
            Padding = new Thickness(14, 14, 14, 8),
            Spacing = 10,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 9,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        logoMark,
                        new VerticalStackLayout
                        {
                            Spacing = 0,
                            VerticalOptions = LayoutOptions.Center,
                            Children = { appTitle, _roleBadgeLabel }
                        }
                    }
                },
                divider
            }
        };
    }

    private void RebuildRail(bool isAnalyst)
    {
        _isAnalyst = isAnalyst;
        _entries.Clear();
        _railRows.Clear();
        _visibleRoutes.Clear();
        _topNavStack.Children.Clear();
        _footerNavStack.Children.Clear();

        // Усі маршрути реєструються завжди, щоб прямий перехід досягав сторінки й вона сама
        // показувала відмову в доступі (US-006 AC-3). У рейлі — лише пункти, дозволені ролі.
        List<NavEntry> allEntries =
        [
            new("catalog", "Каталог", Icons.Globe, typeof(CatalogPage), IsFooter: false),
            new("database", "База вакансій", Icons.Database, typeof(DatabasePage), IsFooter: false),
            new("analytics", "Аналітика", Icons.ChartColumn, typeof(AnalyticsPage), IsFooter: false),
            new("aiconsultant", "ШІ-чат", Icons.MessageSquare, typeof(AIConsultantPage), IsFooter: false),
            new("favorites", "Обране", Icons.Heart, typeof(FavoritesPage), IsFooter: false),
            new("recommendations", "Рекомендації", Icons.Sparkles, typeof(RecommendationsPage), IsFooter: false),
            new("login", "Профіль", Icons.User, typeof(LoginPage), IsFooter: true),
            new("systemsettings", "Налаштування", Icons.Settings, typeof(SystemSettingsPage), IsFooter: true)
        ];

        // US-006 AC-3: шукач роботи не бачить «Аналітику». «Рекомендації», «ШІ-чат» і
        // «Обране» — розділи шукача, тому аналітику вони не показуються.
        _visibleRoutes.Add("catalog");
        _visibleRoutes.Add("database");
        _visibleRoutes.Add("login");
        _visibleRoutes.Add("systemsettings");
        if (isAnalyst)
        {
            _visibleRoutes.Add("analytics");
        }
        else
        {
            _visibleRoutes.Add("aiconsultant");
            _visibleRoutes.Add("favorites");
            _visibleRoutes.Add("recommendations");
        }

        foreach (NavEntry entry in allEntries)
        {
            _entries[entry.Route] = entry;
            if (_visibleRoutes.Contains(entry.Route) && !entry.IsFooter)
            {
                _topNavStack.Children.Add(CreateRailRow(entry));
            }
        }

        BoxView footerDivider = new()
        {
            HeightRequest = 1,
            Margin = new Thickness(6, 0, 6, 6)
        };
        footerDivider.SetDynamicResource(BoxView.ColorProperty, "DividerColor");
        _footerNavStack.Children.Add(footerDivider);

        foreach (NavEntry entry in allEntries)
        {
            if (entry.IsFooter && _visibleRoutes.Contains(entry.Route))
            {
                _footerNavStack.Children.Add(CreateRailRow(entry));
            }
        }

        UpdateRailSelectionVisuals();
    }

    private Border CreateRailRow(NavEntry entry)
    {
        Border indicator = new()
        {
            WidthRequest = 3,
            HeightRequest = 16,
            StrokeShape = new RoundRectangle { CornerRadius = 1.5 },
            StrokeThickness = 0,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            IsVisible = false
        };
        indicator.SetDynamicResource(VisualElement.BackgroundColorProperty, "Primary");

        Label iconLabel = new()
        {
            Text = entry.Glyph,
            FontFamily = Icons.FontFamily,
            FontSize = 14.5,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        iconLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");

        Label titleLabel = new()
        {
            Text = entry.Label,
            FontFamily = "OpenSansSemibold",
            FontSize = 12.5,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "White");

        Grid grid = new()
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(6),
                new ColumnDefinition(20),
                new ColumnDefinition(GridLength.Star)
            ],
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center
        };

        Grid.SetColumn(indicator, 0);
        Grid.SetColumn(iconLabel, 1);
        Grid.SetColumn(titleLabel, 2);
        grid.Children.Add(indicator);
        grid.Children.Add(iconLabel);
        grid.Children.Add(titleLabel);

        Border container = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            StrokeThickness = 0,
            Background = Brush.Transparent,
            Padding = new Thickness(4, 8, 10, 8),
            Content = grid
        };

        TapGestureRecognizer tap = new();
        tap.Tapped += (_, _) => _ = NavigateToAsync(entry.Route);
        container.GestureRecognizers.Add(tap);

        PointerGestureRecognizer hover = new();
        hover.PointerEntered += (_, _) =>
        {
            _hoveredRoute = entry.Route;
            UpdateRailSelectionVisuals();
        };
        hover.PointerExited += (_, _) =>
        {
            if (string.Equals(_hoveredRoute, entry.Route, StringComparison.OrdinalIgnoreCase))
            {
                _hoveredRoute = null;
                UpdateRailSelectionVisuals();
            }
        };
        container.GestureRecognizers.Add(hover);

        _railRows[entry.Route] = new RailRow(container, indicator);
        return container;
    }

    public Task NavigateToAsync(string rawRoute)
    {
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            string route = rawRoute.Trim().TrimStart('/');
            if (string.IsNullOrWhiteSpace(route) || !_entries.TryGetValue(route, out NavEntry? entry))
            {
                return;
            }

            if (string.Equals(_activeRoute, route, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!string.IsNullOrEmpty(_activeRoute) && _mounted.TryGetValue(_activeRoute, out MountedPage? outgoing))
            {
                outgoing.Wrapper.IsVisible = false;
                MountedPageHelper.Deactivate(outgoing.Page);
            }

            if (!_mounted.TryGetValue(route, out MountedPage? incoming))
            {
                ContentPage page = (ContentPage)_services.GetRequiredService(entry.PageType);
                incoming = MountedPageHelper.Mount(page);
                _contentHost.Children.Add(incoming.Wrapper);
                _mounted[route] = incoming;
            }

            _activeRoute = route;
            _pageTitleLabel.Text = string.IsNullOrWhiteSpace(incoming.Page.Title) ? entry.Label : incoming.Page.Title;
            incoming.Wrapper.IsVisible = true;
            MountedPageHelper.Activate(incoming.Page);
            UpdateRailSelectionVisuals();
        });
    }

    private void UpdateRailSelectionVisuals()
    {
        bool isDark = _themeService.IsEffectiveDark;
        Color activeColor = isDark
            ? Color.FromRgba(0xFF, 0xF8, 0xF0, 0x16)
            : Color.FromRgba(0x1E, 0x19, 0x14, 0x14);
        Color hoverColor = isDark
            ? Color.FromRgba(0xFF, 0xF8, 0xF0, 0x0D)
            : Color.FromRgba(0x1E, 0x19, 0x14, 0x0B);

        foreach ((string route, RailRow row) in _railRows)
        {
            bool active = string.Equals(route, _activeRoute, StringComparison.OrdinalIgnoreCase);
            bool hovered = !active && string.Equals(route, _hoveredRoute, StringComparison.OrdinalIgnoreCase);

            row.Indicator.IsVisible = active;
            row.Container.Background = active
                ? new SolidColorBrush(activeColor)
                : (hovered ? new SolidColorBrush(hoverColor) : Brush.Transparent);
        }
    }

    private async Task RefreshRoleAsync()
    {
        (_, bool isAnalyst, string roleLabel) = await MountedPageHelper.ResolveRoleAsync(_tokens);

        bool railRebuilt = false;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _roleBadgeLabel.Text = roleLabel;
            if (isAnalyst != _isAnalyst)
            {
                RebuildRail(isAnalyst);
                railRebuilt = true;
            }
        });

        if (railRebuilt && !_visibleRoutes.Contains(_activeRoute))
        {
            await NavigateToAsync("catalog");
        }
    }
}
