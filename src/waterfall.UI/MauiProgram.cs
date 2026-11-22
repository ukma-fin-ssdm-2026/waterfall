// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
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
    // фізичний пристрій — через `adb reverse tcp:3000 tcp:3000` (localhost на самому телефоні).
    // cleartext HTTP дозволено лише для цих адрес (Platforms/Android/Resources/xml/network_security_config.xml).
#if ANDROID
    private static readonly string ApiBaseUrl = DeviceInfo.DeviceType == DeviceType.Virtual
        ? "http://10.0.2.2:3000"
        : "http://localhost:3000";
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
                fonts.AddFont("lucide.ttf", "Lucide");
            });

#if ANDROID
        builder.ConfigureMauiHandlers(handlers =>
            handlers.AddHandler<waterfall.UI.Controls.SectionPagerView, waterfall.UI.Platforms.Android.Handlers.SectionPagerViewHandler>());
#endif

        builder.Services.AddSingleton<ThemeService>();

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(SecureStorage.Default);
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.IUrlLauncher, MauiUrlLauncher>();
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.IFileDialogService, MauiFileDialogService>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.ILocalGeoCacheService, Waterfall.Client.Services.EfLocalGeoCacheService>();
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.ILocalVacancyStore>(_ =>
            new Waterfall.Client.Services.EfLocalVacancyStore());
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.IOfflineSiteCollector>(sp =>
            new Waterfall.Client.Services.OfflineSiteCollector(sp.GetRequiredService<IHttpClientFactory>().CreateClient()));
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.IDistanceCalculatorService>(sp =>
            new Waterfall.Client.Services.GeoDistanceService(
                sp.GetService<Waterfall.Client.Interfaces.IHttpGatewayService>(),
                sp.GetService<Waterfall.Client.Interfaces.ILocalGeoCacheService>()));
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.IAddressAutocompleteService, Waterfall.Client.Services.PhotonAddressAutocompleteService>();
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.INavigationService, ShellNavigationService>();
        builder.Services.AddWaterfallClient<SecureStorageTokenStore>(new Uri(ApiBaseUrl));
        builder.Services.AddSingleton<Waterfall.Client.Interfaces.IVacancySyncService, Waterfall.Client.Services.VacancySyncService>();

        // ViewModels лишаються Singleton на всіх платформах (перемикання вкладок зберігає дані та фільтри).
        builder.Services.AddSingleton<CatalogViewModel>();
        builder.Services.AddSingleton<DatabaseViewModel>();
        builder.Services.AddSingleton<RecommendationsViewModel>();
        builder.Services.AddSingleton<AIConsultantViewModel>();
        builder.Services.AddSingleton<AnalyticsViewModel>();
        builder.Services.AddSingleton<FavoritesViewModel>();
        builder.Services.AddSingleton<LoginViewModel>();
        builder.Services.AddSingleton(sp => new SystemSettingsViewModel(
            sp.GetService<Waterfall.Client.Interfaces.ILocalVacancyStore>(),
            sp.GetService<Waterfall.Client.Interfaces.IVacancySyncService>(),
            sp.GetService<Waterfall.Client.Interfaces.IDistanceCalculatorService>()));

#if WINDOWS
        // На Windows DesktopHostPage тримає сторінки живими в RAM для миттєвого перемикання (<16ms).
        builder.Services.AddSingleton<CatalogPage>();
        builder.Services.AddSingleton<DatabasePage>();
        builder.Services.AddSingleton<RecommendationsPage>();
        builder.Services.AddSingleton<AIConsultantPage>();
        builder.Services.AddSingleton<AnalyticsPage>();
        builder.Services.AddSingleton<FavoritesPage>();
        builder.Services.AddSingleton<LoginPage>();
        builder.Services.AddSingleton<SystemSettingsPage>();
#else
        // На мобільних платформах Transient-сторінки уникають конфлікту повторного прикріплення
        // при перебудові навігації в PhoneHostPage (зміна ролі Аналітик ↔ Шукач).
        builder.Services.AddTransient<CatalogPage>();
        builder.Services.AddTransient<DatabasePage>();
        builder.Services.AddTransient<RecommendationsPage>();
        builder.Services.AddTransient<AIConsultantPage>();
        builder.Services.AddTransient<AnalyticsPage>();
        builder.Services.AddTransient<FavoritesPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<SystemSettingsPage>();
#endif

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
            handler.PlatformView.SetPadding(0, 0, 0, 0);
            handler.PlatformView.SetMinHeight(0);
            handler.PlatformView.SetMinimumHeight(0);
            handler.PlatformView.Gravity =
                (handler.PlatformView.Gravity & Android.Views.GravityFlags.HorizontalGravityMask)
                | Android.Views.GravityFlags.CenterVertical;
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
            int plateId = handler.PlatformView.Resources?.GetIdentifier("android:id/search_plate", null, null) ?? 0;
            if (plateId != 0 && handler.PlatformView.FindViewById(plateId) is Android.Views.View plate)
            {
                plate.Background = null;
                plate.SetBackgroundColor(Android.Graphics.Color.Transparent);
            }
#endif
        });

        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("RemoveNativeBorder", (handler, view) =>
        {
#if WINDOWS
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
#elif ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
            handler.PlatformView.Gravity = Android.Views.GravityFlags.CenterVertical;
#elif IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });

#if ANDROID
        Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping("FastScroll", (handler, view) =>
        {
            if (handler.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                rv.SetItemViewCacheSize(12);
                rv.SetItemAnimator(null);
                rv.OverScrollMode = Android.Views.OverScrollMode.Always;
            }
        });

        Microsoft.Maui.Handlers.ScrollViewHandler.Mapper.AppendToMapping("StretchOverscroll", (handler, view) =>
        {
            if (view.Orientation != ScrollOrientation.Horizontal)
            {
                handler.PlatformView.OverScrollMode = Android.Views.OverScrollMode.Always;
            }
        });
#endif

        MauiApp app = builder.Build();

        // Створюємо ШІ-консультант одразу, щоб кнопка «ШІ-аналіз» у каталозі працювала
        // навіть якщо вкладка «ШІ-чат» ще жодного разу не відкривалася.
        app.Services.GetRequiredService<AIConsultantViewModel>();

        return app;
    }
}
