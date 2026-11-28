using CommunityToolkit.Mvvm.Messaging;
using waterfall.UI.Services;
using Waterfall.Client.Interfaces;
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
        if (messenger is not null)
        {
            SyncAfterLogin(messenger);
        }

        InitializeComponent();
        _themeService.Initialize();
        SubscribeConnectivitySync();
    }

    // Збережений токен не видаляється при старті (REL-002): сесія переживає перезапуск.
    // /vacancies/sync і /geo/sync вимагають входу, тому синхронізація стартує після входу (SyncAfterLogin).

    /// <summary>
    /// Синхронізація бази й геокешу одразу після входу: сервер віддає їх лише автентифікованим користувачам.
    /// </summary>
    private void SyncAfterLogin(IMessenger messenger) =>
        messenger.Register<App, UserAuthStateChangedMessage>(this, static (app, message) =>
        {
            if (message.IsLoggedIn)
            {
                _ = app.TriggerBackgroundSyncAsync();
            }
        });

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
            string? token = _tokenStore is null
                ? null
                : await _tokenStore.GetTokenAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            if (_vacancySyncService is not null)
            {
                await _vacancySyncService.SyncWithServerAsync(fullSync: false, CancellationToken.None);
            }

            if (_distanceService is GeoDistanceService geoService)
            {
                await geoService.SyncWithServerAsync(CancellationToken.None);
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
