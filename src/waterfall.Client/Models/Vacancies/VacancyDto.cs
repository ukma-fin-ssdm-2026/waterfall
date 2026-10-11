// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Vacancies;

/// <summary>
/// Клієнтська копія контракту VacancyDto з waterfall.Services (відповідь GET /api/v1/vacancies).
/// Копія свідома: посилання на waterfall.Services затягнуло б EF Core та серверний код у застосунок.
/// </summary>
public record VacancyDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string? FullAddress { get; init; }
    public string? Schedule { get; init; }
    public string? Experience { get; init; }
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
}
