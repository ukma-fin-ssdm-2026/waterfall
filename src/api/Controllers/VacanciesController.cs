// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Controllers;
/// <summary>
/// Каталог вакансій: пошук, фільтрація, рекомендації та синхронізація з клієнтом.
/// Змінює каталог лише серверний парсер, тож ендпоінтів ручного створення, зміни й видалення немає.
/// </summary>
[ApiController]
[Route("api/v1/vacancies")]
[Produces("application/json")]
[SwaggerTag("Каталог агрегованих вакансій платформи Waterfall (лише читання; наповнює серверний парсер)")]
public class VacanciesController : ApiControllerBase
{
    private readonly IVacancyService _vacancyService;
    private readonly IRecommendationService? _recommendationService;
    private readonly ILogger<VacanciesController> _logger;

    public VacanciesController(
        IVacancyService vacancyService,
        ILogger<VacanciesController> logger,
        IRecommendationService? recommendationService = null)
    {
        _vacancyService = vacancyService;
        _logger = logger;
        _recommendationService = recommendationService;
    }

    /// <summary>
    /// Отримати список вакансій із фільтрацією та пагінацією.
    /// </summary>
    /// <remarks>
    /// Повертає список вакансій за заданими параметрами пошуку (назва, навички), фільтрами категорій,
    /// мінімальної зарплати та джерела агрегації.
    /// </remarks>
    /// <param name="filter">Параметри пагінації та фільтрації.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Успішне отримання переліку вакансій.</response>
    /// <response code="400">Помилка валідації параметрів пагінації чи фільтрації.</response>
    [HttpGet]
    [SwaggerOperation(
        Summary = "Отримати список вакансій",
        Description = "Повертає стандартизований пагінований список вакансій з можливістю повнотекстового пошуку та фільтрації.",
        OperationId = "GetVacancies",
        Tags = ["Vacancies"])]
    [ProducesResponseType(typeof(PagedResult<VacancyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<VacancyDto>>> GetAll([FromQuery] VacancyFilterParameters filter, CancellationToken ct)
    {
        _logger.LogInformation("Отримання вакансій: сторінка {Page}, розмір {PageSize}, фільтр search='{Search}'",
            filter.Page, filter.PageSize, filter.Search);

        PagedResult<VacancyDto> result = await _vacancyService.GetAllAsync(filter, ct, CurrentUserId);
        return Ok(result);
    }

    /// <summary>
    /// Отримати персоналізовані рекомендації вакансій (FR-006, US-004).
    /// </summary>
    /// <param name="limit">Максимальна кількість рекомендованих вакансій (1–100, за замовчуванням 20).</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Рекомендації успішно сформовано (або порожній список, якщо збережень ще немає).</response>
    /// <response code="401">Користувач не авторизований.</response>
    [HttpGet("recommendations")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Отримати персоналізовані рекомендації",
        Description = "Підбирає нові вакансії на основі збережених вакансій та історії пошуків користувача, виключаючи вже збережені.",
        OperationId = "GetRecommendedVacancies",
        Tags = ["Vacancies"])]
    [ProducesResponseType(typeof(RecommendationsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RecommendationsResultDto>> GetRecommendations(
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        if (_recommendationService is null)
        {
            throw new InvalidOperationException("Сервіс рекомендацій не зареєстровано в DI.");
        }

        RecommendationsResultDto result = await _recommendationService.GetRecommendationsAsync(userId, limit, ct);
        return Ok(result);
    }

    /// <summary>
    /// Отримати детальну інформацію про конкретну вакансію за її ID.
    /// </summary>
    /// <param name="id">Унікальний глобальний ідентифікатор вакансії (GUID).</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Вакансію успішно знайдено.</response>
    /// <response code="404">Вакансію за вказаним ідентифікатором не знайдено.</response>
    [HttpGet("{id:guid}")]
    [SwaggerOperation(
        Summary = "Отримати вакансію за ідентифікатором",
        Description = "Повертає повний опис, вимоги, рівень зарплати та контактні дані вакансії.",
        OperationId = "GetVacancyById",
        Tags = ["Vacancies"])]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VacancyDto>> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        VacancyDto? vacancy = await _vacancyService.GetByIdAsync(id, ct, CurrentUserId);
        if (vacancy is null)
        {
            return ErrorNotFound($"Вакансію з ID '{id}' не знайдено в каталозі.");
        }

        return Ok(vacancy);
    }

    /// <summary>
    /// Двостороння синхронізація бази вакансій між локальною БД клієнта та сервером.
    /// </summary>
    /// <remarks>
    /// Клієнт надсилає до 500 вакансій за один запит (<see cref="VacancySyncRequestDto.MaxClientVacanciesPerRequest"/>),
    /// зібраних або змінених в офлайні; запити з понад 500 вакансіями відхиляються зі статусом 400 Bad Request.
    /// Сервер об'єднує їх зі своєю базою (дедуплікація за Source+ExternalId / Id / SourceUrl, Last-Write-Wins за LastSeenAt)
    /// та повертає всі актуальні вакансії сервера (або зміни з моменту LastSyncedAt).
    /// Вакансії клієнта зберігаються лише з VacancySync:AcceptClientVacancies = true; за замовчуванням (false) сервер їх ігнорує й лише віддає свої.
    /// </remarks>
    /// <param name="request">Несинхронізовані локальні вакансії клієнта (до 500 записів) та мітка часу останньої синхронізації.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Синхронізація успішна.</response>
    /// <response code="400">Порожній, невалідний запит синхронізації або перевищено ліміт у 500 вакансій за запит.</response>
    /// <response code="401">Потрібно увійти: сервер віддає базу лише користувачам застосунку.</response>
    [HttpPost("sync")]
    [Authorize]
    [SwaggerOperation(
        Summary = "Синхронізувати базу вакансій",
        Description = "Двосторонній обмін вакансіями між локальною БД клієнта (офлайн-збір і правки, до 500 записів у clientVacancies; перевищення ліміту відхиляється з 400 Bad Request) та серверною БД. Вакансії від клієнта сервер зберігає лише з VacancySync:AcceptClientVacancies = true (за замовчуванням false: clientVacancies ігноруються, клієнт лише отримує вакансії сервера).",
        OperationId = "SyncVacancies",
        Tags = ["Vacancies"])]
    [ProducesResponseType(typeof(VacancySyncResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<VacancySyncResponseDto>> Sync(
        [FromBody] VacancySyncRequestDto? request,
        CancellationToken ct = default)
    {
        if (request is null || !ModelState.IsValid)
        {
            return ErrorBadRequest("Тіло запиту синхронізації вакансій не може бути порожнім або невалідним.");
        }

        if (request.ClientVacancies is { Count: > VacancySyncRequestDto.MaxClientVacanciesPerRequest })
        {
            return ErrorBadRequest(
                $"За один запит синхронізації дозволено не більше {VacancySyncRequestDto.MaxClientVacanciesPerRequest} вакансій.");
        }

        VacancySyncResponseDto response = await _vacancyService.SyncVacanciesAsync(request, ct);
        return Ok(response);
    }
}
