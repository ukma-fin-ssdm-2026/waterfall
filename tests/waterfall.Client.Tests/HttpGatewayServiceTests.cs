// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Waterfall.Client;
using Waterfall.Client.Extensions;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Tests;

/// <summary>
/// Тести шлюзу через справжню DI-реєстрацію (AddWaterfallClient); підмінено лише мережевий обробник.
/// </summary>
public class HttpGatewayServiceTests
{
    private readonly StubHandler _server = new();
    private readonly InMemoryTokenStore _tokens;
    private readonly IHttpGatewayService _gateway;

    public HttpGatewayServiceTests()
    {
        ServiceCollection services = new();
        // Без кінцевого "/" навмисно: реєстрація мусить його додати.
        services.AddWaterfallClient<InMemoryTokenStore>(new Uri("http://api.test:3000"));
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = _server));

        ServiceProvider provider = services.BuildServiceProvider();
        _tokens = (InMemoryTokenStore)provider.GetRequiredService<IAuthTokenStore>();
        _gateway = provider.GetRequiredService<IHttpGatewayService>();
    }

    [Fact]
    public async Task GetVacancies_BuildsQueryAndParsesPageWithNumericEnums()
    {
        _server.Respond(HttpStatusCode.OK, """
            {
              "items": [{
                "id": "11111111-1111-1111-1111-111111111111",
                "title": "Senior .NET", "company": "Genesis",
                "source": 3, "skills": ["C#", "Kafka"],
                "isActive": false, "isSaved": true, "salaryMin": 4000
              }],
              "totalCount": 21, "page": 2, "pageSize": 10,
              "totalPages": 3, "hasPreviousPage": true, "hasNextPage": true
            }
            """);

        var page = await _gateway.GetVacanciesAsync("  C# & .NET  ", page: 2, pageSize: 10);

        Assert.Equal("http://api.test:3000/api/v1/vacancies?page=2&pageSize=10&search=C%23%20%26%20.NET",
            _server.Requests.Single().Uri);
        VacancyDto vacancy = Assert.Single(page.Items);
        Assert.Equal(JobSource.Djinni, vacancy.Source);
        Assert.Equal(["C#", "Kafka"], vacancy.Skills);
        Assert.False(vacancy.IsActive);
        Assert.True(vacancy.IsSaved);
        Assert.Equal(4000m, vacancy.SalaryMin);
        Assert.True(page.HasNextPage);
        Assert.Equal(21, page.TotalCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task GetVacancies_BlankSearch_OmitsSearchParameter(string? search)
    {
        _server.Respond(HttpStatusCode.OK, """{ "items": [] }""");

        await _gateway.GetVacanciesAsync(search, page: 1, pageSize: 20);

        Assert.Equal("http://api.test:3000/api/v1/vacancies?page=1&pageSize=20", _server.Requests.Single().Uri);
    }

    [Fact]
    public async Task ErrorEnvelope_BecomesApiExceptionWithServerMessage()
    {
        _server.Respond(HttpStatusCode.Conflict, """
            { "error": { "code": "CONFLICT", "message": "Вакансію вже збережено.", "details": [], "request_id": "req-1" } }
            """);

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() =>
            _gateway.SaveVacancyAsync(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Equal("CONFLICT", ex.Error?.Code);
        Assert.Equal("req-1", ex.Error?.RequestId);
        Assert.Equal("Вакансію вже збережено.", ex.Message);
    }

    [Fact]
    public async Task ValidationError_ExposesFieldDetails()
    {
        _server.Respond(HttpStatusCode.BadRequest, """
            { "error": { "code": "VALIDATION_ERROR", "message": "Невалідні дані.",
              "details": [{ "field": "password", "message": "Мінімум 12 символів", "provided_value": null }] } }
            """);

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() =>
            _gateway.RegisterAsync(new RegisterRequest { Email = "a@b.c", Password = "short" }));

        var detail = Assert.Single(ex.Error!.Details);
        Assert.Equal("password", detail.Field);
        Assert.Null(await _tokens.GetTokenAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "", "Потрібно увійти в обліковий запис.")]
    [InlineData(HttpStatusCode.BadGateway, "<html>proxy</html>", "Сервер повернув помилку 502.")]
    [InlineData(HttpStatusCode.InternalServerError, "null", "Сервер повернув помилку 500.")]
    public async Task ErrorWithoutEnvelope_FallsBackToStatusMessage(HttpStatusCode status, string body, string expected)
    {
        _server.Respond(status, body, body.StartsWith('<') ? "text/html" : "application/json");

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => _gateway.GetSavedVacanciesAsync());

        Assert.Equal(status, ex.StatusCode);
        Assert.Null(ex.Error);
        Assert.Equal(expected, ex.Message);
    }

    [Fact]
    public async Task ServerUnreachable_BecomesApiExceptionWithoutStatus()
    {
        _server.Throw(new HttpRequestException("connection refused"));

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => _gateway.GetVacanciesAsync(null, 1, 10));

        Assert.Null(ex.StatusCode);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task CallerCancellation_IsNotWrapped()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _gateway.GetVacanciesAsync(null, 1, 10, cts.Token));
    }

    /// <summary>
    /// Сторінка помилки проксі з кодуванням, якого .NET не знає без CodePagesEncodingProvider:
    /// ReadFromJsonAsync кидає InvalidOperationException, і вона не повинна вилетіти повз ApiException
    /// (viewmodels ловлять лише ApiException, решта роняє команду).
    /// </summary>
    [Theory]
    [InlineData("text/html; charset=windows-1251")]
    [InlineData("application/json; charset=bogus")]
    public async Task ErrorBodyWithUnsupportedCharset_FallsBackToStatusMessage(string contentType)
    {
        _server.RespondRaw(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>", contentType);

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => _gateway.GetVacanciesAsync(null, 1, 10));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal("Сервер повернув помилку 502.", ex.Message);
    }

    [Fact]
    public async Task HttpClientTimeout_BecomesApiException_UnlikeCallerCancellation()
    {
        // HttpClient.Timeout кидає TaskCanceledException, хоча токен виклику не скасовано.
        _server.Throw(new TaskCanceledException("timeout"));

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => _gateway.GetVacanciesAsync(null, 1, 10));

        Assert.Null(ex.StatusCode);
        Assert.Equal("Сервер не відповів вчасно.", ex.Message);
    }

    [Fact]
    public async Task Login_StoresToken_SubsequentRequestsSendBearer_LogoutStops()
    {
        Assert.Null(await LastAuthHeaderAfterCatalogCall());

        _server.Respond(HttpStatusCode.OK, """
            { "token": "jwt-abc", "expiresAt": "2026-10-11T12:15:00Z",
              "userId": "22222222-2222-2222-2222-222222222222", "email": "kosta@ukma.edu.ua", "role": 1 }
            """);
        AuthResponse auth = await _gateway.LoginAsync(new LoginRequest { Email = "kosta@ukma.edu.ua", Password = "pw" });

        Assert.Equal(UserRole.Analyst, auth.Role);
        Assert.Equal("Bearer jwt-abc", await LastAuthHeaderAfterCatalogCall());

        await _gateway.LogoutAsync();
        Assert.Null(await LastAuthHeaderAfterCatalogCall());
    }

    [Fact]
    public async Task Register_SendsRoleAsName()
    {
        // Сервер (JsonStringEnumConverter) приймає й віддає перелічення назвою: "Analyst", "Djinni".
        _server.Respond(HttpStatusCode.Created, """{ "token": "t", "role": "Analyst" }""");

        await _gateway.RegisterAsync(new RegisterRequest { Email = "a@b.c", Password = "longpassword1", Role = UserRole.Analyst });

        StubHandler.Recorded request = _server.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://api.test:3000/api/v1/auth/register", request.Uri);
        Assert.Contains("\"role\":\"Analyst\"", request.Body);
        Assert.Contains("\"email\":\"a@b.c\"", request.Body);
    }

    [Fact]
    public async Task GetVacancies_ParsesSourceAsName()
    {
        _server.Respond(HttpStatusCode.OK, """
            { "items": [{ "id": "11111111-1111-1111-1111-111111111111",
              "title": "Senior .NET", "company": "Genesis", "source": "Djinni" }],
              "totalCount": 1, "page": 1, "pageSize": 10,
              "totalPages": 1, "hasPreviousPage": false, "hasNextPage": false }
            """);

        var page = await _gateway.GetVacanciesAsync(null, 1, 10);

        Assert.Equal(JobSource.Djinni, Assert.Single(page.Items).Source);
    }

    [Fact]
    public async Task GetVacancy_ReturnsItem()
    {
        Guid id = Guid.Parse("44444444-4444-4444-4444-444444444444");
        _server.Respond(HttpStatusCode.OK, """
            { "id": "44444444-4444-4444-4444-444444444444",
              "title": "Senior .NET", "company": "Genesis", "source": "Djinni" }
            """);

        VacancyDto vacancy = await _gateway.GetVacancyAsync(id);

        Assert.Equal($"http://api.test:3000/api/v1/vacancies/{id}", _server.Requests.Single().Uri);
        Assert.Equal((id, "Senior .NET", JobSource.Djinni), (vacancy.Id, vacancy.Title, vacancy.Source));
    }

    [Fact]
    public async Task GetVacancy_NullBody_ThrowsEmptyResponse()
    {
        _server.Respond(HttpStatusCode.OK, "null");

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => _gateway.GetVacancyAsync(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Null(ex.Error);
        Assert.Equal("Сервер повернув порожню відповідь.", ex.Message);
    }

    [Fact]
    public async Task GetVacancy_MalformedJson_ThrowsInvalidResponse()
    {
        _server.Respond(HttpStatusCode.OK, "{oops");

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => _gateway.GetVacancyAsync(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal("Сервер повернув некоректну відповідь.", ex.Message);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    /// <summary>
    /// Captive portal (Wi-Fi з авторизацією) відповідає 200 зі своєю HTML-сторінкою в будь-якому кодуванні.
    /// </summary>
    [Fact]
    public async Task SuccessBodyWithUnsupportedCharset_ThrowsInvalidResponse()
    {
        _server.RespondRaw(HttpStatusCode.OK, "<html>Увійдіть у Wi-Fi</html>", "text/html; charset=windows-1251");

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => _gateway.GetVacancyAsync(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal("Сервер повернув некоректну відповідь.", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Недостатньо прав для цієї дії.")]
    [InlineData(HttpStatusCode.NotFound, "Ресурс не знайдено.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Сервер повернув помилку 503.")]
    public async Task ErrorStatus_MapsToDefaultMessage(HttpStatusCode status, string expected)
    {
        _server.Respond(status, "");

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => _gateway.GetSavedVacanciesAsync());

        Assert.Equal(status, ex.StatusCode);
        Assert.Null(ex.Error);
        Assert.Equal(expected, ex.Message);
    }

    /// <summary>
    /// API за зворотним проксі з префіксом шляху: без доданого "/" HttpClient відкинув би "waterfall"
    /// з BaseAddress і пішов би на http://api.test:3000/api/v1/...; зайвого "//" теж бути не повинно.
    /// </summary>
    [Theory]
    [InlineData("http://api.test:3000/waterfall")]
    [InlineData("http://api.test:3000/waterfall/")]
    public async Task BaseAddress_WithPathPrefix_KeepsPrefixWithSingleSlash(string baseAddress)
    {
        var server = new StubHandler();
        ServiceCollection services = new();
        services.AddWaterfallClient<InMemoryTokenStore>(new Uri(baseAddress));
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = server));
        IHttpGatewayService gateway = services.BuildServiceProvider().GetRequiredService<IHttpGatewayService>();
        server.Respond(HttpStatusCode.OK, """{ "items": [] }""");

        await gateway.GetVacanciesAsync(null, page: 1, pageSize: 20);

        Assert.Equal("http://api.test:3000/waterfall/api/v1/vacancies?page=1&pageSize=20", server.Requests.Single().Uri);
    }

    [Fact]
    public async Task Unsave_DeletesByVacancyId()
    {
        Guid vacancyId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        _server.Respond(HttpStatusCode.NoContent, "");

        await _gateway.UnsaveVacancyAsync(vacancyId);

        StubHandler.Recorded request = _server.Requests.Single();
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"http://api.test:3000/api/v1/saved-vacancies/{vacancyId}", request.Uri);
    }

    private async Task<string?> LastAuthHeaderAfterCatalogCall()
    {
        _server.Respond(HttpStatusCode.OK, """{ "items": [] }""");
        await _gateway.GetVacanciesAsync(null, 1, 10);
        return _server.Requests[^1].Authorization;
    }

    [Fact]
    public async Task DirtyToken_StillSendsCleanBearer()
    {
        // Токен із зайвими пробілами/переносом (SecureStorage, копіпаст): заголовок мусить бути чистим,
        // інакше Kestrel відхиляє запит порожнім 400 (немає тіла — лише статус).
        _tokens.Token = "jwt-abc \n";
        Assert.Equal("Bearer jwt-abc", await LastAuthHeaderAfterCatalogCall());
    }

    /// <summary>
    /// Мережевий обробник-заглушка: записує запити й повертає заздалегідь задану відповідь.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private Func<HttpResponseMessage> _next = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public record Recorded(HttpMethod Method, string Uri, string? Authorization, string? Body);

        public List<Recorded> Requests { get; } = [];

        public void Respond(HttpStatusCode status, string body, string mediaType = "application/json") =>
            _next = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

        /// <summary>Відповідь із довільним Content-Type (StringContent завжди дописує charset=utf-8).</summary>
        public void RespondRaw(HttpStatusCode status, string body, string contentType) =>
            _next = () =>
            {
                ByteArrayContent content = new(Encoding.UTF8.GetBytes(body));
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                return new HttpResponseMessage(status) { Content = content };
            };

        public void Throw(Exception ex) => _next = () => throw ex;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded(request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
            return _next();
        }
    }
}
