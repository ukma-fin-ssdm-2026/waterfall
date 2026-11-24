using CommunityToolkit.Mvvm.Messaging;
using waterfall.UI.Services;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Services;
using Waterfall.Client.ViewModels;

namespace waterfall.UI;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly ThemeService _themeService;
    private readonly IAuthTokenStore? _tokenStore;
    private readonly IMessenger? _appMessenger;
    private readonly IVacancySyncService? _vacancySyncService;
    private readonly IDistanceCalculatorService? _distanceService;
#if DEBUG
    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;

    public App(
        IServiceProvider services,
        ThemeService themeService,
        IHttpGatewayService gateway,
        IMessenger messenger,
        IAuthTokenStore? tokenStore = null,
        IVacancySyncService? vacancySyncService = null,
        IDistanceCalculatorService? distanceService = null)
    {
        _services = services;
        _themeService = themeService;
        _gateway = gateway;
        _messenger = messenger;
        _appMessenger = messenger;
        _tokenStore = tokenStore;
        _vacancySyncService = vacancySyncService;
        _distanceService = distanceService;
        InitializeComponent();
        _themeService.Initialize();
        try
        {
            SecureStorage.Default.Remove("waterfall_access_token");
        }
        catch
        {
        }

        SubscribeConnectivitySync();
    }

    protected override void OnStart()
    {
        base.OnStart();
        _ = InitializeOnStartAsync();
    }

    private async Task InitializeOnStartAsync()
    {
        await Task.Delay(400);
        try
        {
            AuthResponse response = await _gateway.LoginAsync(new LoginRequest
            {
                Email = "123@123",
                Password = "123@123123@123"
            });
            _messenger.Send(new UserAuthStateChangedMessage(true, response.Email));
        }
        catch
        {
        }

        await TriggerBackgroundSyncAsync();
    }
#else
    public App(
        IServiceProvider services,
        ThemeService themeService,
        IAuthTokenStore? tokenStore = null,
        IMessenger? messenger = null,
        IVacancySyncService? vacancySyncService = null,
        IDistanceCalculatorService? distanceService = null)
    {
        _services = services;
        _themeService = themeService;
        _tokenStore = tokenStore;
        _appMessenger = messenger;
        _vacancySyncService = vacancySyncService;
        _distanceService = distanceService;
        InitializeComponent();
        _themeService.Initialize();
        try
        {
            SecureStorage.Default.Remove("waterfall_access_token");
        }
        catch
        {
        }

        SubscribeConnectivitySync();
    }

    protected override void OnStart()
    {
        base.OnStart();
        _ = DelayedStartSyncAsync();
    }

    private async Task DelayedStartSyncAsync()
    {
        await Task.Delay(400);
        await TriggerBackgroundSyncAsync();
    }
#endif

    private void SubscribeConnectivitySync()
    {
        try
        {
            Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        }
        catch
        {
            // Ігноруємо на платформах без підтримки Connectivity
        }
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
    {
        if (args.NetworkAccess == NetworkAccess.Internet)
        {
            _ = TriggerBackgroundSyncAsync();
        }
    }

    private async Task TriggerBackgroundSyncAsync()
    {
        try
        {
            if (_vacancySyncService is not null)
            {
                await _vacancySyncService.SyncWithServerAsync(fullSync: false);
            }

            if (_distanceService is GeoDistanceService geoService)
            {
                await geoService.SyncWithServerAsync();
            }
        }
        catch
        {
            // Офлайн на старті — синхронізація повториться при відновленні мережі або натисканні кнопки
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
#if WINDOWS
        Page rootPage = new DesktopHostPage(_services, _tokenStore, _appMessenger, _themeService);
        Window window = new(rootPage)
        {
            Title = "Waterfall"
        };
        TitleBar titleBar = new()
        {
            Title = "Waterfall",
            HeightRequest = 32
        };
        titleBar.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg2");
        titleBar.SetDynamicResource(TitleBar.ForegroundColorProperty, "White");
        window.TitleBar = titleBar;
#else
        Window window = new(new waterfall.UI.Views.PhoneHostPage(_services, _tokenStore, _appMessenger, _themeService))
        {
            Title = "Waterfall"
        };
#endif
        window.HandlerChanged += (_, _) => _themeService.ApplyWindowChrome();
        return window;
    }
}
