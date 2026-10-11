// AI: моделі сирих даних для фонового збору вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

namespace Waterfall.Services.Ingestion;

/// <summary>
/// Сирий запис про вакансію в тому вигляді, у якому його віддало джерело.
/// Усі поля — неперевірений текст: зарплата «від $2000», дата «2 дні тому», опис з HTML-тегами.
/// Перевірку й зведення до єдиного формату виконує <see cref="VacancyNormalizer"/>.
/// </summary>
public sealed record RawVacancy
{
    /// <summary>Назва джерела, наприклад «djinni».</summary>
    public required string Source { get; init; }

    /// <summary>Ідентифікатор вакансії на сайті-джерелі.</summary>
    public string? ExternalId { get; init; }

    public string? Url { get; init; }
    public string? Title { get; init; }
    public string? Company { get; init; }
    public string? Salary { get; init; }
    public string? Location { get; init; }
    public string? Posted { get; init; }
    public string? Experience { get; init; }
    public string? Employment { get; init; }
    public string? Category { get; init; }
    public string? DescriptionHtml { get; init; }

    /// <summary>Навички одним рядком через кому, крапку з комою чи «|».</summary>
    public string? Tags { get; init; }
}

/// <summary>
/// Документ із сирими даними (наприклад, JSON-файл дампу) або причина, чому його не вдалося прочитати.
/// </summary>
public sealed record RawVacancyDocument(string Name, string? Content, string? Error = null)
{
    public bool IsReadable => Error is null && Content is not null;

    public static RawVacancyDocument Unreadable(string name, string error) => new(name, null, error);
}

/// <summary>
/// Результат розбору одного документа: або помилка всього документа, або записи
/// разом із помилками окремих записів, які не вдалося прочитати.
/// </summary>
public sealed record RawParseResult
{
    public string? Error { get; private init; }
    public IReadOnlyList<RawVacancy> Items { get; private init; } = [];
    public IReadOnlyList<string> ItemErrors { get; private init; } = [];

    public bool IsValid => Error is null;

    public static RawParseResult Success(IReadOnlyList<RawVacancy> items, IReadOnlyList<string> itemErrors) =>
        new() { Items = items, ItemErrors = itemErrors };

    public static RawParseResult Failure(string error) => new() { Error = error };
}
