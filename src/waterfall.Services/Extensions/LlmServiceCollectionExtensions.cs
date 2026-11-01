// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Waterfall.Services.Llm;

namespace Waterfall.Services.Extensions;

/// <summary>
/// Реєстрація LLM-шлюзу (FR-002, REL-003) у DI-контейнері.
/// </summary>
public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Додає LLM-шлюз: прив'язує секцію <c>Llm</c>, вмикає перевірку структури на старті,
    /// реєструє <see cref="ILlmClientProvider"/> та попередження про відсутні ключі.
    /// Ключі читаються лише зі змінних середовища або User Secrets, тому без них API
    /// усе одно запускається (REL-003).
    /// </summary>
    /// <param name="services">Колекція сервісів DI-контейнера.</param>
    /// <param name="configuration">Конфігурація застосунку.</param>
    /// <returns>Та сама колекція для ланцюжкових викликів.</returns>
    public static IServiceCollection AddWaterfallLlm(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();
        services.AddSingleton<ILlmClientProvider, LlmClientProvider>();
        services.AddHostedService<LlmConfigurationDiagnostics>();

        return services;
    }
}
