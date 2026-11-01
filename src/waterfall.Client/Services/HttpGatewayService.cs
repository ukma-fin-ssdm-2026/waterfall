// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: обробка невідомого кодування відповіді (InvalidOperationException) замість недосяжного NotSupportedException (Lab 06); перевірено: регресійні тести HttpGatewayServiceTests, що падали до виправлення.
// AI: SSE-потік ШІ-консультанта (SseParser, типізовані події, коди помилок до початку потоку); перевірено: AiConsultantGatewayTests, dotnet test.

using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Ai;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.Geo;
using Waterfall.Client.Models.Profile;
using Waterfall.Client.Models.SavedVacancies;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Services;

/// <summary>
/// Реалізація <see cref="IHttpGatewayService"/> поверх типізованого HttpClient
/// (BaseAddress задає реєстрація в DI, див. AddWaterfallClient).
/// </summary>
public sealed class HttpGatewayService : IHttpGatewayService
{
    // Серіалізація лише через згенеровані JsonTypeInfo (ClientJsonContext): без рефлексії, безпечно для trimming/AOT.
    private static readonly ClientJsonContext Json = ClientJsonContext.Default;

    private readonly HttpClient _http;
    private readonly IAuthTokenStore _tokens;

    public HttpGatewayService(HttpClient http, IAuthTokenStore tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public Task<PagedResult<VacancyDto>> GetVacanciesAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        string url = $"api/v1/vacancies?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            url += "&search=" + Uri.EscapeDataString(search.Trim());
        }

