// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: дія /analyze з подією relevance перед LLM (FR-010); перевірено: тести AIConsultantControllerTests і VacancyAnalysisServiceTests

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Extensions;
using Waterfall.Api.Models.Ai;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Services;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Llm;
using Waterfall.Services.Models.Ai;

namespace Waterfall.Api.Controllers;

/// <summary>
/// ШІ-консультант шукача роботи: діалог природною мовою, підбір вакансій із каталогу,
/// стрімінг відповіді через Server-Sent Events (FR-002, FR-009, US-005) та аналіз вибраних
/// вакансій за профілем збережених (FR-010).
/// </summary>
[ApiController]
[Authorize(Roles = "JobSeeker")]
[Route("api/v1/ai-consultant")]
[SwaggerTag("ШІ-консультант: підбір вакансій звичайною мовою (SSE)")]
public class AIConsultantController : ApiControllerBase
{
    private readonly IAIConsultantService _consultantService;
    private readonly IVacancyAnalysisService _analysisService;
    private readonly ILogger<AIConsultantController> _logger;

    public AIConsultantController(
        IAIConsultantService consultantService,
        IVacancyAnalysisService analysisService,
        ILogger<AIConsultantController> logger)
    {
        _consultantService = consultantService;
        _analysisService = analysisService;
        _logger = logger;
    }

    /// <summary>
    /// Надіслати репліку ШІ-консультанту та отримати відповідь потоком SSE.
    /// </summary>
    /// <remarks>
    /// Послідовність подій: <c>meta</c>, <c>vacancies</c>, багато <c>delta</c>, завершальна <c>done</c>.
    /// Помилки до початку стрімінгу повертаються звичайним JSON (400/401/403/404/429/503),
    /// після початку — термінальною подією <c>error</c>.
    /// </remarks>
    /// <param name="request">Текст запиту та (необов'язково) id поточного діалогу.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Потік подій SSE.</response>
    /// <response code="400">Невалідне тіло запиту.</response>
    /// <response code="403">Користувач не є шукачем роботи.</response>
    /// <response code="404">Діалог не знайдено або він належить іншому користувачу.</response>
    /// <response code="429">Перевищено ліміт запитів до ШІ.</response>
    /// <response code="503">LLM недоступна; слід скористатися звичайним пошуком (REL-003).</response>
    [HttpPost("messages")]
    [EnableRateLimiting(AiConsultantRateLimitingExtensions.PolicyName)]
    [SwaggerOperation(
        Summary = "Надіслати повідомлення ШІ-консультанту (SSE)",
        Description = "Витягує структурований фільтр із запиту, шукає вакансії в каталозі та стрімить "
            + "коротку відповідь ШІ. Якщо точних збігів немає — повертає до 3 найближчих вакансій із позначкою.",
        OperationId = "SendAiConsultantMessage",
        Tags = ["AiConsultant"])]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/event-stream")]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> SendMessage([FromBody] AIConsultantMessageRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId)
        {
            return ErrorUnauthorized("Потрібна автентифікація для роботи з ШІ-консультантом.");
        }

        AIConsultantTurn turn;
        try
        {
            // Увесь ризик відмови LLM (витяг фільтра) припадає на цей виклик — до першого байта SSE,
            // тож клієнт отримує звичайну помилку 503, а не обірваний потік (REL-003)
            turn = await _consultantService.PrepareAsync(userId, request.SessionId, request.Text, ct);
        }
        catch (ChatSessionNotFoundException)
        {
            return ErrorNotFound("Діалог з ШІ-консультантом не знайдено.");
        }
        catch (LlmUnavailableException exception)
        {
            _logger.LogWarning(
                "ШІ-консультант недоступний ({FailureKind}); запит користувача відхилено до стрімінгу",
                exception.Kind);
            return ErrorServiceUnavailable(
                "ШІ-консультант тимчасово недоступний. Скористайтеся звичайним пошуком за фільтрами каталогу.",
                AiConsultantErrorCodes.LlmUnavailable);
        }

