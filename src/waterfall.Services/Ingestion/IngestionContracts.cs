// AI: контракти фонового збору вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

namespace Waterfall.Services.Ingestion;

/// <summary>
/// Джерело сирих документів з вакансіями. Зараз це JSON-дампи у файлах (<see cref="FileRawVacancySource"/>);
/// HTTP-парсер сайту стане ще однією реалізацією без змін у нормалізації та збереженні.
/// </summary>
public interface IRawVacancySource
{
    Task<IReadOnlyList<RawVacancyDocument>> ReadDocumentsAsync(CancellationToken ct = default);
}

/// <summary>
/// Розбирає документ одного формату на сирі записи.
/// </summary>
public interface IRawVacancyParser
{
    /// <summary>Назва джерела, формат якого розбирає парсер.</summary>
    string SourceName { get; }

    RawParseResult Parse(string content);
}

/// <summary>
/// Один повний цикл збору: прочитати документи, розібрати, нормалізувати й зберегти в БД.
/// </summary>
public interface IVacancyIngestionService
{
    Task<IngestionReport> RunOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// Підсумок одного циклу збору для журналу й тестів.
/// </summary>
public sealed class IngestionReport
{
    /// <summary>Документи, які вдалося розібрати.</summary>
    public int Documents { get; set; }

    /// <summary>Документи, які не вдалося прочитати чи розібрати.</summary>
    public int FailedDocuments { get; set; }

    /// <summary>Усі записи в розібраних документах.</summary>
    public int Items { get; set; }

    public int Created { get; set; }
    public int Updated { get; set; }

    /// <summary>Записи, відкинуті нормалізацією як невалідні.</summary>
    public int Skipped { get; set; }

    /// <summary>Валідні записи, які не вдалося зберегти через помилку БД.</summary>
    public int Failed { get; set; }

    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Налаштування фонового збору (секція Ingestion в appsettings.json).
/// </summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public bool Enabled { get; set; }

    /// <summary>Каталог з JSON-дампами; відносний шлях рахується від кореня вмісту застосунку.</summary>
    public string RawDataDirectory { get; set; } = "../../data/raw";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Затримка перед першим циклом, щоб не навантажувати БД під час старту застосунку.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Файли більшого розміру не читаються: захист від помилкового чи зловмисного дампу.</summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}
