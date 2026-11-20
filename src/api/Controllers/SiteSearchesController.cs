// AI: ендпоінти пошуку на сайтах на запит користувача (issue #62); перевірено: тести SiteSearchesControllerTests, самоперевірка автора (§8)

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Workers;
using Waterfall.Services.Models.SiteSearch;

namespace Waterfall.Api.Controllers;

/// <summary>
/// Пошук вакансій на сайтах за фільтрами користувача: сервер читає сайт, зберігає знайдене в каталог
/// і віддає результат, а поки читає — прогрес для шкали.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/site-searches")]
[Produces("application/json")]
[SwaggerTag("Пошук вакансій на Djinni, DOU, Work.ua та Robota.ua за фільтрами на запит користувача (один або кілька сайтів одночасно)")]
public class SiteSearchesController : ApiControllerBase
{
    public const string LimitErrorCode = "SITE_SEARCH_LIMIT";

    private readonly ISiteSearchService _siteSearch;

    public SiteSearchesController(ISiteSearchService siteSearch)
    {
        _siteSearch = siteSearch;
    }

    /// <summary>
    /// Запустити пошук на сайті за фільтрами.
    /// </summary>
    /// <remarks>
    /// Повертає ідентифікатор пошуку; стан і результат — через GET /api/v1/site-searches/{id}.
    /// Такий самий пошук протягом 3 годин віддається з бази без звернення до сайту (fromCache = true).
    /// </remarks>
    /// <param name="request">Сайт, фільтри в позначеннях сайту та кількість сторінок (1–5).</param>
    /// <response code="202">Пошук запущено (або результат уже готовий з кешу).</response>
    /// <response code="400">Невалідні фільтри.</response>
    /// <response code="401">Потрібно увійти.</response>
    /// <response code="429">Попередній пошук ще триває.</response>
    [HttpPost]
    [SwaggerOperation(
        Summary = "Запустити пошук на сайті",
        Description = "Сервер читає сторінки списку обраного сайту (Djinni, DOU, Work.ua або Robota.ua) з фільтрами користувача, зберігає вакансії в каталог і повертає їх через GET.",
        OperationId = "StartSiteSearch",
        Tags = ["SiteSearches"])]
    [ProducesResponseType(typeof(SiteSearchStatusDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public ActionResult<SiteSearchStatusDto> Start([FromBody] SiteSearchRequest request)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Потрібно увійти, щоб шукати на сайтах.");
        }

        SiteSearchStart start = _siteSearch.Start(request, userId);
        if (start.Status is null)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                ApiErrorResponse.Create(code: LimitErrorCode, message: start.Rejection!, requestId: RequestId));
        }

        return AcceptedAtAction(nameof(Get), new { id = start.Status.Id }, start.Status);
    }

    /// <summary>
    /// Стан пошуку на сайті: прогрес, а коли завершено — знайдені вакансії.
    /// </summary>
    /// <param name="id">Ідентифікатор пошуку з POST.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Стан пошуку.</response>
    /// <response code="404">Пошуку немає (чужий, застарілий або сервер перезапускався).</response>
    [HttpGet("{id:guid}")]
    [SwaggerOperation(
        Summary = "Стан пошуку на сайті",
        Description = "state: queued, running, completed або failed; pagesDone / pagesTotal — для шкали прогресу; vacancies — коли completed.",
        OperationId = "GetSiteSearch",
        Tags = ["SiteSearches"])]
    [ProducesResponseType(typeof(SiteSearchStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SiteSearchStatusDto>> Get(Guid id, CancellationToken ct = default)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Потрібно увійти, щоб шукати на сайтах.");
        }

        SiteSearchStatusDto? status = await _siteSearch.GetAsync(id, userId, ct);
        return status is null ? ErrorNotFound("Пошук не знайдено: можливо, він застарів. Запустіть його знову.") : Ok(status);
    }

    /// <summary>
    /// Запустити одночасний пошук на кількох сайтах (1–4) з індивідуальними фільтрами для кожного сайту.
    /// </summary>
    /// <remarks>
    /// Запускає паралельний обхід обраних сайтів (Djinni, DOU, Work.ua, Robota.ua).
    /// Прогрес кожного сайту та об'єднаний результат доступні через GET /api/v1/site-searches/batch/{id}.
    /// </remarks>
    /// <param name="request">Перелік запитів (по одному на кожен обраний сайт із власними налаштуваннями).</param>
    /// <response code="202">Пакетний пошук запущено (або всі результати вже готові з кешу).</response>
    /// <response code="400">Невалідні фільтри або дублювання сайтів.</response>
    /// <response code="401">Потрібно увійти.</response>
    /// <response code="429">Попередній пошук ще триває.</response>
    [HttpPost("batch")]
    [SwaggerOperation(
        Summary = "Запустити одночасний пошук на кількох сайтах",
        Description = "Паралельно читає до 4 обраних сайтів (Djinni, DOU, Work.ua, Robota.ua) із власними фільтрами для кожного, зберігає знайдене в каталог і повертає об'єднаний результат через GET /batch/{id}.",
        OperationId = "StartMultiSiteSearch",
        Tags = ["SiteSearches"])]
    [ProducesResponseType(typeof(MultiSiteSearchStatusDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public ActionResult<MultiSiteSearchStatusDto> StartBatch([FromBody] MultiSiteSearchRequest request)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Потрібно увійти, щоб шукати на сайтах.");
        }

        MultiSiteSearchStart start = _siteSearch.StartBatch(request, userId);
        if (start.Status is null)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests,
                ApiErrorResponse.Create(code: LimitErrorCode, message: start.Rejection!, requestId: RequestId));
        }

        return AcceptedAtAction(nameof(GetBatch), new { id = start.Status.Id }, start.Status);
    }

    /// <summary>
    /// Стан одночасного пошуку на кількох сайтах: загальний прогрес, прогрес кожного сайту та об'єднані вакансії.
    /// </summary>
    /// <param name="id">Ідентифікатор пакетного пошуку з POST /batch.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Стан пакетного пошуку.</response>
    /// <response code="401">Потрібно увійти.</response>
    /// <response code="404">Пакетний пошук не знайдено.</response>
    [HttpGet("batch/{id:guid}")]
    [SwaggerOperation(
        Summary = "Стан багатосайтового пошуку",
        Description = "Повертає загальний прогрес (pagesDone / pagesTotal), стан кожного сайту в масиві sites та об'єднаний список vacancies після завершення.",
        OperationId = "GetMultiSiteSearch",
        Tags = ["SiteSearches"])]
    [ProducesResponseType(typeof(MultiSiteSearchStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MultiSiteSearchStatusDto>> GetBatch(Guid id, CancellationToken ct = default)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Потрібно увійти, щоб шукати на сайтах.");
        }

        MultiSiteSearchStatusDto? status = await _siteSearch.GetBatchAsync(id, userId, ct);
        return status is null ? ErrorNotFound("Пошук не знайдено: можливо, він застарів. Запустіть його знову.") : Ok(status);
    }
}
