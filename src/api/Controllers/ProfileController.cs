// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Profile;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Controllers;

/// <summary>
/// Керування профілем та персональними даними користувача.
/// Ендпоінти статистики (FR-007, US-008): поведінкові ПД — лише для власника (THR-12),
/// без аналітичних агрегатів про інших (THR-13).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/profile")]
[Produces("application/json")]
[SwaggerTag("Керування персональними даними та локаційними налаштуваннями профілю")]
public class ProfileController : ApiControllerBase
{
    private readonly IUserProfileService _profileService;
    private readonly IUserStatsService _userStatsService;
    private readonly IVacancyService _vacancyService;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        IUserProfileService profileService,
        IUserStatsService userStatsService,
        IVacancyService vacancyService,
        ILogger<ProfileController> logger)
    {
        _profileService = profileService;
        _userStatsService = userStatsService;
        _vacancyService = vacancyService;
        _logger = logger;
    }

    /// <summary>
    /// Отримати збережену адресу користувача для розрахунку відстаней.
    /// </summary>
    [HttpGet("address")]
    [SwaggerOperation(Summary = "Отримати адресу користувача", Description = "Повертає збережену домашню/поточну адресу для розрахунку відстані до офісів.", OperationId = "GetProfileAddress")]
    [ProducesResponseType(typeof(UserAddressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserAddressDto>> GetAddress(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        string? address = await _profileService.GetUserAddressAsync(userId, ct);
        return Ok(new UserAddressDto { Address = address });
    }

    /// <summary>
    /// Зберегти або оновити адресу користувача в базі даних.
    /// </summary>
    [HttpPut("address")]
    [SwaggerOperation(Summary = "Зберегти адресу користувача", Description = "Зберігає або оновлює адресу в таблиці UserPersonalData.", OperationId = "SaveProfileAddress")]
    [ProducesResponseType(typeof(UserAddressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserAddressDto>> SaveAddress([FromBody] UserAddressDto request, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        if (string.IsNullOrWhiteSpace(request?.Address))
        {
            return ErrorBadRequest("Адреса не може бути порожньою.");
        }

        await _profileService.SaveUserAddressAsync(userId, request.Address, ct);
        _logger.LogInformation("Оновлено адресу для користувача {UserId}.", userId);

        return Ok(new UserAddressDto { Address = request.Address.Trim() });
    }

    /// <summary>
    /// Отримати особисту статистику активності користувача (FR-007, US-008).
    /// </summary>
    /// <response code="200">Статистику успішно отримано.</response>
    /// <response code="401">Користувач не авторизований.</response>
    [HttpGet("stats")]
    [SwaggerOperation(
        Summary = "Отримати особисту статистику активності",
        Description = "Повертає кількість збережених вакансій і пошуків, топ технологій та категорій за переглядами і збереженнями, останні пошукові запити.",
        OperationId = "GetProfileStats")]
    [ProducesResponseType(typeof(UserStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserStatsDto>> GetStats(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        _logger.LogInformation("Запит особистої статистики користувача {UserId}", userId);
        UserStatsDto stats = await _userStatsService.GetStatsAsync(userId, ct);
        return Ok(stats);
    }

    /// <summary>
    /// Записати виконаний пошуковий запит в історію (FR-007).
    /// </summary>
    /// <param name="request">Текст виконаного пошуку.</param>
    /// <response code="202">Пошуковий запит записано в історію.</response>
    /// <response code="204">Запит пропущено: порожній або повтор попереднього записаного запиту.</response>
    /// <response code="400">Помилка валідації вхідних даних.</response>
    /// <response code="401">Користувач не авторизований.</response>
    /// <response code="403">Запис активності доступний лише шукачу роботи (data-model.md §7, інваріант 2).</response>
    [HttpPost("searches")]
    [SwaggerOperation(
        Summary = "Записати пошуковий запит в історію",
        Description = "Додає виконаний пошук до історії особистої статистики. Повтори і порожні запити пропускаються; текст запиту в логи не пишеться (THR-08).",
        OperationId = "RecordSearch")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RecordSearch([FromBody] RecordSearchRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        if (!ModelState.IsValid)
        {
            return ErrorBadRequest("Невалідний запит запису пошукового запиту.");
        }

        UserStatsRecordResult result = await _userStatsService.RecordSearchAsync(userId, request.Query, ct);
        return result switch
        {
            UserStatsRecordResult.Recorded => Accepted(),
            UserStatsRecordResult.Forbidden => ErrorForbidden("Запис активності доступний лише шукачеві роботи."),
            _ => NoContent()
        };
    }

    /// <summary>
    /// Очистити історію пошукових запитів користувача.
    /// </summary>
    /// <response code="204">Історію очищено (порожня історія також вважається успіхом).</response>
    /// <response code="401">Користувач не авторизований.</response>
    [HttpDelete("searches")]
    [SwaggerOperation(
        Summary = "Очистити історію пошукових запитів",
        Description = "Видаляє всі записані пошукові запити поточного користувача. Генерація відсутніх записів не є помилкою.",
        OperationId = "ClearSearchHistory")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ClearSearchHistory(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        await _userStatsService.ClearSearchHistoryAsync(userId, ct);
        return NoContent();
    }

    /// <summary>
    /// Записати перегляд вакансії в історію (FR-007).
    /// </summary>
    /// <param name="request">Ідентифікатор переглянутої вакансії.</param>
    /// <response code="202">Перегляд записано; повтор протягом короткого інтервалу приймається без повторного запису.</response>
    /// <response code="400">Помилка валідації вхідних даних.</response>
    /// <response code="401">Користувач не авторизований.</response>
    /// <response code="403">Запис активності доступний лише шукачу роботи (data-model.md §7, інваріант 2).</response>
    /// <response code="404">Вакансію з вказаним vacancy_id не знайдено.</response>
    [HttpPost("views")]
    [SwaggerOperation(
        Summary = "Записати перегляд вакансії",
        Description = "Додає перегляд вакансії до історії особистої статистики та джерела агрегації частоти технологій/категорій (FR-006, FR-007).",
        OperationId = "RecordVacancyView")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordVacancyView([FromBody] RecordVacancyViewRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        if (!ModelState.IsValid)
        {
            return ErrorBadRequest("Невалідний запит запису перегляду вакансії.");
        }

        // Існування вакансії перевіряється контролером — як у SavedVacanciesController,
        // щоб невідомий ID відображався в 404, а не в помилку зовнішнього ключа.
        VacancyDto? vacancy = await _vacancyService.GetByIdAsync(request.VacancyId, ct, userId);
        if (vacancy is null)
        {
            return ErrorNotFound($"Неможливо записати перегляд: вакансію з ID '{request.VacancyId}' не знайдено.");
        }

        UserStatsRecordResult result = await _userStatsService.RecordVacancyViewAsync(userId, request.VacancyId, ct);
        return result switch
        {
            UserStatsRecordResult.Forbidden => ErrorForbidden("Запис активності доступний лише шукачеві роботи."),
            _ => Accepted()
        };
    }
}
