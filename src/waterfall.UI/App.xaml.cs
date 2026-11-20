using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Services;
using Waterfall.Client.ViewModels;

namespace waterfall.UI;

public partial class App : Application
{
    private readonly IVacancySyncService? _vacancySyncService;
    private readonly IDistanceCalculatorService? _distanceService;
#if DEBUG
    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;

    public App(
        IHttpGatewayService gateway,
        IMessenger messenger,
        IVacancySyncService? vacancySyncService = null,
        IDistanceCalculatorService? distanceService = null)
    {
        _gateway = gateway;
        _messenger = messenger;
        _vacancySyncService = vacancySyncService;
        _distanceService = distanceService;
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
        try
        {
            SecureStorage.Default.Remove("waterfall_access_token");
        }
        catch
        {
        }

        SubscribeConnectivitySync();
    }

    protected override async void OnStart()
    {
        base.OnStart();
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
        IVacancySyncService? vacancySyncService = null,
        IDistanceCalculatorService? distanceService = null)
    {
        _vacancySyncService = vacancySyncService;
        _distanceService = distanceService;
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
        try
        {
            SecureStorage.Default.Remove("waterfall_access_token");
        }
        catch
        {
        }

        SubscribeConnectivitySync();
    }

    protected override async void OnStart()
    {
        base.OnStart();
        await TriggerBackgroundSyncAsync();
    }
#endif

    private void SubscribeConnectivitySync()
    {
        try
        {
            Connectivity.Current.ConnectivityChanged += async (_, args) =>
            {
                if (args.NetworkAccess == NetworkAccess.Internet)
                {
                    await TriggerBackgroundSyncAsync();
                }
            };
        }
        catch
        {
            // Ігноруємо на платформах без підтримки Connectivity
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

    protected override Window CreateWindow(IActivationState? activationState) => new Window(new AppShell());
}
