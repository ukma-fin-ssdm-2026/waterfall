// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Controllers;
/// <summary>
/// Керування каталогом вакансій: пошук, фільтрація, створення та оновлення.
/// </summary>
[ApiController]
[Route("api/v1/vacancies")]
[Produces("application/json")]
[SwaggerTag("Керування агрегованими вакансіями платформи Waterfall (CRUD)")]
public class VacanciesController : ApiControllerBase
{
    private readonly IVacancyService _vacancyService;
    private readonly ILogger<VacanciesController> _logger;

    public VacanciesController(IVacancyService vacancyService, ILogger<VacanciesController> logger)
    {
        _vacancyService = vacancyService;
        _logger = logger;
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

        PagedResult<VacancyDto> result = await _vacancyService.GetAllAsync(filter, ct);
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
        VacancyDto? vacancy = await _vacancyService.GetByIdAsync(id, ct);
        if (vacancy is null)
        {
            return ErrorNotFound($"Вакансію з ID '{id}' не знайдено в каталозі.");
        }

        return Ok(vacancy);
    }

    /// <summary>
    /// Створити або імпортувати нову вакансію.
    /// </summary>
    /// <param name="request">Дані нової вакансії.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="201">Вакансію успішно створено. У заголовку Location міститься посилання на новий ресурс.</response>
    /// <response code="400">Помилка валідації вхідного запиту.</response>
    [HttpPost]
    [SwaggerOperation(
        Summary = "Створити вакансію",
        Description = "Додає нову вакансію до єдиного каталогу (ручне розміщення або збереження модулем збору даних).",
        OperationId = "CreateVacancy",
        Tags = ["Vacancies"])]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VacancyDto>> Create([FromBody] CreateVacancyRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ErrorBadRequest("Невалідні дані запиту створення вакансії.");
        }

        VacancyDto created = await _vacancyService.CreateAsync(request, ct);

        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id },
            created);
    }

    /// <summary>
    /// Оновити дані існуючої вакансії.
    /// </summary>
    /// <param name="id">Унікальний ідентифікатор вакансії, що оновлюється.</param>
    /// <param name="request">Оновлені поля вакансії.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Вакансію успішно оновлено.</response>
    /// <response code="400">Помилка валідації полів запиту.</response>
    /// <response code="404">Вакансію для оновлення не знайдено.</response>
    [HttpPut("{id:guid}")]
    [SwaggerOperation(
        Summary = "Оновити вакансію",
        Description = "Оновлює інформацію про вакансію за її унікальним ідентифікатором.",
        OperationId = "UpdateVacancy",
        Tags = ["Vacancies"])]
    [ProducesResponseType(typeof(VacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VacancyDto>> Update([FromRoute] Guid id, [FromBody] UpdateVacancyRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ErrorBadRequest("Невалідні дані запиту оновлення вакансії.");
        }

        VacancyDto? updated = await _vacancyService.UpdateAsync(id, request, ct);

        if (updated is null)
        {
            return ErrorNotFound($"Вакансію з ID '{id}' не знайдено для оновлення.");
        }

        return Ok(updated);
    }

    /// <summary>
    /// Видалити вакансію з каталогу.
    /// </summary>
    /// <param name="id">Унікальний ідентифікатор вакансії.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="204">Вакансію успішно видалено.</response>
    /// <response code="404">Вакансію не знайдено.</response>
    [HttpDelete("{id:guid}")]
    [SwaggerOperation(
        Summary = "Видалити вакансію",
        Description = "Видаляє вакансію за її ідентифікатором з каталогу системи.",
        OperationId = "DeleteVacancy",
        Tags = ["Vacancies"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        bool deleted = await _vacancyService.DeleteAsync(id, ct);

        if (!deleted)
        {
            return ErrorNotFound($"Вакансію з ID '{id}' не знайдено для видалення.");
        }

        return NoContent();
    }
}
