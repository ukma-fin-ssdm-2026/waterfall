// AI: модель нормалізованої вакансії для фонового збору (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using System.Diagnostics.CodeAnalysis;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Ingestion;

/// <summary>
/// Вакансія після нормалізації: поля перевірені, обрізані до меж схеми БД і зведені до єдиного формату
/// (зарплата — числа й ISO-код валюти, дата — UTC, навички — список без дублікатів).
/// </summary>
public sealed record NormalizedVacancy
{
    public required JobSource Source { get; init; }
    public required string ExternalId { get; init; }
    public required string SourceUrl { get; init; }
    public required string Title { get; init; }
    public required string Company { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string Location { get; init; }
    public string? Schedule { get; init; }
    public string? Experience { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public required string Currency { get; init; }
    public IReadOnlyList<string> Skills { get; init; } = [];
    public required DateTimeOffset PublishedAt { get; init; }
}

/// <summary>
/// Результат нормалізації одного сирого запису: вакансія або причина, чому запис пропущено.
/// </summary>
public sealed record NormalizationResult
{
    public NormalizedVacancy? Vacancy { get; private init; }
    public string? Error { get; private init; }

    [MemberNotNullWhen(true, nameof(Vacancy))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsValid => Vacancy is not null;

    public static NormalizationResult Valid(NormalizedVacancy vacancy) => new() { Vacancy = vacancy };

    public static NormalizationResult Invalid(string error) => new() { Error = error };
}
