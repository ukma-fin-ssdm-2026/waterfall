// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ClientModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Waterfall.Services.Llm;

/// <summary>
/// Виконує запит по ланцюжку провайдерів ролі. Перехід до наступної ланки дозволений
/// лише на таймаут спроби, HTTP 429, HTTP 5xx або збій мережі; 400/401/403 та інші
/// помилки клієнта повертаються без резервування — це наша помилка або неправильний ключ,
/// і повторення в іншого провайдера її не виправить.
/// <para>
/// <see cref="LlmOptions.TotalTimeout"/> обмежує весь ланцюжок (REL-003), тож користувач
/// отримує відмову не пізніше ніж за 30 с. У потоці перемикання можливе лише до першого
/// відданого клієнту фрагмента: зшивати текст двох провайдерів заборонено.
/// </para>
/// <para>
/// Журналюються лише роль, провайдер, модель, статус і тривалість — ніколи запит або
/// відповідь (THR-06, THR-08).
/// </para>
/// </summary>
public sealed class FallbackChatClient : IChatClient
{
    private readonly LlmRole _role;
    private readonly LlmChatAttempt[] _attempts;
    private readonly TimeSpan _attemptTimeout;
    private readonly TimeSpan _totalTimeout;
    private readonly ILogger<FallbackChatClient> _logger;
    private readonly bool _disposeAttempts;

    /// <summary>Створює клієнт ланцюжка.</summary>
    /// <param name="role">Роль, якій належить ланцюжок.</param>
    /// <param name="attempts">Ланки в порядку спроб; порожній список означає «не налаштовано».</param>
    /// <param name="attemptTimeout">Граничний час однієї спроби.</param>
    /// <param name="totalTimeout">Граничний час усього ланцюжка.</param>
    /// <param name="logger">Журнал без тексту запитів і відповідей.</param>
    /// <param name="disposeAttempts">
    /// Чи володіє цей екземпляр клієнтами ланок. Провайдер передає спільні кешовані
    /// клієнти й виставляє <c>false</c>, щоб передчасний <see cref="Dispose"/> одного
    /// запиту не зламав наступні.
    /// </param>
    public FallbackChatClient(
        LlmRole role,
        IEnumerable<LlmChatAttempt> attempts,
        TimeSpan attemptTimeout,
        TimeSpan totalTimeout,
        ILogger<FallbackChatClient> logger,
        bool disposeAttempts = true)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(logger);

        _role = role;
        _attempts = attempts.ToArray();
        _attemptTimeout = attemptTimeout;
        _totalTimeout = totalTimeout;
        _logger = logger;
        _disposeAttempts = disposeAttempts;
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ChatMessage[] messageArray = messages as ChatMessage[] ?? messages.ToArray();

        if (_attempts.Length == 0)
        {
            throw CreateUnavailable(LlmFailureKind.NotConfigured, null);
        }

        using CancellationTokenSource totalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalCts.CancelAfter(_totalTimeout);

        LlmFailureKind lastKind = LlmFailureKind.NotConfigured;
        Exception? lastError = null;

