// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Waterfall.Api.Services;

/// <summary>
/// Записувач Server-Sent Events (SSE) у форматі контракту:
/// <c>event: &lt;name&gt;\ndata: &lt;json&gt;\n\n</c>, з негайним flush після кожного кадру (PERF-002).
/// Хелпер спільний для ШІ-консультанта (#4) та аналізу вакансій (#5) — обидва стріми
/// мають однакову рамку, тож клієнт розбирає їх одним парсером.
/// </summary>
public static class SseEventWriter
{
    /// <summary>
    /// Опції серіалізації: camelCase для DTO без явних імен полів (як у решті API)
    /// і явні snake_case-імена там, де вони задані атрибутами.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        // Ті самі правила, що й для REST-ендпоінтів (Program.cs): camelCase та перелічення назвами,
        // інакше клієнт розбирав би `source` у SSE як число, а в каталозі — як "Djinni"
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>
    /// Підготувати відповідь до SSE: тип вмісту, заборона кешування та вимкнення буферизації проксі.
    /// </summary>
    /// <param name="response">HTTP-відповідь.</param>
    public static void PrepareResponse(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
    }

    /// <summary>
    /// Надіслати одну подію та негайно віддати її клієнту.
    /// </summary>
    /// <typeparam name="T">Тип даних події.</typeparam>
    /// <param name="response">HTTP-відповідь.</param>
    /// <param name="eventName">Ім'я події (<see cref="Waterfall.Api.Models.Ai.AiConsultantEventNames"/>).</param>
    /// <param name="data">Дані події; серіалізуються в один рядок JSON.</param>
    /// <param name="ct">Токен скасування (обрив з'єднання клієнтом).</param>
    public static async Task WriteAsync<T>(HttpResponse response, string eventName, T data, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(data, SerializerOptions);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
