// AI: контракт нормалізації та збереження сирих вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

namespace Waterfall.Services.Interfaces;

/// <summary>
/// Job Ingestion &amp; Normalization Service: приводить сирі вакансії до єдиного формату й зберігає їх у БД.
/// </summary>
public interface IVacancyIngestionService
{
    Task<IngestionResult> ProcessRawVacanciesAsync(IReadOnlyList<RawVacancy> rawItems, CancellationToken ct = default);
}

/// <summary>
/// Вакансія так, як її віддав парсер: усі значення — сирі рядки з сайту
/// (наприклад, зарплата «$3500-4500», дата «2 дні тому», опис у HTML).
/// </summary>
public sealed record RawVacancy
{
    public string? Id { get; init; }
    public string? Url { get; init; }
    public string? Title { get; init; }
    public string? Company { get; init; }
    public string? Salary { get; init; }
    public string? Location { get; init; }
    public string? FullAddress { get; init; }
    public string? Posted { get; init; }
    public string? Experience { get; init; }
    public string? Employment { get; init; }
    public string? Category { get; init; }
    public string? DescriptionHtml { get; init; }
    public string? Tags { get; init; }
}

/// <summary>Скільки вакансій додано, оновлено й відкинуто за один виклик.</summary>
public sealed record IngestionResult(int Created, int Updated, int Skipped);
