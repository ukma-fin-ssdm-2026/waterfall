// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Waterfall.Services.Tests;

/// <summary>
/// Керований <see cref="IChatClient"/> для тестів шлюзу: кожен виклик знімає з черги
/// один запрограмований крок (відповідь, потік фрагментів, затримку або виняток).
/// Записує модель і повідомлення кожного виклику, щоб перевіряти порядок ланцюжка
/// та підстановку моделі.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ScriptedStep> _steps = [];
    private readonly List<ScriptedChatCall> _calls = [];

    /// <summary>Виклики в порядку надходження.</summary>
    public IReadOnlyList<ScriptedChatCall> Calls => _calls;

    /// <summary>Кількість виконаних викликів.</summary>
    public int CallCount => _calls.Count;

    /// <summary>Скільки кроків сценарію ще не використано.</summary>
    public int RemainingSteps => _steps.Count;

    /// <summary>Додає крок «повернути текст».</summary>
    public ScriptedChatClient Respond(string text)
    {
        _steps.Enqueue(new ScriptedStep { Text = text });
        return this;
    }

    /// <summary>Додає крок «зачекати, потім повернути текст».</summary>
    public ScriptedChatClient RespondWithDelay(TimeSpan delay, string text)
    {
        _steps.Enqueue(new ScriptedStep { Text = text, Delay = delay });
        return this;
    }

    /// <summary>Додає крок «кинути виняток».</summary>
    public ScriptedChatClient Throw(Exception exception)
    {
        _steps.Enqueue(new ScriptedStep { Error = exception });
        return this;
    }

    /// <summary>Додає крок «зачекати, потім кинути виняток».</summary>
    public ScriptedChatClient ThrowAfterDelay(TimeSpan delay, Exception exception)
    {
        _steps.Enqueue(new ScriptedStep { Error = exception, Delay = delay });
        return this;
    }

    /// <summary>Додає потік із вказаних фрагментів.</summary>
    public ScriptedChatClient Stream(params string[] chunks)
    {
        _steps.Enqueue(new ScriptedStep { Chunks = chunks });
        return this;
    }

    /// <summary>Додає потік, який після фрагментів кидає виняток (порожні фрагменти — до першого).</summary>
    public ScriptedChatClient StreamThenThrow(Exception exception, params string[] chunks)
    {
        _steps.Enqueue(new ScriptedStep { Chunks = chunks, Error = exception });
        return this;
    }

    /// <summary>Додає потік, який починається після затримки.</summary>
    public ScriptedChatClient StreamWithDelay(TimeSpan delay, params string[] chunks)
    {
        _steps.Enqueue(new ScriptedStep { Chunks = chunks, Delay = delay });
        return this;
    }

    /// <summary>Додає потік: фрагменти, затримка, потім виняток (затримка посеред відповіді).</summary>
    public ScriptedChatClient StreamThenDelayThrow(TimeSpan delay, Exception exception, params string[] chunks)
    {
        _steps.Enqueue(new ScriptedStep { Chunks = chunks, Error = exception, DelayAfterChunks = delay });
        return this;
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        RecordCall(messages, options, streaming: false);
        ScriptedStep step = Dequeue();
        await DelayAsync(step.Delay, cancellationToken).ConfigureAwait(false);

        if (step.Error is not null)
        {
            throw step.Error;
        }

        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, step.Text ?? string.Concat(step.Chunks)));
        response.ModelId = options?.ModelId;
        return response;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RecordCall(messages, options, streaming: true);
        ScriptedStep step = Dequeue();
        await DelayAsync(step.Delay, cancellationToken).ConfigureAwait(false);

        foreach (string chunk in step.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk) { ModelId = options?.ModelId };
        }

        await DelayAsync(step.DelayAfterChunks, cancellationToken).ConfigureAwait(false);
        if (step.Error is not null)
        {
            throw step.Error;
        }
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private void RecordCall(IEnumerable<ChatMessage> messages, ChatOptions? options, bool streaming)
    {
        _calls.Add(new ScriptedChatCall(streaming, options?.ModelId, messages.ToArray()));
    }

    private ScriptedStep Dequeue()
    {
        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("ScriptedChatClient: сценарій вичерпано, але надійшов ще один виклик.");
        }

        return _steps.Dequeue();
    }

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;

    private sealed class ScriptedStep
    {
        public TimeSpan Delay { get; init; }

        public TimeSpan DelayAfterChunks { get; init; }

        public string? Text { get; init; }

        public string[] Chunks { get; init; } = [];

        public Exception? Error { get; init; }
    }
}

/// <summary>Запис одного виклику <see cref="ScriptedChatClient"/>.</summary>
/// <param name="Streaming">Чи це потоковий виклик.</param>
/// <param name="ModelId">Модель, яку отримав клієнт у <see cref="ChatOptions"/>.</param>
/// <param name="Messages">Повідомлення запиту.</param>
public sealed record ScriptedChatCall(bool Streaming, string? ModelId, IReadOnlyList<ChatMessage> Messages);
