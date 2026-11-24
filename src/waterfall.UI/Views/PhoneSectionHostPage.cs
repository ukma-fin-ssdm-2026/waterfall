using Microsoft.Extensions.DependencyInjection;
using waterfall.UI.Controls;
using waterfall.UI.Resources;
using waterfall.UI.Services;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

/// <summary>
/// Mobile host page that pins the phone header and segmented pill switcher in place
/// while lazily mounting child sub-pages and horizontally switching between them.
/// </summary>
public sealed class PhoneSectionHostPage : ContentPage, IHostedPage
{
    private sealed record TabEntry(string Route, Type PageType);

    private readonly IServiceProvider _services;
    private readonly bool _isCabinet;
    private readonly List<TabEntry> _tabs;
    private readonly MobileSegmentedBar _segmentedBar;
#if ANDROID
    private readonly SectionPagerView _pager;
    private readonly List<ContentView> _slots = [];
#else
    private readonly Grid _bodyHost;
    private bool _isSwitching;
#endif
    private readonly HorizontalStackLayout? _catalogHeaderActions;
    private readonly HorizontalStackLayout? _databaseHeaderActions;
    private readonly Dictionary<string, MountedPage> _mounted = new(StringComparer.OrdinalIgnoreCase);

    private string _activeRoute = string.Empty;

    internal string ActiveSubRoute => string.IsNullOrEmpty(_activeRoute) ? _tabs[0].Route : _activeRoute;

