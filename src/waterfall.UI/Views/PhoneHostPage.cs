using CommunityToolkit.Mvvm.Messaging;
using waterfall.UI.Resources;
using waterfall.UI.Services;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

/// <summary>
/// Root mobile navigation host replacing MAUI Shell on Android/iOS/MacCatalyst:
/// keeps a pinned Warm Editorial bottom navigation bar with static-size Lucide icons,
/// eliminates Shell toolbar/TabLayout height flicker, lazily mounts tabs in RAM,
/// and slides horizontally across all 5 bottom sections.
/// </summary>
public sealed class PhoneHostPage : ContentPage, IAppNavigationHost
{
    private sealed record BottomTabSpec(string Route, string Label, string Glyph, Func<ContentPage> Factory);
    private sealed record TabVisual(Border TouchTarget, Label IconLabel, Label TitleLabel);

    public static PhoneHostPage? Current { get; private set; }

    private readonly IServiceProvider _services;
    private readonly IAuthTokenStore? _tokens;
    private readonly ThemeService _themeService;
    private readonly Grid _contentHost;
    private readonly Grid _tabBarGrid;
    private readonly List<BottomTabSpec> _tabs = [];
    private readonly Dictionary<string, TabVisual> _tabVisuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MountedPage> _mounted = new(StringComparer.OrdinalIgnoreCase);

    private PhoneSectionHostPage? _vacanciesHost;
    private PhoneSectionHostPage? _cabinetHost;
    private string _activeTabRoute = string.Empty;
    private UserRole? _role;
    private bool _isSwitching;

    public PhoneHostPage(
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
        SafeAreaEdges = SafeAreaEdges.None;
        this.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg2");

        _contentHost = new Grid
        {
            IsClippedToBounds = true,
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.Container, SafeAreaRegions.Container, SafeAreaRegions.Container, SafeAreaRegions.None)
        };

        _tabBarGrid = new Grid
        {
            Padding = new Thickness(4, 7, 4, 7),
            ColumnSpacing = 0
        };

        BoxView topDivider = new() { HeightRequest = 1 };
        topDivider.SetDynamicResource(BoxView.ColorProperty, "DividerColor");

        Grid bottomChrome = new()
        {
            RowDefinitions = [new RowDefinition(1), new RowDefinition(GridLength.Auto)],
            SafeAreaEdges = new SafeAreaEdges(SafeAreaRegions.None, SafeAreaRegions.None, SafeAreaRegions.None, SafeAreaRegions.All)
        };
        bottomChrome.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkRail");
        Grid.SetRow(topDivider, 0);
        Grid.SetRow(_tabBarGrid, 1);
        bottomChrome.Children.Add(topDivider);
        bottomChrome.Children.Add(_tabBarGrid);

