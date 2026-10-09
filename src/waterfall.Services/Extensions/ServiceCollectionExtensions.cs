// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Waterfall.Repository.Data;
using Waterfall.Repository.Interfaces;
using Waterfall.Repository.Repositories;
using Waterfall.Services.Interfaces;

namespace Waterfall.Services.Extensions;

/// <summary>
/// Реєстрація сервісів бізнес-логіки, доступу до даних (EF Core) та кешування у DI-контейнері.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWaterfallServices(
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

        // 1. Репозиторії БД (працюють виключно із сутностями)
        services.AddScoped<IVacancyRepository, EfVacancyRepository>();
        services.AddScoped<ISavedVacancyRepository, EfSavedVacancyRepository>();

        // 2. Кешування
        services.AddMemoryCache();
        services.AddSingleton<IVacancyCacheService, VacancyCacheService>();

        // 3. Сервіси бізнес-логіки (Працюють з DTO та кешем)
        services.AddScoped<IVacancyService, VacancyService>();
        services.AddScoped<ISavedVacancyService, SavedVacancyService>();

        return services;
    }
}