    public PhoneSectionHostPage(IServiceProvider services, bool isCabinet, bool isAnalyst)
    {
        _services = services;
        _isCabinet = isCabinet;

        Title = isCabinet ? "Кабінет" : "Вакансії";
        this.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg2");

        _tabs = isCabinet
            ?
            [
                new("login", typeof(LoginPage)),
                new("systemsettings", typeof(SystemSettingsPage)),
                isAnalyst
                    ? new("recommendations", typeof(RecommendationsPage))
                    : new("analytics", typeof(AnalyticsPage))
            ]
            :
            [
                new("catalog", typeof(CatalogPage)),
                new("database", typeof(DatabasePage))
            ];

        View header;
        if (isCabinet)
        {
            header = BuildCabinetHeader();
        }
        else
        {
            (header, _catalogHeaderActions, _databaseHeaderActions) = BuildVacanciesHeader();
        }

        _segmentedBar = new MobileSegmentedBar
        {
            IsAnalystOverride = isAnalyst,
            Group = isCabinet ? "Cabinet" : "Vacancies",
            ActiveRoute = _tabs[0].Route,
            OnTabSelected = route => SwitchToAsync(route, animate: true)
        };

#if ANDROID
        for (int i = 0; i < _tabs.Count; i++)
        {
            _slots.Add(new ContentView
            {
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            });
        }

        _pager = new SectionPagerView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _pager.SetPages(_slots);
        _pager.PageApproaching += (_, index) => EnsureSlotMounted(index);
        _pager.PageSelected += (_, index) => HandlePagerSelected(index);
        View body = _pager;
#else
        _bodyHost = new Grid
        {
            IsClippedToBounds = true
        };
        View body = _bodyHost;
#endif

        Grid root = new()
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            ]
        };

        Grid.SetRow(header, 0);
        Grid.SetRow(_segmentedBar, 1);
        Grid.SetRow(body, 2);

        root.Children.Add(header);
        root.Children.Add(_segmentedBar);
        root.Children.Add(body);

        Content = root;
        UpdateHeaderCta(_tabs[0].Route);
    }

    private (View Header, HorizontalStackLayout CatalogActions, HorizontalStackLayout DatabaseActions) BuildVacanciesHeader()
    {
        WaterfallLogo logo = new()
        {
            WidthRequest = 24,
            HeightRequest = 24,
            VerticalOptions = LayoutOptions.Center
        };

        Label title = new()
        {
            Text = "Вакансії",
            FontFamily = "OpenSansSemibold",
            FontSize = 20,
            VerticalOptions = LayoutOptions.Center
        };
        title.SetDynamicResource(Label.TextColorProperty, "White");

        HorizontalStackLayout brandStack = new()
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { logo, title }
        };

        CatalogViewModel catalogVm = _services.GetRequiredService<CatalogViewModel>();
        ParseSitePreferences.Attach(catalogVm);

        Button parseSettingsButton = new()
        {
            BindingContext = catalogVm,
            Text = Icons.SlidersHorizontal
        };
        MountedPageHelper.ApplyStyle(parseSettingsButton, "IconButton");
        parseSettingsButton.WidthRequest = 34;
        parseSettingsButton.HeightRequest = 34;
        parseSettingsButton.MinimumWidthRequest = 34;
        parseSettingsButton.MinimumHeightRequest = 34;
        parseSettingsButton.CornerRadius = 9;
        parseSettingsButton.VerticalOptions = LayoutOptions.Center;
        parseSettingsButton.SetBinding(Button.CommandProperty, nameof(CatalogViewModel.ToggleParsePanelCommand));
        SemanticProperties.SetDescription(parseSettingsButton, "Параметри парсингу сайтів");

        Button catalogCta = new()
        {
            BindingContext = catalogVm
        };
        MountedPageHelper.ApplyStyle(catalogCta, "CardActionPrimary");
        catalogCta.FontSize = 12;
        catalogCta.Padding = new Thickness(12, 5);
        catalogCta.HeightRequest = 34;
        catalogCta.MinimumHeightRequest = 34;
        catalogCta.CornerRadius = 9;
        catalogCta.VerticalOptions = LayoutOptions.Center;
        catalogCta.SetBinding(Button.TextProperty, nameof(CatalogViewModel.FetchButtonText));
        catalogCta.SetBinding(Button.CommandProperty, nameof(CatalogViewModel.FetchFromSitesCommand));

        HorizontalStackLayout catalogActions = new()
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = true,
            Children = { parseSettingsButton, catalogCta }
        };

        DatabaseViewModel dbVm = _services.GetRequiredService<DatabaseViewModel>();

        Button databaseCta = new()
        {
            BindingContext = dbVm
        };
        MountedPageHelper.ApplyStyle(databaseCta, "CardActionPrimary");
        databaseCta.FontSize = 12;
        databaseCta.Padding = new Thickness(12, 5);
        databaseCta.HeightRequest = 34;
        databaseCta.MinimumHeightRequest = 34;
        databaseCta.CornerRadius = 9;
        databaseCta.VerticalOptions = LayoutOptions.Center;
        databaseCta.SetBinding(Button.TextProperty, nameof(DatabaseViewModel.SyncButtonText));
        databaseCta.SetBinding(Button.CommandProperty, nameof(DatabaseViewModel.SyncDatabaseCommand));

        HorizontalStackLayout databaseActions = new()
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
            Children = { databaseCta }
        };

        Grid ctaSlot = new()
        {
            VerticalOptions = LayoutOptions.Center,
            Children = { catalogActions, databaseActions }
        };

        Button overflowButton = new()
        {
            Text = Icons.EllipsisVertical
        };
        MountedPageHelper.ApplyStyle(overflowButton, "GhostIconButton");
        overflowButton.WidthRequest = 34;
        overflowButton.HeightRequest = 34;
        overflowButton.MinimumWidthRequest = 34;
        overflowButton.MinimumHeightRequest = 34;
        overflowButton.VerticalOptions = LayoutOptions.Center;
        overflowButton.Clicked += (_, _) => ToggleActiveOverflow();

        Grid headerGrid = new()
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 6,
            Padding = new Thickness(14, 10, 12, 6)
        };

        Grid.SetColumn(brandStack, 0);
        Grid.SetColumn(ctaSlot, 1);
        Grid.SetColumn(overflowButton, 2);

        headerGrid.Children.Add(brandStack);
        headerGrid.Children.Add(ctaSlot);
        headerGrid.Children.Add(overflowButton);

        return (headerGrid, catalogActions, databaseActions);
    }

    private static View BuildCabinetHeader()
    {
        Label title = new()
        {
            Text = "Кабінет",
            FontFamily = "OpenSansSemibold",
            FontSize = 20,
            VerticalOptions = LayoutOptions.Center
        };
        title.SetDynamicResource(Label.TextColorProperty, "White");

        return new Grid
        {
            Padding = new Thickness(14, 10, 12, 6),
            Children = { title }
        };
    }

    public Task SwitchToAsync(string route, bool animate)
    {
        if (!MainThread.IsMainThread)
        {
            return MainThread.InvokeOnMainThreadAsync(() => SwitchToOnMainThreadAsync(route, animate));
        }

        return SwitchToOnMainThreadAsync(route, animate);
    }

