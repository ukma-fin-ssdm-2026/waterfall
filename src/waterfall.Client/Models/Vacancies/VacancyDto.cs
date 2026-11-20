// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: позначка «переглянуто» для знайдених на сайтах вакансій (issue #62, на прохання Дениса); перевірено: тести SiteSearchServiceTests, самоперевірка автора (§8)

using Waterfall.Repository.Entities;

namespace Waterfall.Client.Models.Vacancies;

/// <summary>
/// Клієнтська копія контракту VacancyDto з waterfall.Services (відповідь GET /api/v1/vacancies).
/// Копія свідома: посилання на waterfall.Services затягнуло б серверний код бізнес-логіки у застосунок.
/// </summary>
public record VacancyDto
{
    public Guid Id { get; init; }
    public string? ExternalId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? CategoryGroup { get; init; }
    public string Location { get; init; } = string.Empty;
    public string? FullAddress { get; init; }
    public string? Schedule { get; init; }
    public string? Experience { get; init; }
    public int? ExperienceMonths { get; init; }
    public WorkFormat WorkFormat { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// Джерело вакансії; сервер серіалізує enum числом.
    /// </summary>
    public JobSource Source { get; init; } = JobSource.Unknown;

    public string SourceUrl { get; init; } = string.Empty;
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    public DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset FirstSeenAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }

    /// <summary>
    /// true — активна, false — закрита/архівна.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Чи збережена вакансія у «Обране».
    /// </summary>
    public bool IsSaved { get; init; }

    /// <summary>Користувач уже переглядав вакансію; приходить із результатом пошуку на сайтах.</summary>
    public bool IsViewed { get; init; }
}
