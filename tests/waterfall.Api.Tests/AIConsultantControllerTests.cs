// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Waterfall.Api.Controllers;
using Waterfall.Api.Extensions;
using Waterfall.Repository.Entities;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Llm;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Tests;

/// <summary>
/// Інтеграційні тести ШІ-консультанта (FR-002, REL-003, THR-10, THR-12):
/// справжній контролер, справжня політика rate limiting і справжній SSE-потік через TestServer,
/// підмінено лише сервіс ШІ (LLM не викликається).
/// </summary>
public sealed class AIConsultantControllerTests
{
    private static readonly Guid SeekerId = Guid.Parse("66666666-6666-6666-6666-66666666666a");

    private const string MessagesPath = "/api/v1/ai-consultant/messages";

    private const string AnalyzePath = "/api/v1/ai-consultant/analyze";

    private static VacancyDto CreateVacancy(string title = "Python Developer")
    {
        return new VacancyDto
        {
            Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            Title = title,
            Company = "Waterfall Labs",
            Description = "Опис",
            Category = "Software Development",
            CategoryGroup = "Software Development",
            Location = "Київ (Віддалено)",
            Experience = "1 рік",
            ExperienceMonths = 12,
            WorkFormat = WorkFormat.Remote,
            SalaryMin = 1000,
            SalaryMax = 1500,
            Currency = "USD",
            Source = JobSource.Djinni,
            SourceUrl = "https://example.com/job",
            Skills = ["Python"],
            PublishedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    private static AIConsultantTurn CreateTurn(
        Guid? sessionId = null,
        bool exact = true,
        IReadOnlyList<VacancyDto>? vacancies = null)
    {
        return new AIConsultantTurn
        {
            SessionId = sessionId ?? Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Filter = new AppliedFilter { Search = "python", WorkFormat = ["remote"] },
            Vacancies = vacancies ?? [CreateVacancy()],
            Exact = exact,
            Relaxed = exact ? Array.Empty<string>() : ["min_salary"],
            Disclaimer = exact ? null : "Точних збігів не знайдено. Наведені вакансії відповідають запиту не на 100%."
        };
    }

    private static IReadOnlyList<AIConsultantStreamItem> SuccessfulStream(long messageId = 42)
    {
        return
        [
            new AIConsultantDelta("Знайшов "),
            new AIConsultantDelta("вакансію."),
            new AIConsultantCompleted(messageId)
        ];
    }

    private static HttpRequestMessage SendMessageRequest(Guid userId, string role, object body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, MessagesPath)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(TestAuthenticationHandler.UserHeader, userId.ToString());
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, role);
        return request;
    }

    private static AIAnalysisTurn CreateAnalysisTurn(IReadOnlyList<VacancyDto>? vacancies = null)
    {
        List<VacancyDto> items = vacancies is null ? [CreateVacancy()] : vacancies.ToList();
        return new AIAnalysisTurn
        {
            Vacancies = items,
            Relevance = items
                .Select((vacancy, index) => new VacancyRelevance(vacancy.Id, 72 - index, ["python", "віддалено"], 2))
                .ToList()
        };
    }

