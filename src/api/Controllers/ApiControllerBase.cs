// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Waterfall.Api.Models.Common;

namespace Waterfall.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Отримує унікальний ідентифікатор поточного HTTP-запиту для аудиту та пошуку в логах.
    /// Працює за пріоритетом:
    /// 1. X-Correlation-Id — наскрізний ID для трасування всього ланцюжка викликів від клієнта (.NET MAUI) до бекенду.
    /// 2. X-Request-Id — ідентифікатор конкретного поодинокого HTTP-запиту (наприклад, від API Gateway чи NGINX).
    /// 3. TraceIdentifier — внутрішній системний ID, згенерований самим ASP.NET Core (якщо клієнт не надіслав заголовків).
    /// </summary>
    protected string RequestId
    {
        get
        {

            if (HttpContext.Request.Headers.TryGetValue("X-Correlation-Id", out StringValues correlationId)
                && !string.IsNullOrWhiteSpace(correlationId))
                return correlationId!;


            if (HttpContext.Request.Headers.TryGetValue("X-Request-Id", out StringValues reqId)
                && !string.IsNullOrWhiteSpace(reqId))
                return reqId!;


            return HttpContext.TraceIdentifier;
        }
    }



    /// <summary>
    /// Власний хелпер для повернення помилки 404 Not Found.
    /// Використовується, коли запитуваний ресурс (вакансію чи збережену вакансію) не знайдено в базі даних.
    /// Формує відповідь з кодом "NOT_FOUND" та поточним RequestId.
    /// </summary>
    /// <param name="message">Людинозчитуваний опис причини помилки для клієнта.</param>
    protected NotFoundObjectResult ErrorNotFound(string message)
    {
        // Створюємо власну структуру помилки
        ApiErrorResponse error = ApiErrorResponse.Create(
            code: "NOT_FOUND",
            message: message,
            requestId: RequestId);

        // Повертаємо HTTP 404 разом із нашим уніфікованим JSON
        return NotFound(error);
    }

    /// <summary>
    /// Власний хелпер для повернення помилки 400 Bad Request.
    /// Використовується при виявленні невалідних параметрів запиту або порушенні бізнес-правил валідації.
    /// Формує відповідь з кодом "VALIDATION_ERROR", необов'язковим списком деталей Details та RequestId.
    /// </summary>
    /// <param name="message">Опис помилки валідації.</param>
    /// <param name="details">Список конкретних полів, які не пройшли валідацію.</param>
    protected BadRequestObjectResult ErrorBadRequest(string message, List<ValidationErrorDetail>? details = null)
    {
        // Створюємо власну структуру помилки
        ApiErrorResponse error = ApiErrorResponse.Create(
            code: "VALIDATION_ERROR",
            message: message,
            requestId: RequestId,
            details: details);

        // Повертаємо HTTP 400 разом із нашим уніфікованим JSON
        return BadRequest(error);
    }

    /// <summary>
    /// Власний хелпер для повернення помилки 401 Unauthorized.
    /// Використовується при невдалій автентифікації (невідомий email або невірний пароль —
    /// навмисно однакове повідомлення, THR-01).
    /// Формує відповідь з кодом "UNAUTHORIZED" та RequestId.
    /// </summary>
    /// <param name="message">Узагальнений опис причини (без розкриття, який саме фактор невірний).</param>
    protected UnauthorizedObjectResult ErrorUnauthorized(string message)
    {
        // Створюємо власну структуру помилки
        ApiErrorResponse error = ApiErrorResponse.Create(
            code: "UNAUTHORIZED",
            message: message,
            requestId: RequestId);

        // Повертаємо HTTP 401 разом із нашим уніфікованим JSON
        return Unauthorized(error);
    }

    /// <summary>
    /// Власний хелпер для повернення помилки 409 Conflict.
    /// Використовується при конфлікті стану ресурсу (наприклад, спроба повторно зберегти вже збережену вакансію).
    /// Формує відповідь з кодом "CONFLICT" та RequestId.
    /// </summary>
    /// <param name="message">Опис причини конфлікту.</param>
    protected ConflictObjectResult ErrorConflict(string message)
    {
        // Створюємо власну структуру помилки
        ApiErrorResponse error = ApiErrorResponse.Create(
            code: "CONFLICT",
            message: message,
            requestId: RequestId);

        // Повертаємо HTTP 409 разом із нашим уніфікованим JSON
        return Conflict(error);
    }
}