        _logger.LogInformation(
            "ШІ-консультант: діалог {SessionId}, знайдено {VacancyCount} вакансій (exact={Exact})",
            turn.SessionId,
            turn.Vacancies.Count,
            turn.Exact);

        SseEventWriter.PrepareResponse(Response);

        try
        {
            await SseEventWriter.WriteAsync(
                Response,
                AiConsultantEventNames.Meta,
                new MetaEventPayload { SessionId = turn.SessionId, AppliedFilter = turn.Filter },
                ct);

            await SseEventWriter.WriteAsync(
                Response,
                AiConsultantEventNames.Vacancies,
                new VacanciesEventPayload
                {
                    Items = turn.Vacancies,
                    Exact = turn.Exact,
                    Relaxed = turn.Relaxed,
                    Disclaimer = turn.Disclaimer
                },
                ct);

            await StreamAnswerAsync(turn, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Клієнт закрив з'єднання: нічого не дописуємо й не логуємо як помилку
            _logger.LogInformation("Клієнт закрив SSE-з'єднання ШІ-консультанта (діалог {SessionId})", turn.SessionId);
        }
        catch (IOException exception)
        {
            _logger.LogInformation(exception, "SSE-з'єднання ШІ-консультанта обірвано (діалог {SessionId})", turn.SessionId);
        }

        return new EmptyResult();
    }

    /// <summary>
    /// Проаналізувати вибрані вакансії за профілем користувача та стрімити розбір через SSE.
    /// </summary>
    /// <remarks>
    /// Послідовність подій: <c>relevance</c> (детерміновані оцінки 0..100, обчислені без LLM),
    /// далі багато <c>delta</c> і завершальна <c>done</c> (<c>{}</c>).
    /// Помилки до початку стрімінгу повертаються звичайним JSON: 400 (не 1..3 вакансії), 401, 403, 404
    /// (хоч однієї вакансії немає в каталозі), 429 (спільний ліміт із <c>/messages</c>).
    /// <para>
    /// Свідоме відхилення від контракту #5: 503 <c>LLM_UNAVAILABLE</c> тут НЕ повертається перед
    /// стрімінгом. Оцінка релевантності не потребує LLM, тому сервер спершу віддає корисну подію
    /// <c>relevance</c>, а відмову провайдера повідомляє термінальною подією <c>error</c> з кодом
    /// <c>LLM_UNAVAILABLE</c> (REL-003: відмова ШІ не забирає вже обчислений результат).
    /// </para>
    /// </remarks>
    /// <param name="request">Від 1 до 3 ідентифікаторів вакансій каталогу.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Потік подій SSE (<c>relevance</c>, <c>delta</c>, <c>done</c> або <c>error</c>).</response>
    /// <response code="400">Не передано жодної вакансії або передано більше трьох.</response>
    /// <response code="403">Користувач не є шукачем роботи.</response>
    /// <response code="404">Хоч однієї з указаних вакансій немає в каталозі.</response>
    /// <response code="429">Перевищено ліміт запитів до ШІ.</response>
    [HttpPost("analyze")]
    [EnableRateLimiting(AiConsultantRateLimitingExtensions.PolicyName)]
    [SwaggerOperation(
        Summary = "Проаналізувати вибрані вакансії (SSE)",
        Description = "Обчислює детерміновану релевантність вакансій профілю збережених вакансій користувача "
            + "(подія relevance), а потім стрімить розбір ШІ: обов'язкові й бажані вимоги, червоні прапорці "
            + "та пояснення відповідності з посиланням на score. Якщо LLM недоступна — relevance лишається, "
            + "а потік завершується подією error з кодом LLM_UNAVAILABLE.",
        OperationId = "AnalyzeVacancies",
        Tags = ["AiConsultant"])]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/event-stream")]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Analyze([FromBody] AIAnalysisRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId)
        {
            return ErrorUnauthorized("Потрібна автентифікація для аналізу вакансій.");
        }