        return SendAsync(new HttpRequestMessage(HttpMethod.Get, url), Json.PagedResultVacancyDto, ct);
    }

    public Task<VacancyDto> GetVacancyAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"api/v1/vacancies/{id}"), Json.VacancyDto, ct);

    public async Task<IReadOnlyList<SavedVacancyDto>> GetSavedVacanciesAsync(CancellationToken ct = default) =>
        await SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/v1/saved-vacancies"), Json.ListSavedVacancyDto, ct);

    public Task<SavedVacancyDto> SaveVacancyAsync(Guid vacancyId, string? notes = null, CancellationToken ct = default) =>
        SendAsync(
            Post("api/v1/saved-vacancies", new SaveVacancyRequest { VacancyId = vacancyId, Notes = notes }, Json.SaveVacancyRequest),
            Json.SavedVacancyDto,
            ct);

    public async Task UnsaveVacancyAsync(Guid vacancyId, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await SendCoreAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/v1/saved-vacancies/{vacancyId}"), ct);
    }

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        AuthenticateAsync(Post("api/v1/auth/login", request, Json.LoginRequest), ct);

    public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default) =>
        AuthenticateAsync(Post("api/v1/auth/register", request, Json.RegisterRequest), ct);

    public Task LogoutAsync(CancellationToken ct = default) => _tokens.ClearAsync(ct);

    public async Task<string?> GetProfileAddressAsync(CancellationToken ct = default)
    {
        try
        {
            UserAddressDto dto = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/v1/profile/address"), Json.UserAddressDto, ct);
            return dto.Address;
        }
        catch (ApiException)
        {
            return null;
        }
    }

    public async Task SaveProfileAddressAsync(string address, CancellationToken ct = default)
    {
        HttpRequestMessage request = new(HttpMethod.Put, "api/v1/profile/address")
        {
            Content = JsonContent.Create(new UserAddressDto { Address = address }, Json.UserAddressDto)
        };
        await SendAsync(request, Json.UserAddressDto, ct);
    }

    public async Task<CoordinatesDto?> GetCoordinatesAsync(string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        try
        {
            string url = "api/v1/geo/coordinates?address=" + Uri.EscapeDataString(address.Trim());
            return await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), Json.CoordinatesDto, ct);
        }
        catch (ApiException)
        {
            return null;
        }
    }

    public async Task<GeoSyncResponseDto?> SyncGeoCacheAsync(GeoSyncRequestDto request, CancellationToken ct = default)
    {
        try
        {
            HttpRequestMessage msg = Post("api/v1/geo/sync", request, Json.GeoSyncRequestDto);
            return await SendAsync(msg, Json.GeoSyncResponseDto, ct);
        }
        catch (ApiException)
        {
            return null;
        }
    }

    public async Task<UserStatsDto?> GetPersonalStatsAsync(CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/v1/profile/stats"), Json.UserStatsDto, ct);
        }
        catch (ApiException)
        {
            return null;
        }
    }

    public async Task<bool> RecordSearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        try
        {
            using HttpResponseMessage response = await SendCoreAsync(
                Post("api/v1/profile/searches", new RecordSearchRequest { Query = query.Trim() }, Json.RecordSearchRequest),
                ct);
            return response.IsSuccessStatusCode;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    public async Task<bool> ClearSearchHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            using HttpResponseMessage response = await SendCoreAsync(new HttpRequestMessage(HttpMethod.Delete, "api/v1/profile/searches"), ct);
            return response.IsSuccessStatusCode;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    public async Task<bool> RecordVacancyViewAsync(Guid vacancyId, CancellationToken ct = default)
    {
        if (vacancyId == Guid.Empty)
        {
            return false;
        }

        try
        {
            using HttpResponseMessage response = await SendCoreAsync(
                Post("api/v1/profile/views", new RecordVacancyViewRequest { VacancyId = vacancyId }, Json.RecordVacancyViewRequest),
                ct);
            return response.IsSuccessStatusCode;
        }
        catch (ApiException)
        {
            return false;
        }
    }

    public async IAsyncEnumerable<AiStreamEvent> SendAiMessageAsync(Guid? sessionId, string text, [EnumeratorCancellation] CancellationToken ct = default)
    {
        HttpRequestMessage request = Post(
            "api/v1/ai-consultant/messages",
            new AiMessageRequest { SessionId = sessionId, Text = text },
            Json.AiMessageRequest);

        await foreach (AiStreamEvent streamEvent in ReadEventsAsync(request, ct))
        {
            yield return streamEvent;
        }
    }

    public async Task DeleteAiSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await SendCoreAsync(new HttpRequestMessage(HttpMethod.Delete, $"api/v1/ai-consultant/sessions/{sessionId}"), ct);
    }

    public async IAsyncEnumerable<AiStreamEvent> AnalyzeVacanciesAsync(IReadOnlyList<Guid> vacancyIds, [EnumeratorCancellation] CancellationToken ct = default)
    {
        HttpRequestMessage request = Post(
            "api/v1/ai-consultant/analyze",
            new AiAnalyzeRequest { VacancyIds = vacancyIds },
            Json.AiAnalyzeRequest);

        await foreach (AiStreamEvent streamEvent in ReadEventsAsync(request, ct))
        {
            yield return streamEvent;
        }
    }

    /// <summary>
    /// Читає SSE-потік (text/event-stream) і перетворює кожну подію на типізовану модель.
    /// </summary>
    private async IAsyncEnumerable<AiStreamEvent> ReadEventsAsync(HttpRequestMessage request, [EnumeratorCancellation] CancellationToken ct)
    {
        using HttpResponseMessage response = await SendStreamingCoreAsync(request, ct);
        using Stream stream = await response.Content.ReadAsStreamAsync(ct);

        // SseParser (System.Net.ServerSentEvents) сам дотримується фреймінгу: багаторядкові data:,
        // коментарі (": keep-alive") ігноруються, подія віддається на порожньому рядку.
        SseParser<string> parser = SseParser.Create(stream);
        await foreach (SseItem<string> item in parser.EnumerateAsync(ct))
        {
            AiStreamEvent? streamEvent = ParseEvent(item, response.StatusCode);
            if (streamEvent is not null)
            {
                yield return streamEvent;
            }
        }
    }

    /// <summary>
    /// Розбирає подію за назвою; невідомі типи пропускаються (новий сервер — старий клієнт).
    /// </summary>
    private static AiStreamEvent? ParseEvent(SseItem<string> item, HttpStatusCode status)
    {
        try
        {
            return item.EventType switch
            {
                "meta" => JsonSerializer.Deserialize(item.Data, Json.AiMetaEvent),
                "vacancies" => JsonSerializer.Deserialize(item.Data, Json.AiVacanciesEvent),
                "delta" => JsonSerializer.Deserialize(item.Data, Json.AiDeltaEvent),
                "done" => JsonSerializer.Deserialize(item.Data, Json.AiDoneEvent),
                "error" => JsonSerializer.Deserialize(item.Data, Json.AiErrorEvent),
                "relevance" => JsonSerializer.Deserialize(item.Data, Json.AiRelevanceEvent),
                _ => null
            };
        }
        catch (JsonException ex)
        {
            throw new ApiException(status, null, "Сервер повернув некоректну відповідь.", ex);
        }
    }

    /// <summary>
    /// Як <see cref="SendCoreAsync"/>, але з <see cref="HttpCompletionOption.ResponseHeadersRead"/>,
    /// щоб тіло SSE читалося в міру надходження, а не після завершення відповіді.
    /// </summary>
    private async Task<HttpResponseMessage> SendStreamingCoreAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using (request)
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(null, null, "Не вдалося з'єднатися з сервером. Перевірте, чи запущено API.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(null, null, "Сервер не відповів вчасно.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            ApiError? error = await TryReadErrorAsync(response, ct);
            throw new AiConsultantException(
                response.StatusCode,
                error,
                !string.IsNullOrWhiteSpace(error?.Message) ? error.Message : DefaultAiMessage(response.StatusCode),
                string.IsNullOrWhiteSpace(error?.Code) ? DefaultAiCode(response.StatusCode) : error.Code);
        }
    }

    private static string? DefaultAiCode(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "UNAUTHORIZED",
        HttpStatusCode.Forbidden => "FORBIDDEN",
        HttpStatusCode.NotFound => "NOT_FOUND",
        HttpStatusCode.TooManyRequests => AiConsultantException.RateLimitedCode,
        HttpStatusCode.ServiceUnavailable => AiConsultantException.LlmUnavailableCode,
        _ => null
    };

    private static string DefaultAiMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Потрібно увійти в обліковий запис.",
        HttpStatusCode.Forbidden => "ШІ-консультант доступний лише користувачам із роллю «Шукач роботи».",
        HttpStatusCode.NotFound => "Сесію діалогу не знайдено.",
        HttpStatusCode.TooManyRequests => "Перевищено ліміт запитів до ШІ-консультанта. Спробуйте пізніше.",
        HttpStatusCode.ServiceUnavailable => "ШІ-консультант тимчасово недоступний. Скористайтеся звичайним пошуком у каталозі.",
        _ => $"Сервер повернув помилку {(int)status}."
    };

    private async Task<AuthResponse> AuthenticateAsync(HttpRequestMessage request, CancellationToken ct)
    {
        AuthResponse response = await SendAsync(request, Json.AuthResponse, ct);
        await _tokens.SetTokenAsync(response.Token, ct);
        return response;
    }

    private static HttpRequestMessage Post<TBody>(string url, TBody body, JsonTypeInfo<TBody> typeInfo) =>
        new(HttpMethod.Post, url) { Content = JsonContent.Create(body, typeInfo) };

    private async Task<T> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        using HttpResponseMessage response = await SendCoreAsync(request, ct);
        try
        {
            return await response.Content.ReadFromJsonAsync(typeInfo, ct)
                ?? throw new ApiException(response.StatusCode, null, "Сервер повернув порожню відповідь.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // InvalidOperationException — невідоме кодування в Content-Type (напр. сторінка captive portal).
            throw new ApiException(response.StatusCode, null, "Сервер повернув некоректну відповідь.", ex);
        }
    }

    /// <summary>
    /// Надсилає запит і повертає лише успішну відповідь; усе інше перетворює на <see cref="ApiException"/>.
    /// Скасування через <paramref name="ct"/> не перехоплюється — це не помилка.
    /// </summary>
    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using (request)
            {
                response = await _http.SendAsync(request, ct);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(null, null, "Не вдалося з'єднатися з сервером. Перевірте, чи запущено API.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout сигналізує через TaskCanceledException без скасування ct.
            throw new ApiException(null, null, "Сервер не відповів вчасно.", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            ApiError? error = await TryReadErrorAsync(response, ct);
            string message = !string.IsNullOrWhiteSpace(error?.Message)
                ? error.Message
                : DefaultMessage(response.StatusCode);
            throw new ApiException(response.StatusCode, error, message);
        }
    }

    private static async Task<ApiError?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            ApiErrorResponse? envelope = await response.Content.ReadFromJsonAsync(Json.ApiErrorResponse, ct);
            return envelope?.Error;
        }
        catch (JsonException)
        {
            // Порожнє тіло (напр. 401 від JwtBearer), HTML від проксі або не наш формат.
            return null;
        }
        catch (InvalidOperationException)
        {
            // Невідоме кодування в Content-Type (напр. сторінка проксі в windows-1251).
            return null;
        }
    }

    private static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Потрібно увійти в обліковий запис.",
        HttpStatusCode.Forbidden => "Недостатньо прав для цієї дії.",
        HttpStatusCode.NotFound => "Ресурс не знайдено.",
        _ => $"Сервер повернув помилку {(int)status}."
    };
}