    private static HttpRequestMessage AnalyzeRequest(Guid userId, string role, object body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, AnalyzePath)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(TestAuthenticationHandler.UserHeader, userId.ToString());
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, role);
        return request;
    }

    private static HttpRequestMessage DeleteSessionRequest(Guid userId, string role, Guid sessionId)
    {
        HttpRequestMessage request = new(HttpMethod.Delete, $"/api/v1/ai-consultant/sessions/{sessionId}");
        request.Headers.Add(TestAuthenticationHandler.UserHeader, userId.ToString());
        request.Headers.Add(TestAuthenticationHandler.RoleHeader, role);
        return request;
    }

    /// <summary>
    /// Прочитати SSE-потік у список подій (ім'я + сирий JSON), як це зробить клієнт MAUI.
    /// </summary>
    private static async Task<List<(string Event, string Data)>> ReadEventsAsync(HttpResponseMessage response)
    {
        using StreamReader reader = new(await response.Content.ReadAsStreamAsync());
        List<(string Event, string Data)> events = [];
        string? currentEvent = null;
        StringBuilder data = new();

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (line.Length == 0)
            {
                if (currentEvent is not null)
                {
                    events.Add((currentEvent, data.ToString()));
                    currentEvent = null;
                    data.Clear();
                }

                continue;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEvent = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data.Append(line["data: ".Length..]);
            }
        }

        if (currentEvent is not null)
        {
            events.Add((currentEvent, data.ToString()));
        }

        return events;
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task SendMessage_Analyst_IsForbidden()
    {
        // Arrange
        StubConsultantService service = new();
        await using ApiHost host = await ApiHost.StartAsync(service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "Analyst", new { text = "шукаю python" }));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(service.PrepareCalls);
    }

    [Fact]
    public async Task SendMessage_Anonymous_IsUnauthorized()
    {
        // Arrange
        StubConsultantService service = new();
        await using ApiHost host = await ApiHost.StartAsync(service);
        HttpRequestMessage request = new(HttpMethod.Post, MessagesPath)
        {
            Content = JsonContent.Create(new { text = "шукаю python" })
        };

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(service.PrepareCalls);
    }

    [Fact]
    public async Task SendMessage_ForeignSession_Returns404()
    {
        // Arrange
        Guid foreignSession = Guid.NewGuid();
        StubConsultantService service = new()
        {
            PrepareHandler = (_, _, _) => throw new ChatSessionNotFoundException(foreignSession)
        };
        await using ApiHost host = await ApiHost.StartAsync(service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { session_id = foreignSession, text = "привіт" }));
        JsonElement error = Parse(await response.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", error.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task SendMessage_LlmUnavailable_Returns503BeforeStreaming()
    {
        // Arrange
        StubConsultantService service = new()
        {
            PrepareHandler = (_, _, _) => throw new LlmUnavailableException(LlmFailureKind.NotConfigured)
        };
        await using ApiHost host = await ApiHost.StartAsync(service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "шукаю python" }));
        JsonElement error = Parse(await response.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("LLM_UNAVAILABLE", error.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(
            "звичайним пошуком",
            error.GetProperty("error").GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessage_EmptyOrTooLongText_Returns400()
    {
        // Arrange
        StubConsultantService service = new();
        await using ApiHost host = await ApiHost.StartAsync(service);

        // Act
        HttpResponseMessage empty = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "" }));
        HttpResponseMessage tooLong = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = new string('a', 1001) }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Empty(service.PrepareCalls);
    }

    [Fact]
    public async Task SendMessage_OwnSession_StreamsMetaVacanciesDeltasDoneInOrder()
    {
        // Arrange
        StubConsultantService service = new()
        {
            Turn = CreateTurn(),
            StreamItems = SuccessfulStream()
        };
        await using ApiHost host = await ApiHost.StartAsync(service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "шукаю python" }));
        List<(string Event, string Data)> events = await ReadEventsAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { "meta", "vacancies", "delta", "delta", "done" }, events.Select(item => item.Event));

        JsonElement meta = Parse(events[0].Data);
        Assert.Equal(service.Turn.SessionId, meta.GetProperty("session_id").GetGuid());
        Assert.Equal("python", meta.GetProperty("applied_filter").GetProperty("search").GetString());
        Assert.Equal("remote", meta.GetProperty("applied_filter").GetProperty("work_format")[0].GetString());

        JsonElement vacancies = Parse(events[1].Data);
        Assert.True(vacancies.GetProperty("exact").GetBoolean());
        Assert.Equal(0, vacancies.GetProperty("relaxed").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, vacancies.GetProperty("disclaimer").ValueKind);
        JsonElement item = vacancies.GetProperty("items")[0];
        Assert.Equal(service.Turn.Vacancies[0].Id, item.GetProperty("id").GetGuid());
        Assert.Equal("Python Developer", item.GetProperty("title").GetString());
        Assert.Equal("Djinni", item.GetProperty("source").GetString());
        Assert.Equal(1000m, item.GetProperty("salaryMin").GetDecimal());

        Assert.Equal("Знайшов ", Parse(events[2].Data).GetProperty("text").GetString());
        Assert.Equal("вакансію.", Parse(events[3].Data).GetProperty("text").GetString());
        Assert.Equal(42, Parse(events[4].Data).GetProperty("message_id").GetInt64());
    }

    [Fact]
    public async Task SendMessage_RelaxedResults_DescribeRelaxedCriteriaAndDisclaimer()
    {
        // Arrange
        StubConsultantService service = new()
        {
            Turn = CreateTurn(exact: false),
            StreamItems = SuccessfulStream()
        };
        await using ApiHost host = await ApiHost.StartAsync(service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "дуже вузький запит" }));
        List<(string Event, string Data)> events = await ReadEventsAsync(response);

        // Assert
        JsonElement vacancies = Parse(events.Single(item => item.Event == "vacancies").Data);
        Assert.False(vacancies.GetProperty("exact").GetBoolean());
        Assert.Equal("min_salary", vacancies.GetProperty("relaxed")[0].GetString());
        Assert.Contains(
            "не на 100%",
            vacancies.GetProperty("disclaimer").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessage_SummaryFailsAfterVacancies_SendsTerminalErrorEvent()
    {
        // Arrange
        StubConsultantService service = new()
        {
            Turn = CreateTurn(),
            StreamItems = [new AIConsultantDelta("Частина ")],
            StreamException = new LlmUnavailableException(LlmFailureKind.Timeout)
        };
        await using ApiHost host = await ApiHost.StartAsync(service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "шукаю python" }));
        List<(string Event, string Data)> events = await ReadEventsAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "meta", "vacancies", "delta", "error" }, events.Select(item => item.Event));
        JsonElement error = Parse(events[^1].Data);
        Assert.Equal("LLM_UNAVAILABLE", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task SendMessage_AfterLimit_Returns429RateLimited()
    {
        // Arrange
        StubConsultantService service = new() { StreamItems = SuccessfulStream() };
        await using ApiHost host = await ApiHost.StartAsync(service, messagesPerHour: 2);

        // Act
        HttpResponseMessage first = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "перший" }));
        HttpResponseMessage second = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "другий" }));
        HttpResponseMessage third = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "третій" }));
        JsonElement error = Parse(await third.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("RATE_LIMITED", error.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(2, service.PrepareCalls.Count);
    }

    [Fact]
    public async Task SendMessage_RateLimit_IsPerUser()
    {
        // Arrange
        Guid otherSeeker = Guid.NewGuid();
        StubConsultantService service = new() { StreamItems = SuccessfulStream() };
        await using ApiHost host = await ApiHost.StartAsync(service, messagesPerHour: 1);

        // Act
        HttpResponseMessage first = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "перший" }));
        HttpResponseMessage sameUser = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "другий" }));
        HttpResponseMessage otherUser = await host.Client.SendAsync(
            SendMessageRequest(otherSeeker, "JobSeeker", new { text = "третій" }));

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, sameUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);
    }

    [Fact]
    public async Task Analyze_Analyst_IsForbidden()
    {
        // Arrange
        StubAnalysisService service = new();
        await using ApiHost host = await ApiHost.StartAsync(new StubConsultantService(), analysisService: service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            AnalyzeRequest(SeekerId, "Analyst", new { vacancy_ids = new[] { Guid.NewGuid() } }));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(service.PrepareCalls);
    }

    [Fact]
    public async Task Analyze_ZeroOrFourIds_Returns400()
    {
        // Arrange
        StubAnalysisService service = new();
        await using ApiHost host = await ApiHost.StartAsync(new StubConsultantService(), analysisService: service);

        // Act
        HttpResponseMessage empty = await host.Client.SendAsync(
            AnalyzeRequest(SeekerId, "JobSeeker", new { vacancy_ids = Array.Empty<Guid>() }));
        HttpResponseMessage tooMany = await host.Client.SendAsync(
            AnalyzeRequest(
                SeekerId,
                "JobSeeker",
                new { vacancy_ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() } }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Empty(service.PrepareCalls);
    }

    [Fact]
    public async Task Analyze_UnknownVacancy_Returns404()
    {
        // Arrange
        Guid unknown = Guid.NewGuid();
        StubAnalysisService service = new()
        {
            PrepareHandler = (_, _) => throw new VacancyNotFoundException(unknown)
        };
        await using ApiHost host = await ApiHost.StartAsync(new StubConsultantService(), analysisService: service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            AnalyzeRequest(SeekerId, "JobSeeker", new { vacancy_ids = new[] { unknown } }));
        JsonElement error = Parse(await response.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("NOT_FOUND", error.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Analyze_StreamsRelevanceThenDeltasThenDone()
    {
        // Arrange
        StubAnalysisService service = new()
        {
            Turn = CreateAnalysisTurn(),
            StreamItems =
            [
                new AIAnalysisDelta("Вимоги: "),
                new AIAnalysisDelta("Python."),
                new AIAnalysisCompleted()
            ]
        };
        await using ApiHost host = await ApiHost.StartAsync(new StubConsultantService(), analysisService: service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            AnalyzeRequest(SeekerId, "JobSeeker", new { vacancy_ids = new[] { service.Turn.Vacancies[0].Id } }));
        List<(string Event, string Data)> events = await ReadEventsAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { "relevance", "delta", "delta", "done" }, events.Select(item => item.Event));

        JsonElement relevance = Parse(events[0].Data);
        JsonElement item = relevance.GetProperty("items")[0];
        Assert.Equal(service.Turn.Vacancies[0].Id, item.GetProperty("vacancy_id").GetGuid());
        Assert.Equal(72, item.GetProperty("score").GetInt32());
        Assert.Equal(2, item.GetProperty("basis_count").GetInt32());
        Assert.Equal("python", item.GetProperty("matched")[0].GetString());

        Assert.Equal("Вимоги: ", Parse(events[1].Data).GetProperty("text").GetString());
        Assert.Equal("Python.", Parse(events[2].Data).GetProperty("text").GetString());
        Assert.Equal("{}", events[3].Data);
        Assert.Equal(SeekerId, Assert.Single(service.PrepareCalls).UserId);
    }

    [Fact]
    public async Task Analyze_LlmUnavailableAfterRelevance_SendsTerminalErrorEvent()
    {
        // Arrange: провайдер відмовив до першого delta, але relevance вже порахована без LLM
        StubAnalysisService service = new()
        {
            Turn = CreateAnalysisTurn(),
            StreamItems = Array.Empty<AIAnalysisStreamItem>(),
            StreamException = new LlmUnavailableException(LlmFailureKind.NotConfigured)
        };
        await using ApiHost host = await ApiHost.StartAsync(new StubConsultantService(), analysisService: service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            AnalyzeRequest(SeekerId, "JobSeeker", new { vacancy_ids = new[] { service.Turn.Vacancies[0].Id } }));
        List<(string Event, string Data)> events = await ReadEventsAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "relevance", "error" }, events.Select(item => item.Event));
        JsonElement error = Parse(events[^1].Data);
        Assert.Equal("LLM_UNAVAILABLE", error.GetProperty("code").GetString());
        Assert.Contains(
            "релевантності",
            error.GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_SharesRateLimitWithMessages()
    {
        // Arrange: одна політика на користувача для /messages і /analyze (THR-10)
        StubConsultantService consultant = new() { StreamItems = SuccessfulStream() };
        StubAnalysisService analysis = new() { Turn = CreateAnalysisTurn(), StreamItems = [new AIAnalysisCompleted()] };
        await using ApiHost host = await ApiHost.StartAsync(consultant, messagesPerHour: 1, analysisService: analysis);

        // Act
        HttpResponseMessage message = await host.Client.SendAsync(
            SendMessageRequest(SeekerId, "JobSeeker", new { text = "шукаю python" }));
        HttpResponseMessage analyze = await host.Client.SendAsync(
            AnalyzeRequest(SeekerId, "JobSeeker", new { vacancy_ids = new[] { Guid.NewGuid() } }));
        JsonElement error = Parse(await analyze.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.OK, message.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, analyze.StatusCode);
        Assert.Equal("RATE_LIMITED", error.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(analysis.PrepareCalls);
    }

    [Fact]
    public async Task DeleteSession_OwnSession_Returns204WithCurrentUser()
    {
        // Arrange
        Guid sessionId = Guid.NewGuid();
        StubConsultantService service = new();
        await using ApiHost host = await ApiHost.StartAsync(service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            DeleteSessionRequest(SeekerId, "JobSeeker", sessionId));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        (Guid deletedSession, Guid deletedBy) = Assert.Single(service.DeletedSessions);
        Assert.Equal(sessionId, deletedSession);
        Assert.Equal(SeekerId, deletedBy);
    }

    [Fact]
    public async Task DeleteSession_ForeignOrMissing_Returns404()
    {
        // Arrange
        StubConsultantService service = new() { DeleteSucceeds = false };
        await using ApiHost host = await ApiHost.StartAsync(service);

        // Act
        HttpResponseMessage response = await host.Client.SendAsync(
            DeleteSessionRequest(SeekerId, "JobSeeker", Guid.NewGuid()));
        JsonElement error = Parse(await response.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", error.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// Мінімальний хост із справжнім контролером, справжньою політикою rate limiting і підміненою
    /// автентифікацією (заголовки <c>X-Test-User</c> / <c>X-Test-Role</c>), щоб LLM не викликалася.
    /// </summary>
    private sealed class ApiHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ApiHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<ApiHost> StartAsync(
            StubConsultantService service,
            int messagesPerHour = 20,
            StubAnalysisService? analysisService = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ApplicationName = typeof(AIConsultantControllerTests).Assembly.GetName().Name
            });

            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiConsultant:MessagesPerHour"] = messagesPerHour.ToString(),
                ["AiConsultant:HistoryMessages"] = "10",
                ["AiConsultant:MaxVacancies"] = "5"
            });

            // Лише збірка API як джерело контролерів: тестова збірка містить власні тестові контролери
            // без маршрутів, і стандартний пошук application parts зламав би старт хоста
            builder.Services.AddControllers()
                .ConfigureApplicationPartManager(parts =>
                {
                    parts.ApplicationParts.Clear();
                    parts.ApplicationParts.Add(new AssemblyPart(typeof(AIConsultantController).Assembly));
                });
            builder.Services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton<IAIConsultantService>(service);
            builder.Services.AddSingleton<IVacancyAnalysisService>(analysisService ?? new StubAnalysisService());
            builder.Services.AddWaterfallAiConsultantRateLimiting(builder.Configuration);

            WebApplication app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapControllers();

            await app.StartAsync();
            // TestServer може бути зареєстрований у DI як TestServer або як IServer (залежно від версії)
            TestServer server = app.Services.GetService<TestServer>()
                ?? (TestServer)app.Services.GetRequiredService<IServer>();
            return new ApiHost(app, server.CreateClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Тестова схема автентифікації: читає користувача й роль із заголовків, щоб перевірити
    /// і рольову авторизацію (403 для аналітика), і ключ розділу rate limiting (id із токена).
    /// </summary>
    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TestAuth";
        public const string UserHeader = "X-Test-User";
        public const string RoleHeader = "X-Test-Role";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out Microsoft.Extensions.Primitives.StringValues userId)
                || string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            string role = Request.Headers[RoleHeader].ToString();
            Claim[] claims =
            [
                new(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new(ClaimTypes.Role, string.IsNullOrWhiteSpace(role) ? "JobSeeker" : role)
            ];

            ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    /// <summary>
    /// Підміна сервісу ШІ-консультанта: контролер і SSE пишуться справжні, LLM не викликається.
    /// </summary>
    private sealed class StubConsultantService : IAIConsultantService
    {
        public AIConsultantTurn Turn { get; set; } = CreateTurn();

        public IReadOnlyList<AIConsultantStreamItem> StreamItems { get; set; } = Array.Empty<AIConsultantStreamItem>();

        public Exception? StreamException { get; set; }

        public bool DeleteSucceeds { get; set; } = true;

        public Func<Guid, Guid?, string, Task<AIConsultantTurn>>? PrepareHandler { get; set; }

        public List<(Guid SessionId, Guid UserId, string Text)> PrepareCalls { get; } = [];

        public List<(Guid SessionId, Guid UserId)> DeletedSessions { get; } = [];

        public Task<AIConsultantTurn> PrepareAsync(
            Guid userId,
            Guid? sessionId,
            string text,
            CancellationToken ct = default)
        {
            PrepareCalls.Add((sessionId ?? Guid.Empty, userId, text));
            return PrepareHandler is not null
                ? PrepareHandler(userId, sessionId, text)
                : Task.FromResult(Turn);
        }

        public async IAsyncEnumerable<AIConsultantStreamItem> StreamAnswerAsync(
            AIConsultantTurn turn,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (AIConsultantStreamItem item in StreamItems)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }

            if (StreamException is not null)
            {
                throw StreamException;
            }
        }

        public Task<bool> DeleteSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
        {
            DeletedSessions.Add((sessionId, userId));
            return Task.FromResult(DeleteSucceeds);
        }
    }

    /// <summary>
    /// Підміна сервісу аналізу вакансій: контролер, SSE-події та rate limiting справжні, LLM не викликається.
    /// </summary>
    private sealed class StubAnalysisService : IVacancyAnalysisService
    {
        public AIAnalysisTurn Turn { get; set; } = CreateAnalysisTurn();

        public IReadOnlyList<AIAnalysisStreamItem> StreamItems { get; set; } = Array.Empty<AIAnalysisStreamItem>();

        public Exception? StreamException { get; set; }

        public Func<Guid, IReadOnlyList<Guid>, Task<AIAnalysisTurn>>? PrepareHandler { get; set; }

        public List<(Guid UserId, IReadOnlyList<Guid> VacancyIds)> PrepareCalls { get; } = [];

        public Task<AIAnalysisTurn> PrepareAsync(
            Guid userId,
            IReadOnlyList<Guid> vacancyIds,
            CancellationToken ct = default)
        {
            PrepareCalls.Add((userId, vacancyIds));
            return PrepareHandler is not null
                ? PrepareHandler(userId, vacancyIds)
                : Task.FromResult(Turn);
        }

        public async IAsyncEnumerable<AIAnalysisStreamItem> StreamAnalysisAsync(
            AIAnalysisTurn turn,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (AIAnalysisStreamItem item in StreamItems)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }

            if (StreamException is not null)
            {
                throw StreamException;
            }
        }
    }
}
