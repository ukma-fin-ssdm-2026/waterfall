// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Waterfall.Services.Extensions;
using Waterfall.Services.Llm;

namespace Waterfall.Services.Tests;

/// <summary>
/// Шлюз через справжні SDK OpenAI та Anthropic проти локального HTTP-сервера:
/// перевіряє, що запити йдуть на BaseUrl із конфігурації з ключем і моделлю ланки,
/// а відмови провайдера реального протоколу правильно вмикають або не вмикають fallback.
/// </summary>
public sealed class LlmGatewayHttpTests : IDisposable
{
    private const string OpenAiAnswer =
        "{\"id\":\"c1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"m\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"від openai\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";

    private const string AnthropicAnswer =
        "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"m\",\"content\":[{\"type\":\"text\",\"text\":\"від anthropic\"}],\"stop_reason\":\"end_turn\",\"stop_sequence\":null,\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";

    private readonly FakeLlmServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task OpenAiCompatibleProvider_SendsToConfiguredBaseUrlWithKeyAndModel()
    {
        // Arrange
        _server.Respond("/v1/chat/completions", HttpStatusCode.OK, OpenAiAnswer);
        using ServiceProvider services = BuildServices(
            ("compat", LlmProviderKinds.OpenAiCompatible, "/v1", "sk-compat"),
            chain: [("compat", "vendor/model-a")]);
        IChatClient client = services.GetRequiredService<ILlmClientProvider>().GetClient(LlmRole.Chat);

        // Act
        ChatResponse response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "привіт")]);

        // Assert
        Assert.Equal("від openai", response.Text);
        RecordedRequest request = Assert.Single(_server.Requests);
        Assert.Equal("/v1/chat/completions", request.Path);
        Assert.Equal("Bearer sk-compat", request.Authorization);
        Assert.Contains("\"model\":\"vendor/model-a\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnthropicProvider_SendsMessagesRequestWithConfiguredKey()
    {
        // Arrange
        _server.Respond("/v1/messages", HttpStatusCode.OK, AnthropicAnswer);
        using ServiceProvider services = BuildServices(
            ("claude", LlmProviderKinds.Anthropic, string.Empty, "sk-ant-test"),
            chain: [("claude", "claude-test")]);
        IChatClient client = services.GetRequiredService<ILlmClientProvider>().GetClient(LlmRole.Chat);

        // Act
        ChatResponse response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "привіт")]);

        // Assert
        Assert.Equal("від anthropic", response.Text);
        RecordedRequest request = Assert.Single(_server.Requests);
        Assert.Equal("/v1/messages", request.Path);
        // SDK передає ключ у x-api-key або як Bearer (залежно від версії й BaseUrl) — важливо, що саме наш
        Assert.True(
            request.ApiKeyHeader == "sk-ant-test" || request.Authorization == "Bearer sk-ant-test",
            "ключ із конфігурації не потрапив у запит");
        Assert.Contains("\"model\":\"claude-test\"", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealRateLimitResponse_FallsBackToNextProviderInChain()
    {
        // Arrange: перший провайдер відповідає 429 справжнім протоколом OpenAI
        _server.Respond("/v1/chat/completions", HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"slow down\"}}");
        _server.Respond("/v1/messages", HttpStatusCode.OK, AnthropicAnswer);
        using ServiceProvider services = BuildServices(
            [("compat", LlmProviderKinds.OpenAiCompatible, "/v1", "sk-compat"), ("claude", LlmProviderKinds.Anthropic, string.Empty, "sk-ant")],
            chain: [("compat", "model-a"), ("claude", "claude-test")]);
        IChatClient client = services.GetRequiredService<ILlmClientProvider>().GetClient(LlmRole.Chat);

        // Act
        ChatResponse response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "привіт")]);

        // Assert: без повторів SDK (MaxRetries = 0) — рівно один запит на кожну ланку
        Assert.Equal("від anthropic", response.Text);
        Assert.Equal(["/v1/chat/completions", "/v1/messages"], _server.Requests.Select(r => r.Path));
    }

    [Fact]
    public async Task RealUnauthorizedResponse_DoesNotFallBack()
    {
        // Arrange: невірний ключ — наша помилка конфігурації, її не можна ховати за іншим провайдером
        _server.Respond("/v1/chat/completions", HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"bad key\"}}");
        _server.Respond("/v1/messages", HttpStatusCode.OK, AnthropicAnswer);
        using ServiceProvider services = BuildServices(
            [("compat", LlmProviderKinds.OpenAiCompatible, "/v1", "sk-wrong"), ("claude", LlmProviderKinds.Anthropic, string.Empty, "sk-ant")],
            chain: [("compat", "model-a"), ("claude", "claude-test")]);
        IChatClient client = services.GetRequiredService<ILlmClientProvider>().GetClient(LlmRole.Chat);

        // Act
        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "привіт")]));

        // Assert
        Assert.IsNotType<LlmUnavailableException>(exception);
        Assert.Equal(["/v1/chat/completions"], _server.Requests.Select(r => r.Path));
    }

    [Fact]
    public async Task ServerErrorOnEveryProvider_ReportsServerErrorAfterTryingEachOnce()
    {
        // Arrange
        _server.Respond("/v1/chat/completions", HttpStatusCode.ServiceUnavailable, "{}");
        _server.Respond("/v1/messages", HttpStatusCode.InternalServerError, "{\"type\":\"error\",\"error\":{\"type\":\"api_error\",\"message\":\"x\"}}");
        using ServiceProvider services = BuildServices(
            [("compat", LlmProviderKinds.OpenAiCompatible, "/v1", "sk-compat"), ("claude", LlmProviderKinds.Anthropic, string.Empty, "sk-ant")],
            chain: [("compat", "model-a"), ("claude", "claude-test")]);
        IChatClient client = services.GetRequiredService<ILlmClientProvider>().GetClient(LlmRole.Chat);

        // Act
        LlmUnavailableException exception = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "привіт")]));

        // Assert
        Assert.Equal(LlmFailureKind.ServerError, exception.Kind);
        Assert.Equal(2, _server.Requests.Count);
    }

    [Fact]
    public void Factory_RejectsUnknownProviderKind()
    {
        // Arrange
        LlmProviderOptions provider = new() { Kind = "Gopher", BaseUrl = "https://example.com" };

        // Act
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => LlmClientFactory.Create(provider, "m", "key"));

        // Assert
        Assert.Contains("Gopher", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddWaterfallLlm_RejectsStructurallyBrokenConfigurationWhenOptionsAreRead()
    {
        // Arrange: ланка посилається на неіснуючого провайдера
        using ServiceProvider services = BuildServicesFromConfig(new Dictionary<string, string?>
        {
            ["Llm:Roles:Chat:Chain:0:Provider"] = "ghost",
            ["Llm:Roles:Chat:Chain:0:Model"] = "m"
        });

        // Act + Assert
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<LlmOptions>>().Value);
    }

    [Fact]
    public async Task StartupDiagnostics_WarnAboutMissingKeysWithoutFailing()
    {
        // Arrange: ключ для провайдера не задано, змінна середовища з унікальною назвою
        string missingEnv = $"WATERFALL_TEST_MISSING_KEY_{Guid.NewGuid():N}";
        CapturingLoggerProvider logs = new();
        using ServiceProvider services = BuildServicesFromConfig(
            new Dictionary<string, string?>
            {
                ["Llm:Providers:compat:Kind"] = LlmProviderKinds.OpenAiCompatible,
                ["Llm:Providers:compat:BaseUrl"] = "https://example.com/v1",
                ["Llm:Providers:compat:ApiKeyEnv"] = missingEnv,
                ["Llm:Roles:Chat:Chain:0:Provider"] = "compat",
                ["Llm:Roles:Chat:Chain:0:Model"] = "m"
            },
            logs);

        // Act
        foreach (IHostedService hosted in services.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        // Assert: попереджено про провайдера без ключа, про роль без придатної ланки і про ролі без ланцюжка
        Assert.Contains(logs.Warnings, message => message.Contains("compat", StringComparison.Ordinal) && message.Contains(missingEnv, StringComparison.Ordinal));
        Assert.Contains(logs.Warnings, message => message.Contains("Chat", StringComparison.Ordinal) && message.Contains("жодної ланки", StringComparison.Ordinal));
        Assert.Contains(logs.Warnings, message => message.Contains("Extract", StringComparison.Ordinal) && message.Contains("не задано ланцюжок", StringComparison.Ordinal));
    }

    [Fact]
    public void UnavailableException_DefaultMessageDescribesEveryFailureKind()
    {
        // Act
        string[] messages = Enum.GetValues<LlmFailureKind>()
            .Select(kind => new LlmUnavailableException(kind).Message)
            .ToArray();

        // Assert: різні причини — різні повідомлення, жодного порожнього
        Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message)));
        Assert.Equal(messages.Length, messages.Distinct(StringComparer.Ordinal).Count());
    }

    private ServiceProvider BuildServices(
        (string Name, string Kind, string Path, string Key) provider,
        (string Provider, string Model)[] chain) => BuildServices([provider], chain);

    private ServiceProvider BuildServices(
        (string Name, string Kind, string Path, string Key)[] providers,
        (string Provider, string Model)[] chain)
    {
        Dictionary<string, string?> config = new()
        {
            ["Llm:AttemptTimeout"] = "00:00:10",
            ["Llm:TotalTimeout"] = "00:00:20"
        };
        foreach ((string name, string kind, string path, string key) in providers)
        {
            config[$"Llm:Providers:{name}:Kind"] = kind;
            config[$"Llm:Providers:{name}:BaseUrl"] = _server.BaseUrl + path;
            config[$"Llm:Providers:{name}:ApiKey"] = key;
        }

        for (int i = 0; i < chain.Length; i++)
        {
            config[$"Llm:Roles:Chat:Chain:{i}:Provider"] = chain[i].Provider;
            config[$"Llm:Roles:Chat:Chain:{i}:Model"] = chain[i].Model;
        }

        return BuildServicesFromConfig(config);
    }

    private static ServiceProvider BuildServicesFromConfig(
        Dictionary<string, string?> config,
        ILoggerProvider? loggerProvider = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        ServiceCollection services = new();
        services.AddLogging(builder =>
        {
            if (loggerProvider is not null)
            {
                builder.AddProvider(loggerProvider);
            }
        });
        services.AddWaterfallLlm(configuration);
        return services.BuildServiceProvider();
    }

    private sealed record RecordedRequest(string Path, string? Authorization, string? ApiKeyHeader, string Body);

    /// <summary>Мінімальний локальний HTTP-сервер із заданими відповідями за шляхом.</summary>
    private sealed class FakeLlmServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentDictionary<string, (HttpStatusCode Status, string Body)> _routes = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<RecordedRequest> _requests = new();
        private readonly CancellationTokenSource _stop = new();

        public FakeLlmServer()
        {
            int port = FreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseUrl + "/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public string BaseUrl { get; }

        public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

        public void Respond(string path, HttpStatusCode status, string body) => _routes[path] = (status, body);

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
            _stop.Dispose();
        }

        private static int FreePort()
        {
            using TcpListener probe = new(IPAddress.Loopback, 0);
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        private async Task LoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (HttpListenerException)
                {
                    return;
                }

                using StreamReader reader = new(context.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync();
                string path = context.Request.Url!.AbsolutePath;
                _requests.Enqueue(new RecordedRequest(path, context.Request.Headers["Authorization"], context.Request.Headers["x-api-key"], body));

                (HttpStatusCode status, string responseBody) = _routes.TryGetValue(path, out var route)
                    ? route
                    : (HttpStatusCode.NotFound, "{}");
                byte[] bytes = Encoding.UTF8.GetBytes(responseBody);
                context.Response.StatusCode = (int)status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
    }

    /// <summary>Збирає тексти попереджень для перевірки діагностики старту.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IReadOnlyList<string> Warnings => [.. _warnings];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_warnings);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    warnings.Enqueue(formatter(state, exception));
                }
            }
        }
    }
}
