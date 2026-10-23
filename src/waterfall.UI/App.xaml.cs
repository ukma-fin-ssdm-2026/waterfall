namespace waterfall.UI;

public partial class App : Application
{
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

    protected override Window CreateWindow(IActivationState? activationState) => new Window(new AppShell());
}
