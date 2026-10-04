// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Models.SavedVacancies;
using Waterfall.Api.Models.Vacancies;
using Waterfall.Api.Services;

namespace Waterfall.Api.Controllers;
/// <summary>
/// Керування збереженими вакансіями (лайками / закладками) шукача роботи.
/// </summary>
[ApiController]
[Route("api/v1/saved-vacancies")]
[Produces("application/json")]
[SwaggerTag("Керування вподобаними («лайкнутими») вакансіями для рекомендацій та статистики")]
public class SavedVacanciesController : ApiControllerBase
{
    private readonly ISavedVacancyRepository _savedRepository;
    private readonly IVacancyRepository _vacancyRepository;
    private readonly ILogger<SavedVacanciesController> _logger;

    public SavedVacanciesController(
        ISavedVacancyRepository savedRepository,
        IVacancyRepository vacancyRepository,
        ILogger<SavedVacanciesController> logger)
    {
        _savedRepository = savedRepository;
        _vacancyRepository = vacancyRepository;
        _logger = logger;
    }

    /// <summary>
    /// Отримати перелік усіх збережених вакансій користувача.
    /// </summary>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Перелік збережених вакансій успішно отримано.</response>
    [HttpGet]
    [SwaggerOperation(
        Summary = "Отримати список збережених вакансій",
        Description = "Повертає перелік вакансій, які користувач зберіг («лайкнув») для подальшого відгуку чи рекомендацій ШІ.",
        OperationId = "GetSavedVacancies",
        Tags = ["SavedVacancies"])]
    [ProducesResponseType(typeof(List<SavedVacancyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<SavedVacancyDto>>> GetAll(CancellationToken ct)
    {
        _logger.LogInformation("Запит списку збережених вакансій");
        IReadOnlyList<SavedVacancyDto> items = await _savedRepository.GetAllAsync(ct);
        return Ok(items);
    }

    /// <summary>
    /// Отримати детальну інформацію про збережену вакансію за її ID.
    /// </summary>
    /// <param name="id">Ідентифікатор запису збереженої вакансії.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Збережену вакансію успішно знайдено.</response>
    /// <response code="404">Збережену вакансію не знайдено.</response>
    [HttpGet("{id:guid}")]
    [SwaggerOperation(
        Summary = "Отримати збережену вакансію за ID",
        Description = "Повертає інформацію про збережену вакансію включно з нотатками шукача роботи.",
        OperationId = "GetSavedVacancyById",
        Tags = ["SavedVacancies"])]
    [ProducesResponseType(typeof(SavedVacancyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SavedVacancyDto>> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        SavedVacancyDto? item = await _savedRepository.GetByIdAsync(id, ct);

        if (item is null)
        {
            return ErrorNotFound($"Збережену вакансію з ID '{id}' не знайдено.");
        }

        return Ok(item);
    }

    /// <summary>
    /// Додати вакансію до списку збережених («лайкнути»).
    /// </summary>
    /// <param name="request">Ідентифікатор вакансії та необов'язкові нотатки користувача.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="201">Вакансію успішно додано до збережених.</response>
    /// <response code="400">Помилка валідації вхідних даних.</response>
    /// <response code="404">Вакансію з вказаним vacancy_id не знайдено.</response>
    /// <response code="409">Ця вакансія вже додана до списку збережених.</response>
    [HttpPost]
    [SwaggerOperation(
        Summary = "Зберегти вакансію у вподобані",
        Description = "Додає вакансію до списку вподобань.",
        OperationId = "SaveVacancy",
        Tags = ["SavedVacancies"])]
    [ProducesResponseType(typeof(SavedVacancyDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SavedVacancyDto>> Save([FromBody] SaveVacancyRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ErrorBadRequest("Невалідний запит збереження вакансії.");
        }

        VacancyDto? vacancy = await _vacancyRepository.GetByIdAsync(request.VacancyId, ct);

        if (vacancy is null)
        {
            return ErrorNotFound($"Неможливо зберегти вакансію: вакансію з ID '{request.VacancyId}' не знайдено.");
        }

        if (await _savedRepository.ExistsForVacancyAsync(request.VacancyId, ct))
        {
            return ErrorConflict($"Вакансію з ID '{request.VacancyId}' уже було додано до списку збережених раніше.");
        }

        SavedVacancyDto saved = await _savedRepository.SaveAsync(request, ct);

        return CreatedAtAction(
            nameof(GetById),
            new { id = saved.Id },
            saved);
    }

    /// <summary>
    /// Видалити вакансію зі списку збережених.
    /// </summary>
    /// <param name="id">Унікальний ідентифікатор збереженої вакансії.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="204">Вакансію успішно видалено зі збережених.</response>
    /// <response code="404">Запис у збережених вакансіях не знайдено.</response>
    [HttpDelete("{id:guid}")]
    [SwaggerOperation(
        Summary = "Видалити збережену вакансію",
        Description = "Видаляє вказану вакансію зі списку вподобань користувача.",
        OperationId = "DeleteSavedVacancy",
        Tags = ["SavedVacancies"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        bool deleted = await _savedRepository.DeleteAsync(id, ct);

        if (!deleted)
        {
            return ErrorNotFound($"Збережену вакансію з ID '{id}' не знайдено для видалення.");
        }

        return NoContent();
    }
}