#if ANDROID
    private Task SwitchToOnMainThreadAsync(string rawRoute, bool animate)
    {
        string targetRoute = rawRoute.Trim().TrimStart('/');
        int newIndex = _tabs.FindIndex(t => string.Equals(t.Route, targetRoute, StringComparison.OrdinalIgnoreCase));
        if (newIndex < 0)
        {
            return Task.CompletedTask;
        }

        string canonicalRoute = _tabs[newIndex].Route;
        if (string.Equals(_activeRoute, canonicalRoute, StringComparison.OrdinalIgnoreCase) &&
            _mounted.ContainsKey(canonicalRoute))
        {
            return Task.CompletedTask;
        }

        MountedPage? incoming = EnsureSlotMounted(newIndex);
        if (incoming is null)
        {
            return Task.CompletedTask;
        }

        MountedPage? outgoing = !string.IsNullOrEmpty(_activeRoute) && _mounted.TryGetValue(_activeRoute, out MountedPage? prev)
            ? prev
            : null;

        _activeRoute = canonicalRoute;
        _segmentedBar.SetActiveRoute(canonicalRoute);
        UpdateHeaderCta(canonicalRoute);
        UpdateSwipeEnabled();
        _pager.SetCurrentIndex(newIndex, animate);

        if (outgoing is not null)
        {
            CloseActiveMenus(outgoing.Page);
            MountedPageHelper.Deactivate(outgoing.Page);
        }

        MountedPageHelper.Activate(incoming.Page);
        return Task.CompletedTask;
    }

    private void HandlePagerSelected(int newIndex)
    {
        if (newIndex < 0 || newIndex >= _tabs.Count)
        {
            return;
        }

        string canonicalRoute = _tabs[newIndex].Route;
        if (string.Equals(_activeRoute, canonicalRoute, StringComparison.OrdinalIgnoreCase) &&
            _mounted.ContainsKey(canonicalRoute))
        {
            return;
        }

        MountedPage? incoming = EnsureSlotMounted(newIndex);
        if (incoming is null)
        {
            return;
        }

        MountedPage? outgoing = !string.IsNullOrEmpty(_activeRoute) && _mounted.TryGetValue(_activeRoute, out MountedPage? prev)
            ? prev
            : null;

        _activeRoute = canonicalRoute;
        _segmentedBar.SetActiveRoute(canonicalRoute);
        UpdateHeaderCta(canonicalRoute);
        UpdateSwipeEnabled();

        if (outgoing is not null)
        {
            CloseActiveMenus(outgoing.Page);
            MountedPageHelper.Deactivate(outgoing.Page);
        }

        MountedPageHelper.Activate(incoming.Page);
    }

    private MountedPage? EnsureSlotMounted(int index)
    {
        if (index < 0 || index >= _tabs.Count)
        {
            return null;
        }

        string canonicalRoute = _tabs[index].Route;
        if (_mounted.TryGetValue(canonicalRoute, out MountedPage? existing))
        {
            return existing;
        }

        ContentPage page = (ContentPage)_services.GetRequiredService(_tabs[index].PageType);
        HideStandaloneHeaderIfNeeded(page);

        MountedPage mounted = MountedPageHelper.Mount(page);
        mounted.Wrapper.TranslationX = 0;
        mounted.Wrapper.Opacity = 1;
        mounted.Wrapper.IsVisible = true;
        HookOverlayChanges(mounted.Page);

        _slots[index].Content = mounted.Wrapper;
        _mounted[canonicalRoute] = mounted;
        return mounted;
    }

    private void HookOverlayChanges(ContentPage page)
    {
        switch (page)
        {
            case CatalogPage cp:
                cp.OverlayStateChanged += OnChildOverlayStateChanged;
                break;
            case DatabasePage dp:
                dp.OverlayStateChanged += OnChildOverlayStateChanged;
                break;
        }
    }

    private void OnChildOverlayStateChanged(object? sender, EventArgs e) => UpdateSwipeEnabled();

    private void UpdateSwipeEnabled()
    {
        _pager.IsSwipeEnabled = !IsActiveOverlayOpen();
    }

    private bool IsActiveOverlayOpen()
    {
        if (string.IsNullOrEmpty(_activeRoute) || !_mounted.TryGetValue(_activeRoute, out MountedPage? active))
        {
            return false;
        }

        return active.Page switch
        {
            CatalogPage cp => cp.HasOpenOverlay,
            DatabasePage dp => dp.HasOpenOverlay,
            _ => false
        };
    }
