// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Waterfall.Repository.Data;
using Waterfall.Repository.Interfaces;
using Waterfall.Repository.Repositories;

namespace Waterfall.Repository.Extensions;

/// <summary>
/// Реєстрація контексту бази даних (EF Core) та репозиторіїв у DI-контейнері.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Реєструє <see cref="WaterfallDbContext"/> (з підтримкою SQLite або SQL Server)
    /// та реалізації репозиторіїв доступу до даних (<see cref="IVacancyRepository"/>,
    /// <see cref="ISavedVacancyRepository"/>, <see cref="IUserRepository"/>).
    /// </summary>
    /// <param name="services">Колекція сервісів DI-контейнера.</param>
    /// <param name="configuration">Конфігурація застосунку (секції DatabaseProvider та ConnectionStrings:DefaultConnection).</param>
    public static IServiceCollection AddWaterfallRepositories(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string provider = configuration["DatabaseProvider"] ?? "Sqlite";
        string connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=waterfall.db";

        services.AddDbContext<WaterfallDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ||
                provider.Equals("Mssql", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString);
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });

        services.AddScoped<IVacancyRepository, EfVacancyRepository>();
        services.AddScoped<ISavedVacancyRepository, EfSavedVacancyRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IGeoLocationCacheRepository, EfGeoLocationCacheRepository>();

        return services;
    }
}