        AIAnalysisTurn turn;
        try
        {
            // Релевантність і промпт готуються без LLM, тому відмова провайдера не заважає
            // віддати клієнту оцінки: вона приходить уже всередині SSE (див. remarks)
            turn = await _analysisService.PrepareAsync(userId, request.VacancyIds, ct);
        }
        catch (VacancyNotFoundException exception)
        {
            return ErrorNotFound($"Вакансію з ID '{exception.VacancyId}' не знайдено в каталозі.");
        }
        catch (ArgumentException exception)
        {
            return ErrorBadRequest(exception.Message);
        }

        SseEventWriter.PrepareResponse(Response);

        try
        {
            await SseEventWriter.WriteAsync(
                Response,
                AiConsultantEventNames.Relevance,
                new RelevanceEventPayload
                {
                    Items = turn.Relevance
                        .Select(item => new RelevanceItemPayload
                        {
                            VacancyId = item.VacancyId,
                            Score = item.Score,
                            Matched = item.Matched,
                            BasisCount = item.BasisCount
                        })
                        .ToList()
                },
                ct);

            await StreamAnalysisAsync(turn, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Клієнт закрив SSE-з'єднання аналізу вакансій");
        }
        catch (IOException exception)
        {
            _logger.LogInformation(exception, "SSE-з'єднання аналізу вакансій обірвано");
        }

        return new EmptyResult();
    }

