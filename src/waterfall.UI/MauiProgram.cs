using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using waterfall.UI.Services;
using waterfall.UI.Views;
using Waterfall.Client.Extensions;
using Waterfall.Client.ViewModels;

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
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.IUrlLauncher, MauiUrlLauncher>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.ILocalGeoCacheService, Waterfall.Client.Services.EfLocalGeoCacheService>();
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.IDistanceCalculatorService>(sp =>
            new Waterfall.Client.Services.GeoDistanceService(
                sp.GetService<Waterfall.Client.Interfaces.IHttpGatewayService>(),
                sp.GetService<Waterfall.Client.Interfaces.ILocalGeoCacheService>()));
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.IAddressAutocompleteService, Waterfall.Client.Services.PhotonAddressAutocompleteService>();
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        builder.Services.AddWaterfallClient<SecureStorageTokenStore>(new Uri(ApiBaseUrl));

        builder.Services.AddTransient<CatalogViewModel>();
        builder.Services.AddTransient<CatalogPage>();
        builder.Services.AddTransient<RecommendationsViewModel>();
        builder.Services.AddTransient<RecommendationsPage>();
        builder.Services.AddTransient<AIConsultantViewModel>();
        builder.Services.AddTransient<AIConsultantPage>();
        builder.Services.AddTransient<AnalyticsViewModel>();
        builder.Services.AddTransient<AnalyticsPage>();
        builder.Services.AddTransient<FavoritesViewModel>();
        builder.Services.AddTransient<FavoritesPage>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Прибираємо внутрішні нативні рамки з Entry, Editor, SearchBar,
        // щоб показувалася лише стилізована зовнішня рамка MAUI Border.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("RemoveNativeBorder", (handler, view) =>
        {
#if WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#elif ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
#elif IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });

        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("RemoveNativeBorder", (handler, view) =>
        {
#if WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#elif ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
#endif
        });

        Microsoft.Maui.Handlers.SearchBarHandler.Mapper.AppendToMapping("RemoveNativeBorder", (handler, view) =>
        {
#if WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#elif ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
#endif
        });

        return builder.Build();
    }
}
