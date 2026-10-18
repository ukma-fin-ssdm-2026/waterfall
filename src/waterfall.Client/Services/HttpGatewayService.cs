// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Common;
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
        catch (JsonException ex)
        {
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
