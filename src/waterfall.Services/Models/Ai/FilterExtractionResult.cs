// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json.Serialization;

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Структурована відповідь LLM на етапі витягу фільтра (structured output).
/// Імена полі в JSON — snake_case, щоб модель бачила ті самі ключі, що й у <see cref="AppliedFilter"/>.
/// Кожне поле означає «нове значення»; сервіс зливає його з попереднім фільтром діалогу (US-005, критерій 2).
/// </summary>
public sealed class FilterExtractionResult
{
    /// <summary>
    /// Новий пошуковий рядок.
    /// </summary>
    [JsonPropertyName("search")]
    public string? Search { get; set; }

    /// <summary>
    /// Категорія з закритого переліку <c>CategoryGroups.All</c>.
    /// </summary>
    [JsonPropertyName("category_group")]
    public string? CategoryGroup { get; set; }

    /// <summary>
    /// Формати роботи з переліку <c>remote</c>, <c>hybrid</c>, <c>office</c>.
    /// </summary>
    [JsonPropertyName("work_format")]
    public string[]? WorkFormat { get; set; }

    /// <summary>
    /// Максимально прийнятний мінімальний досвід вакансії в місяцях.
    /// </summary>
    [JsonPropertyName("max_experience_months")]
    public int? MaxExperienceMonths { get; set; }

    /// <summary>
    /// Нижня межа зарплати (USD на місяць).
    /// </summary>
    [JsonPropertyName("min_salary")]
    public decimal? MinSalary { get; set; }

    /// <summary>
    /// Верхня межа зарплати (USD на місяць).
    /// </summary>
    [JsonPropertyName("max_salary")]
    public decimal? MaxSalary { get; set; }

    /// <summary>
    /// Локація.
    /// </summary>
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>
    /// Навички, кожна з яких має бути у вакансії.
    /// </summary>
    [JsonPropertyName("skills")]
    public string[]? Skills { get; set; }

    /// <summary>
    /// Ключі фільтра, які користувач просить прибрати (наприклад, «зарплата не важлива»).
    /// </summary>
    [JsonPropertyName("clear")]
    public string[]? Clear { get; set; }
}
