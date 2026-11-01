// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Services.Llm;

namespace Waterfall.Services.Tests;

/// <summary>
/// Поведінка ланцюжка резервування (FR-002, REL-003): коли перемикатися на наступного
/// провайдера, коли ні, як працює загальний таймаут і як поводиться потік.
/// </summary>
public sealed class FallbackChatClientTests
{
    private static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task GetResponseAsync_OnOpenAiRateLimit_FallsBackToNextProvider()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient().Throw(LlmTestExceptions.OpenAiStatus(429));
        ScriptedChatClient second = new ScriptedChatClient().Respond("друга відповідь");
        FallbackChatClient client = CreateClient(first, second);

        // Act
        ChatResponse response = await client.GetResponseAsync(UserMessages());

        // Assert
        Assert.Equal("друга відповідь", response.Text);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_OnAnthropicServerError_FallsBackToNextProvider()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient()
            .Throw(LlmTestExceptions.AnthropicStatus(System.Net.HttpStatusCode.ServiceUnavailable));
        ScriptedChatClient second = new ScriptedChatClient().Respond("ok");
        FallbackChatClient client = CreateClient(first, second);

        // Act
        ChatResponse response = await client.GetResponseAsync(UserMessages());

        // Assert
        Assert.Equal("ok", response.Text);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_OnNetworkFailure_FallsBackToNextProvider()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient().Throw(new HttpRequestException("мережа недоступна"));
        ScriptedChatClient second = new ScriptedChatClient().Respond("ok");
        FallbackChatClient client = CreateClient(first, second);

        // Act
        ChatResponse response = await client.GetResponseAsync(UserMessages());

        // Assert
        Assert.Equal("ok", response.Text);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_OnAttemptTimeout_FallsBackToNextProvider()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient()
            .RespondWithDelay(TimeSpan.FromSeconds(5), "запізно");
        ScriptedChatClient second = new ScriptedChatClient().Respond("встигла");
        FallbackChatClient client = CreateClient(
            TimeSpan.FromMilliseconds(80),
            TimeSpan.FromSeconds(5),
            first,
            second);

        // Act
        ChatResponse response = await client.GetResponseAsync(UserMessages());

        // Assert
        Assert.Equal("встигла", response.Text);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    public async Task GetResponseAsync_OnClientError_DoesNotFallBack(int status)
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient().Throw(LlmTestExceptions.OpenAiStatus(status));
        ScriptedChatClient second = new ScriptedChatClient().Respond("не має викликатися");
        FallbackChatClient client = CreateClient(first, second);

        // Act
        ClientResultException exception = await Assert.ThrowsAsync<ClientResultException>(
            () => client.GetResponseAsync(UserMessages()));

        // Assert
        Assert.Equal(status, exception.Status);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_WhenEveryProviderFails_ThrowsUnavailableWithLastKind()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient().Throw(LlmTestExceptions.OpenAiStatus(429));
        ScriptedChatClient second = new ScriptedChatClient().Throw(LlmTestExceptions.OpenAiStatus(503));
        FallbackChatClient client = CreateClient(first, second);

