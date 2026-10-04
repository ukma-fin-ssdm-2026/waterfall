// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json.Serialization;

namespace Waterfall.Api.Models.Vacancies;
/// <summary>
/// Представлення сутності вакансії Waterfall.
/// </summary>
public class VacancyDto
{
    /// <summary>
    /// Унікальний ідентифікатор вакансії.
    /// </summary>
    /// <example>3fa85f64-5717-4562-b3fc-2c963f66afa6</example>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>
    /// Назва вакансії / посади.
    /// </summary>
    /// <example>.NET Core Backend Developer</example>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Назва компанії або роботодавця.
    /// </summary>
    /// <example>GlobalTech Innovations</example>
    [JsonPropertyName("company")]
    public string Company { get; init; } = string.Empty;

    /// <summary>
    /// Детальний опис вакансії та обов'язків.
    /// </summary>
    /// <example>Шукаємо досвідченого C# розробника для створення високонавантажених REST API на базі .NET 10.</example>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Категорія чи напрямок (наприклад, .NET, QA, DevOps, Mobile).
    /// </summary>
    /// <example>.NET</example>
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Локація або формат роботи (Київ, Віддалено, Гібрид).
    /// </summary>
    /// <example>Київ (Віддалено)</example>
    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;

    /// <summary>
    /// Повна фізична адреса офісу або локації
    /// </summary>
    /// <example>Київ, проспект Академіка Глушкова, 1</example>
    [JsonPropertyName("full_address")]
    public string? FullAddress { get; init; }

    /// <summary>
    /// Графік та тип зайнятості
    /// </summary>
    /// <example>Повна зайнятість · Стандартний графік: 5/2</example>
    [JsonPropertyName("schedule")]
    public string? Schedule { get; init; }

    /// <summary>
    /// Необхідний досвід роботи (наприклад: "1-3 роки", "3+ роки", "5+ років", "Без досвіду").
    /// </summary>
    /// <example>3+ роки</example>
    [JsonPropertyName("experience")]
    public string? Experience { get; init; }

    /// <summary>
    /// Мінімальна заробітна плата у валюті currency (якщо вказана).
    /// </summary>
    /// <example>2500</example>
    [JsonPropertyName("salary_min")]
    public decimal? SalaryMin { get; init; }

    /// <summary>
    /// Максимальна заробітна плата у валюті currency (якщо вказана).
    /// </summary>
    /// <example>4000</example>
    [JsonPropertyName("salary_max")]
    public decimal? SalaryMax { get; init; }

    /// <summary>
    /// Валюта виплати заробітної плати.
    /// </summary>
    /// <example>USD</example>
    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// Джерело агрегації даних про вакансію.
    /// </summary>
    /// <example>Djinni</example>
    [JsonPropertyName("source")]
    public JobSource Source { get; init; }

    /// <summary>
    /// Пряме посилання на оригінал вакансії на ресурсі-джерелі.
    /// </summary>
    /// <example>https://djinni.co/jobs/12345-dotnet-developer/</example>
    [JsonPropertyName("source_url")]
    public string SourceUrl { get; init; } = string.Empty;

    /// <summary>
    /// Список ключових технологій та навичок (тегів).
    /// </summary>
    /// <example>["C#", ".NET 10", "PostgreSQL", "Docker", "REST API"]</example>
    [JsonPropertyName("skills")]
    public List<string> Skills { get; init; } = [];

    /// <summary>
    /// Дата первинної публікації на джерелі.
    /// </summary>
    /// <example>2026-09-28T10:00:00Z</example>
    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>
    /// Чи активна вакансія на даний момент.
    /// </summary>
    /// <example>true</example>
    [JsonPropertyName("is_active")]
    public bool IsActive { get; init; } = true;
}
