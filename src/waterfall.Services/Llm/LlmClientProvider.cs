// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Waterfall.Services.Llm;

/// <summary>
/// Будує й кешує клієнти ланок за секцією <c>Llm</c>. Ланки без ключа пропускаються
/// (лише з попередженням у журналі), тож застосунок стартує без жодного ключа (REL-003).
/// </summary>
public sealed class LlmClientProvider : ILlmClientProvider, IDisposable
{
    private readonly IOptions<LlmOptions> _options;
    private readonly ILogger<LlmClientProvider> _logger;
    private readonly ILogger<FallbackChatClient> _fallbackLogger;
    private readonly ConcurrentDictionary<LlmRole, Lazy<LlmChatAttempt[]>> _attemptsByRole = new();
    private bool _disposed;

    /// <summary>Створює провайдер клієнтів; зазвичай реєструється через <c>AddWaterfallLlm</c>.</summary>
    /// <param name="options">Прив'язана секція <c>Llm</c>.</param>
    /// <param name="logger">Журнал пропущених ланок (без ключів).</param>
    /// <param name="fallbackLogger">Журнал спроб і резервування.</param>
    public LlmClientProvider(
        IOptions<LlmOptions> options,
        ILogger<LlmClientProvider> logger,
        ILogger<FallbackChatClient> fallbackLogger)
    {
        _options = options;
        _logger = logger;
        _fallbackLogger = fallbackLogger;
    }

    /// <inheritdoc/>
    public IChatClient GetClient(LlmRole role)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        LlmChatAttempt[] attempts = GetAttempts(role);
        LlmOptions options = _options.Value;

        // Обгортка на кожен запит не володіє спільними клієнтами ланок: якщо викликач
        // зробить using, наступні запити не зламаються.
        return new FallbackChatClient(
            role,
            attempts,
            options.AttemptTimeout,
            options.TotalTimeout,
            _fallbackLogger,
            disposeAttempts: false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Lazy<LlmChatAttempt[]> attempts in _attemptsByRole.Values)
        {
            if (!attempts.IsValueCreated)
            {
                continue;
            }

            foreach (LlmChatAttempt attempt in attempts.Value)
            {
                attempt.Client.Dispose();
            }
        }
    }

    private LlmChatAttempt[] GetAttempts(LlmRole role)
    {
        Lazy<LlmChatAttempt[]> lazyAttempts = _attemptsByRole.GetOrAdd(
            role,
            key => new Lazy<LlmChatAttempt[]>(() => CreateAttempts(key), LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyAttempts.Value;
    }

    private LlmChatAttempt[] CreateAttempts(LlmRole role)
    {
        LlmOptions options = _options.Value;
        List<LlmChatAttempt> attempts = [];

        if (!LlmOptionsLookup.TryGetRole(options, role, out LlmRoleOptions roleOptions) || roleOptions.Chain.Count == 0)
        {
            _logger.LogWarning(
                "LLM: для ролі {Role} не задано ланцюжок провайдерів; ШІ-функції цієї ролі повідомлятимуть про недоступність",
                role);

            return [];
        }

        foreach (LlmChainEntryOptions entry in roleOptions.Chain)
        {
            if (!LlmOptionsLookup.TryGetProvider(options, entry.Provider, out LlmProviderOptions provider))
            {
                // Структурну помилку вже відхилив LlmOptionsValidator; тут лише захист від обходу валідації.
                _logger.LogError(
                    "LLM: роль {Role} посилається на невідомого провайдера {Provider}; ланку пропущено",
                    role,
                    entry.Provider);

                continue;
            }

            string? apiKey = LlmApiKeyResolver.Resolve(provider);
            if (apiKey is null)
            {
                _logger.LogWarning(
                    "LLM: провайдер {Provider} для ролі {Role} пропущено — немає ключа ({ApiKeyEnv} або Llm:Providers:{Provider}:ApiKey)",
                    entry.Provider,
                    role,
                    provider.ApiKeyEnv ?? "змінна середовища не задана",
                    entry.Provider);

                continue;
            }

            attempts.Add(new LlmChatAttempt(entry.Provider, entry.Model, LlmClientFactory.Create(provider, entry.Model, apiKey)));
        }

        if (attempts.Count == 0)
        {
            _logger.LogWarning(
                "LLM: для ролі {Role} немає жодної придатної ланки; запит отримає LlmUnavailableException(NotConfigured)",
                role);
        }

        return [.. attempts];
    }
}
