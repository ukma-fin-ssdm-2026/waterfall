using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Waterfall.Client.Extensions;
using Waterfall.Client.ViewModels;
using waterfall.UI.Services;
using waterfall.UI.Views;

namespace waterfall.UI;

public static class MauiProgram
{
    // Локальний API (dotnet run --project src/api). Android-емулятор бачить localhost хоста як 10.0.2.2;
    // cleartext HTTP дозволено лише для цих адрес (Platforms/Android/Resources/xml/network_security_config.xml).
#if ANDROID
    private const string ApiBaseUrl = "http://10.0.2.2:3000";
#else
    private const string ApiBaseUrl = "http://localhost:3000";
#endif

    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(SecureStorage.Default);
        // Синхронізація стану «Обраного» між вкладками (VacancySaveChangedMessage); слабкі посилання — без витоків сторінок.
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        builder.Services.AddWaterfallClient<SecureStorageTokenStore>(new Uri(ApiBaseUrl));

        builder.Services.AddTransient<CatalogViewModel>();
        builder.Services.AddTransient<CatalogPage>();
        builder.Services.AddTransient<FavoritesViewModel>();
        builder.Services.AddTransient<FavoritesPage>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();

    }
}