    /// <summary>
    /// Видалити діалог з ШІ-консультантом («очистити чат»).
    /// </summary>
    /// <param name="id">Ідентифікатор діалогу.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="204">Діалог видалено.</response>
    /// <response code="404">Діалог не знайдено або він належить іншому користувачу.</response>
    [HttpDelete("sessions/{id:guid}")]
    [SwaggerOperation(
        Summary = "Видалити діалог з ШІ-консультантом",
        Description = "Видаляє діалог разом з історією повідомлень. Чужий діалог недоступний (404).",
        OperationId = "DeleteAiConsultantSession",
        Tags = ["AiConsultant"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSession([FromRoute] Guid id, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId)
        {
            return ErrorUnauthorized("Потрібна автентифікація для роботи з ШІ-консультантом.");
        }

        bool deleted = await _consultantService.DeleteSessionAsync(id, userId, ct);
        if (!deleted)
        {
            return ErrorNotFound($"Діалог з ШІ-консультантом з ID '{id}' не знайдено.");
        }

        return NoContent();
    }

    /// <summary>
    /// Стрімити відповідь LLM, перетворюючи елементи сервісу на SSE-події.
    /// </summary>
    private async Task StreamAnswerAsync(AIConsultantTurn turn, CancellationToken ct)
    {
        bool doneSent = false;
        try
        {
            await foreach (AIConsultantStreamItem item in _consultantService.StreamAnswerAsync(turn, ct))
            {
                switch (item)
                {
                    case AIConsultantDelta delta:
                        await SseEventWriter.WriteAsync(
                            Response,
                            AiConsultantEventNames.Delta,
                            new DeltaEventPayload { Text = delta.Text },
                            ct);
                        break;

                    case AIConsultantCompleted completed:
                        await SseEventWriter.WriteAsync(
                            Response,
                            AiConsultantEventNames.Done,
                            new DoneEventPayload { MessageId = completed.MessageId },
                            ct);
                        doneSent = true;
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Невідомий елемент потоку ШІ-консультанта: {item.GetType().Name}.");
                }
            }
        }
        catch (LlmUnavailableException exception)
        {
            // Вакансії вже показані й лишаються валідними; клієнт бачить термінальну помилку (REL-003)
            _logger.LogWarning("Стрімінг відповіді ШІ обірвано ({FailureKind})", exception.Kind);
            await TryWriteErrorAsync(
                AiConsultantErrorCodes.LlmUnavailable,
                "ШІ-консультант тимчасово недоступний. Показані вакансії залишаються актуальними — "
                    + "можете скористатися звичайним пошуком за фільтрами.",
                ct);
            return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not IOException)
        {
            _logger.LogError(exception, "Помилка стрімінгу відповіді ШІ (діалог {SessionId})", turn.SessionId);
            await TryWriteErrorAsync(
                AiConsultantErrorCodes.Internal,
                "Не вдалося сформувати відповідь ШІ. Спробуйте ще раз або скористайтеся звичайним пошуком.",
                ct);
            return;
        }

        if (!doneSent)
        {
            // Потік завершився без підтвердження збереження — клієнт не має лишитися без термінальної події
            _logger.LogError("Потік відповіді ШІ завершився без події done (діалог {SessionId})", turn.SessionId);
            await TryWriteErrorAsync(
                AiConsultantErrorCodes.Internal,
                "Відповідь ШІ завершилася некоректно. Спробуйте ще раз.",
                ct);
        }
    }

    /// <summary>
    /// Стрімити розбір LLM для <c>/analyze</c>, перетворюючи елементи сервісу на події
    /// <c>delta</c> і <c>done</c>. Подія <c>relevance</c> уже надіслана, тож відмова провайдера
    /// не забирає вже обчислений результат (REL-003).
    /// </summary>
    private async Task StreamAnalysisAsync(AIAnalysisTurn turn, CancellationToken ct)
    {
        bool doneSent = false;
        try
        {
            await foreach (AIAnalysisStreamItem item in _analysisService.StreamAnalysisAsync(turn, ct))
            {
                switch (item)
                {
                    case AIAnalysisDelta delta:
                        await SseEventWriter.WriteAsync(
                            Response,
                            AiConsultantEventNames.Delta,
                            new DeltaEventPayload { Text = delta.Text },
                            ct);
                        break;

                    case AIAnalysisCompleted:
                        await SseEventWriter.WriteAsync(
                            Response,
                            AiConsultantEventNames.Done,
                            new AnalysisDoneEventPayload(),
                            ct);
                        doneSent = true;
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Невідомий елемент потоку аналізу вакансій: {item.GetType().Name}.");
                }
            }
        }
        catch (LlmUnavailableException exception)
        {
            _logger.LogWarning(
                "Аналіз вакансій ({VacancyCount}) обірвано: LLM недоступна ({FailureKind})",
                turn.Vacancies.Count,
                exception.Kind);
            await TryWriteErrorAsync(
                AiConsultantErrorCodes.LlmUnavailable,
                "ШІ-аналіз тимчасово недоступний. Оцінки релевантності вже показані й залишаються актуальними — "
                    + "можете скористатися звичайним пошуком за фільтрами каталогу.",
                ct);
            return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not IOException)
        {
            _logger.LogError(exception, "Помилка стрімінгу аналізу вакансій");
            await TryWriteErrorAsync(
                AiConsultantErrorCodes.Internal,
                "Не вдалося сформувати аналіз вакансій. Спробуйте ще раз або скористайтеся звичайним пошуком.",
                ct);
            return;
        }

        if (!doneSent)
        {
            // Клієнт не має лишитися без термінальної події
            _logger.LogError("Потік аналізу вакансій завершився без події done");
            await TryWriteErrorAsync(
                AiConsultantErrorCodes.Internal,
                "Аналіз вакансій завершився некоректно. Спробуйте ще раз.",
                ct);
        }
    }

    /// <summary>
    /// Надіслати термінальну подію <c>error</c>, не заважаючи основній обробці помилок,
    /// якщо клієнт уже від'єднався.
    /// </summary>
    private async Task TryWriteErrorAsync(string code, string message, CancellationToken ct)
    {
        try
        {
            await SseEventWriter.WriteAsync(
                Response,
                AiConsultantEventNames.Error,
                new StreamErrorEventPayload { Code = code, Message = message },
                ct);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            _logger.LogInformation(exception, "Не вдалося надіслати подію error: клієнт уже від'єднався");
        }
    }
}
