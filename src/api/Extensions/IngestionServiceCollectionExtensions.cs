// AI: реєстрація фонового збору вакансій у DI (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, ручний запуск API, самоперевірка автора (§8)

using Microsoft.Extensions.DependencyInjection.Extensions;
using Waterfall.Api.Workers;
using Waterfall.Services.Ingestion;

namespace Waterfall.Api.Extensions;

/// <summary>
/// Реєструє фоновий збір вакансій: налаштування, джерело, парсер, нормалізатор, сервіс циклу й воркер.
/// </summary>
public static class IngestionServiceCollectionExtensions
{
    public static IServiceCollection AddVacancyIngestion(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(IngestionOptions.SectionName))
            .PostConfigure(options =>
            {
                // Відносний шлях рахуємо від кореня вмісту, як і шлях до docs/api в OpenApiGenerator
                if (!string.IsNullOrWhiteSpace(options.RawDataDirectory))
                {
                    options.RawDataDirectory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.RawDataDirectory));
                }
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.RawDataDirectory), "Ingestion:RawDataDirectory не задано.")
            .Validate(options => options.Interval >= TimeSpan.FromMinutes(1), "Ingestion:Interval має бути щонайменше 1 хвилина.")
            .Validate(options => options.InitialDelay >= TimeSpan.Zero, "Ingestion:InitialDelay не може бути від'ємним.")
            .Validate(options => options.MaxFileSizeBytes > 0, "Ingestion:MaxFileSizeBytes має бути додатним.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IRawVacancySource, FileRawVacancySource>();
        services.AddSingleton<IRawVacancyParser, DjinniRawJsonParser>();
        services.AddSingleton<VacancyNormalizer>();
        services.AddScoped<IVacancyIngestionService, VacancyIngestionService>();
        services.AddHostedService<VacancyIngestionWorker>();

        return services;
    }
}
