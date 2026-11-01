// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Структурований фільтр діалогу з ШІ-консультантом (FR-009, US-005).
/// Це єдиний формат, який зберігається в <c>ChatSession.context_filter_json</c> і надсилається
/// клієнту в SSE-події <c>meta</c>, тому імена полів — snake_case, як у контракті API.
/// </summary>
public sealed class AppliedFilter
{
    /// <summary>
    /// Вільний пошуковий рядок (підрядок у назві, компанії, описі).
    /// </summary>
    [JsonPropertyName("search")]
    public string? Search { get; set; }

    /// <summary>
    /// Канонічна широка категорія з <c>CategoryGroups.All</c>.
    /// </summary>
    [JsonPropertyName("category_group")]
    public string? CategoryGroup { get; set; }

    /// <summary>
    /// Формати роботи: будь-який із переліку <c>remote</c>, <c>hybrid</c>, <c>office</c>.
    /// </summary>
    [JsonPropertyName("work_format")]
    public IReadOnlyList<string>? WorkFormat { get; set; }

    /// <summary>
    /// Максимально прийнятний мінімальний досвід вакансії в місяцях.
    /// </summary>
    [JsonPropertyName("max_experience_months")]
    public int? MaxExperienceMonths { get; set; }

    /// <summary>
    /// Нижня межа зарплати в USD.
    /// </summary>
    [JsonPropertyName("min_salary")]
    public decimal? MinSalary { get; set; }

    /// <summary>
    /// Верхня межа зарплати в USD.
    /// </summary>
    [JsonPropertyName("max_salary")]
    public decimal? MaxSalary { get; set; }

    /// <summary>
    /// Локація (підрядок, без урахування регістру).
    /// </summary>
    [JsonPropertyName("location")]
    public string? Location { get; set; }

    /// <summary>
    /// Навички, кожна з яких має траплятися у вакансії (AND).
    /// </summary>
    [JsonPropertyName("skills")]
    public IReadOnlyList<string>? Skills { get; set; }

    /// <summary>
    /// Порожній фільтр (новий діалог без жодного критерію).
    /// </summary>
    public static AppliedFilter Empty => new();

    /// <summary>
    /// Ознака відсутності жодного критерію.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Search)
        && string.IsNullOrWhiteSpace(CategoryGroup)
        && (WorkFormat is null || WorkFormat.Count == 0)
        && MaxExperienceMonths is null
        && MinSalary is null
        && MaxSalary is null
        && string.IsNullOrWhiteSpace(Location)
        && (Skills is null || Skills.Count == 0);

    /// <summary>
    /// Створити незалежну копію фільтра.
    /// </summary>
    public AppliedFilter Clone() => new()
    {
        Search = Search,
        CategoryGroup = CategoryGroup,
        WorkFormat = WorkFormat?.ToArray(),
        MaxExperienceMonths = MaxExperienceMonths,
        MinSalary = MinSalary,
        MaxSalary = MaxSalary,
        Location = Location,
        Skills = Skills?.ToArray()
    };

    /// <summary>
    /// Серіалізувати фільтр у JSON для <c>context_filter_json</c> та SSE-події <c>meta</c>.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, AppliedFilterJson.Options);

    /// <summary>
    /// Відновити фільтр із JSON. Пошкоджений або порожній JSON дає порожній фільтр,
    /// щоб старий запис у БД не ламав новий діалог.
    /// </summary>
    public static AppliedFilter FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<AppliedFilter>(json, AppliedFilterJson.Options) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}

/// <summary>
/// Налаштування серіалізації фільтра: спільні для БД і SSE, щоб формати не розходилися.
/// </summary>
public static class AppliedFilterJson
{
    /// <summary>
    /// Опції серіалізації без camelCase-конвертації: імена полів задані атрибутами.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