#else
    private async Task SwitchToOnMainThreadAsync(string rawRoute, bool animate)
    {
        string targetRoute = rawRoute.Trim().TrimStart('/');
        int newIndex = _tabs.FindIndex(t => string.Equals(t.Route, targetRoute, StringComparison.OrdinalIgnoreCase));
        if (newIndex < 0 || _isSwitching)
        {
            return;
        }

        string canonicalRoute = _tabs[newIndex].Route;
        if (string.Equals(_activeRoute, canonicalRoute, StringComparison.OrdinalIgnoreCase) &&
            _mounted.ContainsKey(canonicalRoute))
        {
            return;
        }

        _segmentedBar.SetActiveRoute(canonicalRoute);
        UpdateHeaderCta(canonicalRoute);

        if (!_mounted.TryGetValue(canonicalRoute, out MountedPage? incoming))
        {
            ContentPage page = (ContentPage)_services.GetRequiredService(_tabs[newIndex].PageType);
            HideStandaloneHeaderIfNeeded(page);

            incoming = MountedPageHelper.Mount(page);
            _bodyHost.Children.Add(incoming.Wrapper);
            _mounted[canonicalRoute] = incoming;
        }

        MountedPage? outgoing = !string.IsNullOrEmpty(_activeRoute) && _mounted.TryGetValue(_activeRoute, out MountedPage? prev)
            ? prev
            : null;
        int oldIndex = string.IsNullOrEmpty(_activeRoute)
            ? -1
            : _tabs.FindIndex(t => string.Equals(t.Route, _activeRoute, StringComparison.OrdinalIgnoreCase));

        _activeRoute = canonicalRoute;

        if (animate && outgoing is not null && oldIndex >= 0 && _bodyHost.Width > 0)
        {
            _isSwitching = true;
            try
            {
                await MountedPageHelper.SlideSwitchAsync(outgoing.Wrapper, incoming.Wrapper, _bodyHost.Width, newIndex > oldIndex ? 1 : -1, durationMs: 210);
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
            CloseActiveMenus(outgoing.Page);
            MountedPageHelper.Deactivate(outgoing.Page);
        }

        MountedPageHelper.Activate(incoming.Page);
    }
#endif

    private void UpdateHeaderCta(string route)
    {
        if (_isCabinet || _catalogHeaderActions is null || _databaseHeaderActions is null)
        {
            return;
        }

        bool isCatalog = string.Equals(route, "catalog", StringComparison.OrdinalIgnoreCase);
        _catalogHeaderActions.IsVisible = isCatalog;
        _databaseHeaderActions.IsVisible = !isCatalog;
    }

    private void ToggleActiveOverflow()
    {
        if (string.IsNullOrEmpty(_activeRoute) || !_mounted.TryGetValue(_activeRoute, out MountedPage? active))
        {
            return;
        }

        switch (active.Page)
        {
            case CatalogPage cp: cp.ToggleOverflowFromHost(); break;
            case DatabasePage dp: dp.ToggleOverflowFromHost(); break;
        }
    }

    private static void CloseActiveMenus(ContentPage page)
    {
        switch (page)
        {
            case CatalogPage cp: cp.CloseOverflowFromHost(); break;
            case DatabasePage dp: dp.CloseOverflowFromHost(); break;
        }
    }

    private void HideStandaloneHeaderIfNeeded(ContentPage page)
    {
        if (!_isCabinet)
        {
            return;
        }

        switch (page)
        {
            case AnalyticsPage ap: ap.HideStandalonePhoneHeader(); break;
            case RecommendationsPage rp: rp.HideStandalonePhoneHeader(); break;
        }
    }

    public void OnHostActivated()
    {
        if (string.IsNullOrEmpty(_activeRoute))
        {
            _ = SwitchToAsync(_tabs[0].Route, animate: false);
        }
        else if (_mounted.TryGetValue(_activeRoute, out MountedPage? active))
        {
            MountedPageHelper.Activate(active.Page);
        }
    }

    public void OnHostDeactivated()
    {
        if (!string.IsNullOrEmpty(_activeRoute) && _mounted.TryGetValue(_activeRoute, out MountedPage? active))
        {
            CloseActiveMenus(active.Page);
            MountedPageHelper.Deactivate(active.Page);
        }
    }

    public bool OnHostBackPressed()
    {
        if (!string.IsNullOrEmpty(_activeRoute) &&
            _mounted.TryGetValue(_activeRoute, out MountedPage? active) &&
            MountedPageHelper.HandleBack(active.Page))
        {
            return true;
        }

        int currentIndex = _tabs.FindIndex(t => string.Equals(t.Route, _activeRoute, StringComparison.OrdinalIgnoreCase));
        if (currentIndex > 0)
        {
            _ = SwitchToAsync(_tabs[0].Route, animate: true);
            return true;
        }

        return false;
    }
}