        Grid root = new()
        {
            RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)],
            RowSpacing = 0
        };
        Grid.SetRow(_contentHost, 0);
        Grid.SetRow(bottomChrome, 1);
        root.Children.Add(_contentHost);
        root.Children.Add(bottomChrome);

        Content = root;

        RebuildTabs(role: null);

        _themeService.ThemeChanged += (_, _) => MainThread.BeginInvokeOnMainThread(UpdateTabVisuals);

        if (messenger is not null)
        {
            messenger.Register<PhoneHostPage, UserAuthStateChangedMessage>(this, static (host, message) =>
            {
                _ = host.RefreshRoleAsync();
            });
        }

        _ = NavigateToAsync("catalog");
        _ = RefreshRoleAsync();
    }

    private void RebuildTabs(UserRole? role)
    {
        if (!string.IsNullOrEmpty(_activeTabRoute) && _mounted.TryGetValue(_activeTabRoute, out MountedPage? active))
        {
            MountedPageHelper.Deactivate(active.Page);
        }

        _role = role;
        _tabs.Clear();
        _tabVisuals.Clear();
        _mounted.Clear();
        _contentHost.Children.Clear();
        _tabBarGrid.Children.Clear();
        _tabBarGrid.ColumnDefinitions.Clear();
        _activeTabRoute = string.Empty;

        if (role == UserRole.Admin)
        {
            _vacanciesHost = null;
            _cabinetHost = null;
            _tabs.Add(new("login", "Профіль", Icons.User, () => _services.GetRequiredService<LoginPage>()));
            _tabs.Add(new("systemsettings", "Налаштування", Icons.Settings, () => _services.GetRequiredService<SystemSettingsPage>()));
        }
        else if (role == UserRole.Analyst)
        {
            _vacanciesHost = new PhoneSectionHostPage(_services, isCabinet: false, isAnalyst: true);
            _cabinetHost = new PhoneSectionHostPage(_services, isCabinet: true, isAnalyst: true);
            _tabs.Add(new("vacancies", "Вакансії", Icons.Globe, () => _vacanciesHost));
            _tabs.Add(new("analytics", "Аналітика", Icons.ChartColumn, () => _services.GetRequiredService<AnalyticsPage>()));
            _tabs.Add(new("cabinet", "Кабінет", Icons.User, () => _cabinetHost));
        }
        else
        {
            _vacanciesHost = new PhoneSectionHostPage(_services, isCabinet: false, isAnalyst: false);
            _cabinetHost = new PhoneSectionHostPage(_services, isCabinet: true, isAnalyst: false);
            _tabs.Add(new("vacancies", "Вакансії", Icons.Globe, () => _vacanciesHost));
            _tabs.Add(new("recommendations", "Підбірка", Icons.Sparkles, () => _services.GetRequiredService<RecommendationsPage>()));
            _tabs.Add(new("aiconsultant", "ШІ-чат", Icons.MessageSquare, () => _services.GetRequiredService<AIConsultantPage>()));
            _tabs.Add(new("favorites", "Обране", Icons.Heart, () => _services.GetRequiredService<FavoritesPage>()));
            _tabs.Add(new("cabinet", "Кабінет", Icons.User, () => _cabinetHost));
        }

        for (int i = 0; i < _tabs.Count; i++)
        {
            _tabBarGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            BottomTabSpec spec = _tabs[i];
            Border item = CreateTabCell(spec);
            Grid.SetColumn(item, i);
            _tabBarGrid.Children.Add(item);
        }

        UpdateTabVisuals();
    }

    private Border CreateTabCell(BottomTabSpec spec)
    {
        Label iconLabel = new()
        {
            Text = spec.Glyph,
            FontFamily = Icons.FontFamily,
            FontSize = 18,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        Label titleLabel = new()
        {
            Text = spec.Label,
            FontSize = 10.5,
            HorizontalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        VerticalStackLayout stack = new()
        {
            Spacing = 3,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { iconLabel, titleLabel }
        };

        Border cell = new()
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(2, 3),
            Content = stack
        };

        TapGestureRecognizer tap = new();
        tap.Tapped += (_, _) => _ = NavigateToAsync(spec.Route);
        cell.GestureRecognizers.Add(tap);

        _tabVisuals[spec.Route] = new TabVisual(cell, iconLabel, titleLabel);
        return cell;
    }

    public Task NavigateToAsync(string rawRoute) => NavigateToAsync(rawRoute, animate: true);

    private Task NavigateToAsync(string rawRoute, bool animate)
    {
        if (!MainThread.IsMainThread)
        {
            return MainThread.InvokeOnMainThreadAsync(() => NavigateOnMainThreadAsync(rawRoute, animate));
        }

        return NavigateOnMainThreadAsync(rawRoute, animate);
    }

    private async Task NavigateOnMainThreadAsync(string rawRoute, bool animate)
    {
        (string? tabRoute, string? subRoute) = ResolveDestination(rawRoute);
        if (tabRoute is null || _isSwitching)
        {
            return;
        }

        int newIndex = _tabs.FindIndex(t => string.Equals(t.Route, tabRoute, StringComparison.OrdinalIgnoreCase));
        if (newIndex < 0)
        {
            return;
        }

        if (string.Equals(_activeTabRoute, tabRoute, StringComparison.OrdinalIgnoreCase) && _mounted.ContainsKey(tabRoute))
        {
            if (subRoute is not null)
            {
                await SwitchSectionSubTabAsync(tabRoute, subRoute, animate);
            }

            return;
        }

        if (!_mounted.TryGetValue(tabRoute, out MountedPage? incoming))
        {
            ContentPage page = _tabs[newIndex].Factory();
            incoming = MountedPageHelper.Mount(page);
            _contentHost.Children.Add(incoming.Wrapper);
            _mounted[tabRoute] = incoming;
        }

        if (subRoute is not null)
        {
            await SwitchSectionSubTabAsync(tabRoute, subRoute, animate: false);
        }

        MountedPage? outgoing = !string.IsNullOrEmpty(_activeTabRoute) && _mounted.TryGetValue(_activeTabRoute, out MountedPage? prev)
            ? prev
            : null;
        int oldIndex = string.IsNullOrEmpty(_activeTabRoute)
            ? -1
            : _tabs.FindIndex(t => string.Equals(t.Route, _activeTabRoute, StringComparison.OrdinalIgnoreCase));

        _activeTabRoute = tabRoute;
        UpdateTabVisuals();

        if (animate && outgoing is not null && oldIndex >= 0 && _contentHost.Width > 0)
        {
            _isSwitching = true;
            try
            {
                await MountedPageHelper.SlideSwitchAsync(outgoing.Wrapper, incoming.Wrapper, _contentHost.Width, newIndex > oldIndex ? 1 : -1, durationMs: 195);
            }
            finally
            {
                _isSwitching = false;
            }
        }
        else
        {
            if (outgoing is not null)
            {
                outgoing.Wrapper.IsVisible = false;
                outgoing.Wrapper.TranslationX = 0;
            }

            incoming.Wrapper.TranslationX = 0;
            incoming.Wrapper.Opacity = 1;
            incoming.Wrapper.IsVisible = true;
        }

        if (outgoing is not null)
        {
            MountedPageHelper.Deactivate(outgoing.Page);
        }

        MountedPageHelper.Activate(incoming.Page);
    }

    private (string? TabRoute, string? SubRoute) ResolveDestination(string rawRoute)
    {
        string route = rawRoute.Trim().TrimStart('/').ToLowerInvariant();
        if (_role == UserRole.Admin)
        {
            return route switch
            {
                "login" or "systemsettings" => (route, null),
                "cabinet" => ("login", null),
                _ => (null, null)
            };
        }

        return route switch
        {
            "catalog" or "database" => ("vacancies", route),
            "login" or "systemsettings" => ("cabinet", route),
            "analytics" => _role == UserRole.Analyst ? ("analytics", null) : (null, null),
            "recommendations" or "aiconsultant" or "favorites" => _role == UserRole.Analyst ? (null, null) : (route, null),
            "vacancies" or "cabinet" => (route, null),
            _ => (null, null)
        };
    }

    private Task SwitchSectionSubTabAsync(string tabRoute, string subRoute, bool animate)
    {
        if (string.Equals(tabRoute, "vacancies", StringComparison.OrdinalIgnoreCase) && _vacanciesHost is not null)
        {
            return _vacanciesHost.SwitchToAsync(subRoute, animate);
        }

        if (string.Equals(tabRoute, "cabinet", StringComparison.OrdinalIgnoreCase) && _cabinetHost is not null)
        {
            return _cabinetHost.SwitchToAsync(subRoute, animate);
        }

        return Task.CompletedTask;
    }

    private void UpdateTabVisuals()
    {
        foreach ((string route, TabVisual visual) in _tabVisuals)
        {
            bool active = string.Equals(route, _activeTabRoute, StringComparison.OrdinalIgnoreCase);
            visual.IconLabel.SetDynamicResource(Label.TextColorProperty, active ? "Primary" : "TextMuted");
            visual.TitleLabel.SetDynamicResource(Label.TextColorProperty, active ? "Primary" : "TextMuted");
            visual.TitleLabel.FontFamily = active ? "OpenSansSemibold" : "OpenSansRegular";
        }
    }

    private async Task RefreshRoleAsync()
    {
        (_, UserRole? role, _) = await MountedPageHelper.ResolveRoleAsync(_tokens);

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (role == _role)
            {
                return;
            }

            string currentLeafRoute = GetCurrentLeafRoute();
            RebuildTabs(role);
            string targetRoute = ResolveDestination(currentLeafRoute).TabRoute is not null
                ? currentLeafRoute
                : (role == UserRole.Admin ? "systemsettings" : "catalog");
            await NavigateToAsync(targetRoute, animate: false);
        });
    }

    private string GetCurrentLeafRoute()
    {
        if (string.Equals(_activeTabRoute, "vacancies", StringComparison.OrdinalIgnoreCase) && _vacanciesHost is not null)
        {
            return _vacanciesHost.ActiveSubRoute;
        }

        if (string.Equals(_activeTabRoute, "cabinet", StringComparison.OrdinalIgnoreCase) && _cabinetHost is not null)
        {
            return _cabinetHost.ActiveSubRoute;
        }

        return string.IsNullOrEmpty(_activeTabRoute) ? "catalog" : _activeTabRoute;
    }

    protected override bool OnBackButtonPressed()
    {
        if (!string.IsNullOrEmpty(_activeTabRoute) &&
            _mounted.TryGetValue(_activeTabRoute, out MountedPage? active) &&
            MountedPageHelper.HandleBack(active.Page))
        {
            return true;
        }

        string homeRoute = _role == UserRole.Admin ? "login" : "vacancies";
        if (!string.Equals(_activeTabRoute, homeRoute, StringComparison.OrdinalIgnoreCase))
        {
            _ = NavigateToAsync(homeRoute);
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
