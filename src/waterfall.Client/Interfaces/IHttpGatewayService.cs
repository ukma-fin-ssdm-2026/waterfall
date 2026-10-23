// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.SavedVacancies;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Interfaces;

/// <summary>
/// HTTP-шлюз клієнта до Waterfall API (/api/v1). Усі помилки — <see cref="ApiException"/>.
/// </summary>
public interface IHttpGatewayService
{
    /// <summary>
    /// Сторінка каталогу; порожній <paramref name="search"/> — без фільтра.
    /// </summary>
    Task<PagedResult<VacancyDto>> GetVacanciesAsync(string? search, int page, int pageSize, CancellationToken ct = default);

    Task<VacancyDto> GetVacancyAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<SavedVacancyDto>> GetSavedVacanciesAsync(CancellationToken ct = default);

    Task<SavedVacancyDto> SaveVacancyAsync(Guid vacancyId, string? notes = null, CancellationToken ct = default);

    /// <summary>
    /// Прибирає вакансію з «Обраного» за ідентифікатором вакансії
    /// (сервер приймає як id запису збереження, так і vacancyId).
    /// </summary>
    Task UnsaveVacancyAsync(Guid vacancyId, CancellationToken ct = default);

    /// <summary>
    /// Вхід; у разі успіху токен зберігається в <see cref="IAuthTokenStore"/>.
    /// </summary>
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Реєстрація; у разі успіху токен зберігається в <see cref="IAuthTokenStore"/>.
    /// </summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    /// <summary>
    /// Вихід: видаляє збережений токен (access-JWT без серверного відкликання).
    /// </summary>
    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>
    /// Отримати збережену в базі адресу поточного користувача.
    /// </summary>
    Task<string?> GetProfileAddressAsync(CancellationToken ct = default);

    /// <summary>
    /// Зберегти адресу поточного користувача у базі даних (UserPersonalData).
    /// </summary>
    Task SaveProfileAddressAsync(string address, CancellationToken ct = default);

    /// <summary>
    /// Отримати географічні координати адреси з постійного геокешу сервера (GeoLocationCache).
    /// </summary>
    Task<Waterfall.Client.Models.Geo.CoordinatesDto?> GetCoordinatesAsync(string address, CancellationToken ct = default);
}
