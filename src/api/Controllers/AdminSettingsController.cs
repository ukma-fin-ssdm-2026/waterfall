// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: системні налаштування сервера лише для ролі Admin (без доступу до каталогу); перевірено: тести AdminSettingsControllerTests, самоперевірка автора (§8)

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Admin;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Services;
using Waterfall.Api.Workers;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Api.Controllers;

/// <summary>
/// Системні налаштування сервера для адміністратора: фоновий збір вакансій та його інтервал.
/// Інших прав у ролі Admin немає: каталог вакансій наповнює лише серверний парсер.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/admin/settings")]
[Produces("application/json")]
[SwaggerTag("Системні налаштування сервера (лише роль Admin)")]
public class AdminSettingsController : ApiControllerBase
{
    /// <summary>Найдовший інтервал фонового збору, який можна задати (як найдовший варіант у застосунку).</summary>
    public static readonly TimeSpan MaxInterval = TimeSpan.FromDays(1);

    private readonly ISystemSettingsStore _store;
    private readonly ILogger<AdminSettingsController> _logger;

    public AdminSettingsController(ISystemSettingsStore store, ILogger<AdminSettingsController> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Поточні системні налаштування сервера.
    /// </summary>
    /// <response code="200">Налаштування фонового збору.</response>
    /// <response code="401">Потрібно увійти.</response>
    /// <response code="403">Лише для ролі Admin.</response>
    [HttpGet]
    [SwaggerOperation(
        Summary = "Отримати системні налаштування",
        Description = "Повертає налаштування фонового збору вакансій: чи увімкнений, інтервал, кількість сторінок і паузу між запитами.",
        OperationId = "GetSystemSettings",
        Tags = ["AdminSettings"])]
    [ProducesResponseType(typeof(SystemSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public ActionResult<SystemSettingsDto> Get()
    {
        if (!IsAdmin())
        {
            return AdminOnly();
        }

        return Ok(_store.Get());
    }

    /// <summary>
    /// Змінити системні налаштування сервера.
    /// </summary>
    /// <remarks>
    /// Увімкнення чи вимкнення фонового збору діє за кілька секунд без перезапуску; новий інтервал — після перезапуску API.
    /// </remarks>
    /// <param name="request">Увімкнення фонового збору та інтервал (від 10 хвилин до доби).</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Налаштування збережено; у відповіді — нові значення.</response>
    /// <response code="400">Невалідний інтервал.</response>
    /// <response code="401">Потрібно увійти.</response>
    /// <response code="403">Лише для ролі Admin.</response>
    [HttpPut]
    [SwaggerOperation(
        Summary = "Змінити системні налаштування",
        Description = "Вмикає чи вимикає фоновий збір вакансій і задає його інтервал. Зміни зберігаються в appsettings.Admin.json сервера.",
        OperationId = "UpdateSystemSettings",
        Tags = ["AdminSettings"])]
    [ProducesResponseType(typeof(SystemSettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SystemSettingsDto>> Update([FromBody] UpdateSystemSettingsRequest? request, CancellationToken ct = default)
    {
        if (!IsAdmin())
        {
            return AdminOnly();
        }

        if (request is null || !ModelState.IsValid)
        {
            return ErrorBadRequest("Тіло запиту налаштувань не може бути порожнім або невалідним.");
        }

        // Ті самі межі, що й під час старту сервера (IngestionOptionsValidator): надто частий збір навантажує сайти
        if (request.Interval < IngestionOptionsValidator.MinInterval || request.Interval > MaxInterval)
        {
            return ErrorBadRequest($"Інтервал фонового збору має бути від {IngestionOptionsValidator.MinInterval.TotalMinutes:0} хвилин до доби.");
        }

        SystemSettingsDto updated = await _store.UpdateAsync(request, ct);
        _logger.LogInformation(
            "Адміністратор змінив системні налаштування: фоновий збір {Enabled}, інтервал {Interval}.",
            updated.BackgroundParsingEnabled ? "увімкнено" : "вимкнено", updated.Interval);
        return Ok(updated);
    }

    private bool IsAdmin()
    {
        string? roleValue = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
        return Enum.TryParse(roleValue, ignoreCase: true, out UserRole role) && role == UserRole.Admin;
    }

    private ObjectResult AdminOnly() =>
        ErrorForbidden("Системні налаштування доступні лише адміністратору.");
}
