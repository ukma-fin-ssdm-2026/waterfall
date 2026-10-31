// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.DependencyInjection;
using Waterfall.Services.Interfaces;

namespace Waterfall.Services.Extensions;

/// <summary>
/// Реєстрація сервісів бізнес-логіки та кешування у DI-контейнері.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Реєструє кешування (In-Memory) та сервіси бізнес-логіки (<see cref="IVacancyService"/>,
    /// <see cref="ISavedVacancyService"/>, <see cref="IVacancyIngestionService"/>) у DI-контейнері.
    /// </summary>
    /// <param name="services">Колекція сервісів DI-контейнера.</param>
    public static IServiceCollection AddWaterfallServices(this IServiceCollection services)
    {
        // 1. Кешування
        services.AddMemoryCache();
        services.AddSingleton<IVacancyCacheService, VacancyCacheService>();

        // 2. Сервіси бізнес-логіки (працюють з DTO та кешем)
        services.AddScoped<IVacancyService, VacancyService>();
        services.AddScoped<ISavedVacancyService, SavedVacancyService>();
        services.AddScoped<IVacancyIngestionService, VacancyIngestionService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IGeoLocationService, GeoLocationService>();
        services.AddScoped<IUserStatsService, UserStatsService>();

        return services;
    }
}