        foreach (LlmChatAttempt attempt in _attempts)
        {
            if (totalCts.IsCancellationRequested)
            {
                throw CreateUnavailable(LlmFailureKind.Timeout, lastError);
            }

            ChatOptions attemptOptions = BuildOptions(options, attempt.Model);
            using CancellationTokenSource attemptCts = CancellationTokenSource.CreateLinkedTokenSource(totalCts.Token);
            attemptCts.CancelAfter(_attemptTimeout);

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                ChatResponse response = await attempt.Client
                    .GetResponseAsync(messageArray, attemptOptions, attemptCts.Token)
                    .ConfigureAwait(false);

                LogSuccess(attempt, stopwatch);
                return response;
            }
            catch (Exception exception)
            {
                // Скасування викликачем — не збій провайдера: не резервуємо й не підмінюємо виняток.
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                LlmFailureKind? kind = ClassifyFailure(exception);
                if (kind is null)
                {
                    throw;
                }

                lastKind = kind.Value;
                lastError = exception;
                LogFailure(attempt, kind.Value, stopwatch);

                if (totalCts.IsCancellationRequested)
                {
                    throw CreateUnavailable(LlmFailureKind.Timeout, exception);
                }
            }
        }

        throw CreateUnavailable(totalCts.IsCancellationRequested ? LlmFailureKind.Timeout : lastKind, lastError);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ChatMessage[] messageArray = messages as ChatMessage[] ?? messages.ToArray();

        if (_attempts.Length == 0)
        {
            throw CreateUnavailable(LlmFailureKind.NotConfigured, null);
        }

        using CancellationTokenSource totalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalCts.CancelAfter(_totalTimeout);

        LlmFailureKind lastKind = LlmFailureKind.NotConfigured;
        Exception? lastError = null;
        bool anyUpdateYielded = false;

        foreach (LlmChatAttempt attempt in _attempts)
        {
            if (totalCts.IsCancellationRequested)
            {
                throw CreateUnavailable(LlmFailureKind.Timeout, lastError);
            }

            ChatOptions attemptOptions = BuildOptions(options, attempt.Model);
            using CancellationTokenSource attemptCts = CancellationTokenSource.CreateLinkedTokenSource(totalCts.Token);
            attemptCts.CancelAfter(_attemptTimeout);

            Stopwatch stopwatch = Stopwatch.StartNew();
            IAsyncEnumerator<ChatResponseUpdate> enumerator;
            try
            {
                enumerator = attempt.Client
                    .GetStreamingResponseAsync(messageArray, attemptOptions, attemptCts.Token)
                    .GetAsyncEnumerator(attemptCts.Token);
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                LlmFailureKind? creationKind = ClassifyFailure(exception);
                if (creationKind is null)
                {
                    throw;
                }

                lastKind = creationKind.Value;
                lastError = exception;
                LogFailure(attempt, creationKind.Value, stopwatch);

                if (anyUpdateYielded || totalCts.IsCancellationRequested)
                {
                    throw CreateUnavailable(
                        totalCts.IsCancellationRequested ? LlmFailureKind.Timeout : lastKind,
                        lastError);
                }

                continue;
            }

            bool failed = false;
            try
            {
                while (true)
                {
                    bool hasNext;
                    Exception? failure = null;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        hasNext = false;
                    }

                    if (failure is not null)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            Rethrow(failure);
                        }

                        LlmFailureKind? kind = ClassifyFailure(failure);
                        if (kind is null)
                        {
                            Rethrow(failure);
                        }

                        lastKind = kind.Value;
                        lastError = failure;
                        LogFailure(attempt, kind.Value, stopwatch);
                        failed = true;
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    anyUpdateYielded = true;
                    yield return enumerator.Current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            if (!failed)
            {
                LogSuccess(attempt, stopwatch);
                yield break;
            }

            if (anyUpdateYielded)
            {
                // Клієнт уже бачив частину відповіді: перемикання «на льоту» дало б зшитий текст.
                throw CreateUnavailable(totalCts.IsCancellationRequested ? LlmFailureKind.Timeout : lastKind, lastError);
            }
        }

        throw CreateUnavailable(totalCts.IsCancellationRequested ? LlmFailureKind.Timeout : lastKind, lastError);
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceType == typeof(FallbackChatClient))
        {
            return this;
        }

        foreach (LlmChatAttempt attempt in _attempts)
        {
            object? service = attempt.Client.GetService(serviceType, serviceKey);
            if (service is not null)
            {
                return service;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposeAttempts)
        {
            return;
        }

        foreach (LlmChatAttempt attempt in _attempts)
        {
            attempt.Client.Dispose();
        }
    }

    private static ChatOptions BuildOptions(ChatOptions? options, string model)
    {
        ChatOptions attemptOptions = options?.Clone() ?? new ChatOptions();
        attemptOptions.ModelId = model;
        return attemptOptions;
    }

    private static LlmFailureKind? ClassifyFailure(Exception exception)
    {
        switch (exception)
        {
            case OperationCanceledException:
                return LlmFailureKind.Timeout;

            case ClientResultException openAiException:
                return ClassifyStatus(openAiException.Status);

            case AnthropicApiException anthropicException:
                return ClassifyStatus((int)anthropicException.StatusCode);

            case AnthropicIOException:
                return LlmFailureKind.ServerError;

            case HttpRequestException networkException:
                return networkException.StatusCode is HttpStatusCode status
                    ? ClassifyStatus((int)status)
                    : LlmFailureKind.ServerError;

            default:
                return null;
        }
    }

    private static LlmFailureKind? ClassifyStatus(int status) => status switch
    {
        429 => LlmFailureKind.RateLimited,
        >= 500 and <= 599 => LlmFailureKind.ServerError,
        0 => LlmFailureKind.ServerError,
        _ => null
    };

    [DoesNotReturn]
    private static void Rethrow(Exception exception)
    {
        ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private void LogSuccess(LlmChatAttempt attempt, Stopwatch stopwatch)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        _logger.LogInformation(
            "LLM: роль {Role}, провайдер {Provider}, модель {Model}, статус ok, {ElapsedMs} мс",
            _role,
            attempt.Provider,
            attempt.Model,
            stopwatch.ElapsedMilliseconds);
    }

    private void LogFailure(LlmChatAttempt attempt, LlmFailureKind kind, Stopwatch stopwatch)
    {
        _logger.LogWarning(
            "LLM: роль {Role}, провайдер {Provider}, модель {Model}, статус {Status}, {ElapsedMs} мс",
            _role,
            attempt.Provider,
            attempt.Model,
            kind,
            stopwatch.ElapsedMilliseconds);
    }

    private LlmUnavailableException CreateUnavailable(LlmFailureKind kind, Exception? innerException)
    {
        return new LlmUnavailableException(kind, $"LLM недоступна для ролі {_role}.", innerException);
    }
}
