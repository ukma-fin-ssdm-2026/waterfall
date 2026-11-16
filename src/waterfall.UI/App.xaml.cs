using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.ViewModels;

namespace waterfall.UI;

public partial class App : Application
{
#if DEBUG
    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;

    public App(IHttpGatewayService gateway, IMessenger messenger)
    {
        _gateway = gateway;
        _messenger = messenger;
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
        try
        {
            SecureStorage.Default.Remove("waterfall_access_token");
        }
        catch
        {
        }
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
    }
#else
    public App()
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;
        try
        {
            SecureStorage.Default.Remove("waterfall_access_token");
        }
        catch
        {
        }
    }
#endif

    protected override Window CreateWindow(IActivationState? activationState) => new Window(new AppShell());
}
