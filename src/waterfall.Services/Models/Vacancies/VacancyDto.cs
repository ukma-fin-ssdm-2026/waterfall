// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// DTO представлення вакансії для клієнтів API (Web API, .NET MAUI).
/// </summary>
public record VacancyDto
{
    /// <summary>
    /// Унікальний ідентифікатор вакансії (UUID).
    /// </summary>
    /// <example>11111111-1111-1111-1111-111111111111</example>
    public Guid Id { get; init; }

    /// <summary>
    /// Назва посади / позиції.
    /// </summary>
    /// <example>Senior C# .NET Backend Developer</example>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Назва компанії-роботодавця.
    /// </summary>
    /// <example>Genesis Tech</example>
    public string Company { get; init; } = string.Empty;

    /// <summary>
    /// Детальний опис вакансії, обов'язків та вимог.
    /// </summary>
    /// <example>Розробка high-load мікросервісів на .NET 10, Kafka, Redis та PostgreSQL.</example>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Професійна категорія / напрям (.NET, QA, Frontend, DevOps).
    /// </summary>
    /// <example>.NET</example>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Місто та формат роботи (наприклад, Віддалено, Київ).
    /// </summary>
    /// <example>Київ (Віддалено)</example>
    public string Location { get; init; } = string.Empty;

    /// <summary>
    /// Повна адреса офісу роботодавця (за наявності).
    /// </summary>
    /// <example>Київ, вул. Мечникова, 2, БЦ Парус</example>
    public string? FullAddress { get; init; }

    /// <summary>
    /// Графік та тип зайнятості.
    /// </summary>
    /// <example>Повна зайнятість · Гнучкий графік</example>
    public string? Schedule { get; init; }

    /// <summary>
    /// Вимоги до досвіду роботи.
    /// </summary>
    /// <example>5+ років</example>
    public string? Experience { get; init; }

    /// <summary>
    /// Мінімальна заробітна плата у валюті currency.
    /// </summary>
    /// <example>4000</example>
    public decimal? SalaryMin { get; init; }

    /// <summary>
    /// Максимальна заробітна плата у валюті currency.
    /// </summary>
    /// <example>5500</example>
    public decimal? SalaryMax { get; init; }

    /// <summary>
    /// Валюта виплати заробітної плати (USD, EUR, UAH).
    /// </summary>
    /// <example>USD</example>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// Джерело, з якого зібрано вакансію (Djinni, WorkUa, RobotaUa, Dou).
    /// </summary>
    /// <example>Djinni</example>
    public JobSource Source { get; init; } = JobSource.Unknown;

    /// <summary>
    /// Оригінальне посилання на вакансію на сайті джерела.
    /// </summary>
    /// <example>https://djinni.co/jobs/example-senior-dotnet/</example>
    public string SourceUrl { get; init; } = string.Empty;

    /// <summary>
    /// Список ключових навичок та технологій (тегів).
    /// </summary>
    /// <example>[".NET 10", "C#", "Kafka", "PostgreSQL"]</example>
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Дата та час публікації вакансії.
    /// </summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>
    /// Дата та час першого виявлення вакансії нашою платформою.
    /// </summary>
    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>
    /// Дата та час останнього підтвердження актуальності вакансії парсером.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; init; }

    /// <summary>
    /// Ознака активності (true — активна, false — закрита/архівна).
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Ознака збереження вакансії поточним користувачем у закладки.
    /// Обчислюється динамічно за таблицею збережених вакансій.
    /// </summary>
    public bool IsSaved { get; init; }
}
