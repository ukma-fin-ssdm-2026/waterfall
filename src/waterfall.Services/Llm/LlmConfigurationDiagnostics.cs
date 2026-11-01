// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Waterfall.Services.Llm;

/// <summary>
/// Одноразова перевірка на старті: попереджає, яких ключів бракує і для яких ролей
/// немає жодної придатної ланки. Це саме попередження, а не помилка: API мусить
/// запускатися без ключів (REL-003), а ШІ-функції просто повідомлять про недоступність.
/// </summary>
internal sealed class LlmConfigurationDiagnostics : IHostedService
{
    private readonly IOptions<LlmOptions> _options;
    private readonly ILogger<LlmConfigurationDiagnostics> _logger;

    public LlmConfigurationDiagnostics(IOptions<LlmOptions> options, ILogger<LlmConfigurationDiagnostics> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LlmOptions options = _options.Value;

        if (options.Providers.Count == 0)
        {
            _logger.LogWarning("LLM: секція Llm:Providers порожня; усі ШІ-функції повідомлятимуть про недоступність");
        }

        foreach ((string providerName, LlmProviderOptions provider) in options.Providers)
        {
            if (LlmApiKeyResolver.Resolve(provider) is null)
            {
                _logger.LogWarning(
                    "LLM: для провайдера {Provider} не задано ключ (змінна середовища {ApiKeyEnv} або Llm:Providers:{Provider}:ApiKey); його ланки пропускатимуться",
                    providerName,
                    provider.ApiKeyEnv ?? "не задана",
                    providerName);
            }
        }

        foreach (LlmRole role in Enum.GetValues<LlmRole>())
        {
            if (!LlmOptionsLookup.TryGetRole(options, role, out LlmRoleOptions roleOptions) || roleOptions.Chain.Count == 0)
            {
                _logger.LogWarning("LLM: для ролі {Role} не задано ланцюжок провайдерів", role);
                continue;
            }

            int usableEntries = roleOptions.Chain.Count(entry =>
                LlmOptionsLookup.TryGetProvider(options, entry.Provider, out LlmProviderOptions provider)
                && LlmApiKeyResolver.Resolve(provider) is not null);

            if (usableEntries == 0)
            {
                _logger.LogWarning(
                    "LLM: для ролі {Role} немає жодної ланки з ключем; ШІ-функції цієї ролі будуть недоступні",
                    role);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
