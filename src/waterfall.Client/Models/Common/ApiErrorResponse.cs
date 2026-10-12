// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Waterfall.Client.Models.Common;

/// <summary>
/// Уніфікований конверт помилки API: { "error": { code, message, details, request_id, documentation } }.
/// </summary>
public record ApiErrorResponse
{
    [JsonPropertyName("error")]
    public ApiError? Error { get; init; }
}

/// <summary>
/// Тіло помилки API.
/// </summary>
public record ApiError
{
    /// <summary>
    /// Машинний код: VALIDATION_ERROR, NOT_FOUND, CONFLICT, UNAUTHORIZED…
    /// </summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Людинозчитуване повідомлення, яке можна показати користувачу.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("details")]
    public IReadOnlyList<ApiValidationError> Details { get; init; } = Array.Empty<ApiValidationError>();

    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("documentation")]
    public string Documentation { get; init; } = string.Empty;
}

/// <summary>
/// Помилка валідації конкретного поля.
/// </summary>
public record ApiValidationError
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("provided_value")]
    public JsonElement? ProvidedValue { get; init; }
}
