// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json.Serialization;

namespace Waterfall.Api.Models.Common;
/// <summary>
/// Узгоджена структура помилок API
/// </summary>
public class ApiErrorResponse
{
    [JsonPropertyName("error")]
    public ErrorPayload Error { get; set; } = new();

    public static ApiErrorResponse Create(
        string code,
        string message,
        string requestId,
        string? documentation = null,
        List<ValidationErrorDetail>? details = null)
    {
        return new ApiErrorResponse
        {
            Error = new ErrorPayload
            {
                Code = code,
                Message = message,
                RequestId = requestId,
                Documentation = documentation ?? "https://ssdm-2026.cs-courses.org/reference/error-structure.html",
                Details = details ?? []
            }
        };
    }
}

public class ErrorPayload
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public List<ValidationErrorDetail> Details { get; set; } = [];

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("documentation")]
    public string Documentation { get; set; } = string.Empty;
}

public class ValidationErrorDetail
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("provided_value")]
    public object? ProvidedValue { get; set; }
}