        // Act
        LlmUnavailableException exception = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.GetResponseAsync(UserMessages()));

        // Assert
        Assert.Equal(LlmFailureKind.ServerError, exception.Kind);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_TotalTimeout_CapsWholeChain()
    {
        // Arrange: кожна спроба могла б чекати 10 с, але весь ланцюжок обмежений 150 мс
        ScriptedChatClient first = new ScriptedChatClient()
            .RespondWithDelay(TimeSpan.FromSeconds(5), "запізно");
        ScriptedChatClient second = new ScriptedChatClient().Respond("теж запізно");
        FallbackChatClient client = CreateClient(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(150),
            first,
            second);

        Stopwatch stopwatch = Stopwatch.StartNew();

        // Act
        LlmUnavailableException exception = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.GetResponseAsync(UserMessages()));

        // Assert
        Assert.Equal(LlmFailureKind.Timeout, exception.Kind);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"ланцюжок тривав {stopwatch.Elapsed}");
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetResponseAsync_EmptyChain_ThrowsNotConfigured()
    {
        // Arrange
        FallbackChatClient client = CreateClient();

        // Act
        LlmUnavailableException exception = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.GetResponseAsync(UserMessages()));

        // Assert
        Assert.Equal(LlmFailureKind.NotConfigured, exception.Kind);
    }

    [Fact]
    public async Task GetResponseAsync_AppliesChainModelPerAttempt_WithoutMutatingCallerOptions()
    {
        // Arrange
        ScriptedChatClient inner = new ScriptedChatClient().Respond("ok");
        FallbackChatClient client = CreateClient(inner);
        ChatOptions callerOptions = new()
        {
            ModelId = "caller-model",
            Temperature = 0.2f
        };

        // Act
        await client.GetResponseAsync(UserMessages(), callerOptions);

        // Assert
        Assert.Equal("model-0", inner.Calls[0].ModelId);
        Assert.Equal("caller-model", callerOptions.ModelId);
        Assert.Equal(0.2f, callerOptions.Temperature!.Value);
    }

    [Fact]
    public async Task GetResponseAsync_WhenCallerCancels_PropagatesWithoutFallback()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient()
            .RespondWithDelay(TimeSpan.FromSeconds(5), "запізно");
        ScriptedChatClient second = new ScriptedChatClient().Respond("не має викликатися");
        FallbackChatClient client = CreateClient(first, second);
        using CancellationTokenSource cts = new(TimeSpan.FromMilliseconds(80));

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetResponseAsync(UserMessages(), null, cts.Token));

        // Assert
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FallsBackBeforeFirstUpdate_AndUsesNextModel()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient()
            .StreamThenThrow(LlmTestExceptions.OpenAiStatus(500));
        ScriptedChatClient second = new ScriptedChatClient().Stream("при", "віт");
        FallbackChatClient client = CreateClient(first, second);

        // Act
        (List<ChatResponseUpdate> updates, Exception? error) = await ReadStreamAsync(
            client.GetStreamingResponseAsync(UserMessages()));

        // Assert
        Assert.Null(error);
        Assert.Equal("привіт", string.Concat(updates.Select(update => update.Text)));
        Assert.Equal(1, second.CallCount);
        Assert.Equal("model-1", second.Calls[0].ModelId);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AfterFirstUpdate_PropagatesServerErrorWithoutSwitching()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient()
            .StreamThenThrow(LlmTestExceptions.OpenAiStatus(500), "початок");
        ScriptedChatClient second = new ScriptedChatClient().Stream("інше");
        FallbackChatClient client = CreateClient(first, second);

        // Act
        (List<ChatResponseUpdate> updates, Exception? error) = await ReadStreamAsync(
            client.GetStreamingResponseAsync(UserMessages()));

        // Assert
        ChatResponseUpdate update = Assert.Single(updates);
        Assert.Equal("початок", update.Text);
        LlmUnavailableException exception = Assert.IsType<LlmUnavailableException>(error);
        Assert.Equal(LlmFailureKind.ServerError, exception.Kind);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AfterFirstUpdate_ClientErrorPropagatesUnchanged()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient()
            .StreamThenThrow(LlmTestExceptions.OpenAiStatus(400), "початок");
        ScriptedChatClient second = new ScriptedChatClient().Stream("інше");
        FallbackChatClient client = CreateClient(first, second);

        // Act
        (List<ChatResponseUpdate> updates, Exception? error) = await ReadStreamAsync(
            client.GetStreamingResponseAsync(UserMessages()));

        // Assert
        Assert.Single(updates);
        Assert.IsType<ClientResultException>(error);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_OnAttemptTimeoutBeforeFirstUpdate_FallsBack()
    {
        // Arrange
        ScriptedChatClient first = new ScriptedChatClient()
            .StreamWithDelay(TimeSpan.FromSeconds(5), "запізно");
        ScriptedChatClient second = new ScriptedChatClient().Stream("встигла");
        FallbackChatClient client = CreateClient(
            TimeSpan.FromMilliseconds(80),
            TimeSpan.FromSeconds(5),
            first,
            second);

        // Act
        (List<ChatResponseUpdate> updates, Exception? error) = await ReadStreamAsync(
            client.GetStreamingResponseAsync(UserMessages()));

        // Assert
        Assert.Null(error);
        Assert.Equal("встигла", string.Concat(updates.Select(update => update.Text)));
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_TotalTimeoutAfterFirstUpdate_ThrowsTimeout()
    {
        // Arrange: перший фрагмент уже пішов, далі ланцюжок не встигає
        ScriptedChatClient first = new ScriptedChatClient()
            .StreamThenDelayThrow(
                TimeSpan.FromSeconds(5),
                LlmTestExceptions.OpenAiStatus(500),
                "часткова відповідь");
        ScriptedChatClient second = new ScriptedChatClient().Stream("інше");
        FallbackChatClient client = CreateClient(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(150),
            first,
            second);

        // Act
        (List<ChatResponseUpdate> updates, Exception? error) = await ReadStreamAsync(
            client.GetStreamingResponseAsync(UserMessages()));

        // Assert
        Assert.Single(updates);
        LlmUnavailableException exception = Assert.IsType<LlmUnavailableException>(error);
        Assert.Equal(LlmFailureKind.Timeout, exception.Kind);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_EmptyChain_ThrowsNotConfigured()
    {
        // Arrange
        FallbackChatClient client = CreateClient();

        // Act
        (List<ChatResponseUpdate> updates, Exception? error) = await ReadStreamAsync(
            client.GetStreamingResponseAsync(UserMessages()));

        // Assert
        Assert.Empty(updates);
        LlmUnavailableException exception = Assert.IsType<LlmUnavailableException>(error);
        Assert.Equal(LlmFailureKind.NotConfigured, exception.Kind);
    }

    private static FallbackChatClient CreateClient(params IChatClient[] clients)
        => CreateClient(DefaultAttemptTimeout, DefaultTotalTimeout, clients);

    private static FallbackChatClient CreateClient(
        TimeSpan attemptTimeout,
        TimeSpan totalTimeout,
        params IChatClient[] clients)
    {
        LlmChatAttempt[] attempts = clients
            .Select((client, index) => new LlmChatAttempt($"provider-{index}", $"model-{index}", client))
            .ToArray();

        return new FallbackChatClient(
            LlmRole.Chat,
            attempts,
            attemptTimeout,
            totalTimeout,
            NullLogger<FallbackChatClient>.Instance);
    }

    private static ChatMessage[] UserMessages() => [new ChatMessage(ChatRole.User, "привіт")];

    private static async Task<(List<ChatResponseUpdate> Updates, Exception? Error)> ReadStreamAsync(
        IAsyncEnumerable<ChatResponseUpdate> stream)
    {
        List<ChatResponseUpdate> updates = [];
        Exception? error = null;

        // Явний enumerator, щоб відділити вже отримані фрагменти від помилки потоку.
        await using IAsyncEnumerator<ChatResponseUpdate> enumerator = stream.GetAsyncEnumerator();
        try
        {
            while (await enumerator.MoveNextAsync())
            {
                updates.Add(enumerator.Current);
            }
        }
        catch (Exception exception)
        {
            error = exception;
        }

        return (updates, error);
    }
}
