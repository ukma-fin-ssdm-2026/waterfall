// AI: файлове джерело JSON-дампів вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Waterfall.Services.Ingestion;

/// <summary>
/// Читає JSON-дампи вакансій з каталогу <see cref="IngestionOptions.RawDataDirectory"/>.
/// Файл, який не вдалося прочитати, повертається як документ з помилкою, щоб збій одного файлу
/// не зупиняв обробку інших (REL-001).
/// </summary>
public sealed class FileRawVacancySource : IRawVacancySource
{
    private readonly IngestionOptions _options;
    private readonly ILogger<FileRawVacancySource> _logger;

    public FileRawVacancySource(IOptions<IngestionOptions> options, ILogger<FileRawVacancySource> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RawVacancyDocument>> ReadDocumentsAsync(CancellationToken ct = default)
    {
        string directory = _options.RawDataDirectory;
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Каталог з сирими даними {Directory} не знайдено; збір пропущено.", directory);
            return [];
        }

        List<RawVacancyDocument> documents = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            string name = Path.GetFileName(path);

            try
            {
                long size = new FileInfo(path).Length;
                if (size > _options.MaxFileSizeBytes)
                {
                    documents.Add(RawVacancyDocument.Unreadable(name, $"файл має {size} байт, дозволено не більше {_options.MaxFileSizeBytes}"));
                    continue;
                }

                string content = await File.ReadAllTextAsync(path, ct);
                documents.Add(new RawVacancyDocument(name, content));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                documents.Add(RawVacancyDocument.Unreadable(name, ex.Message));
            }
        }

        return documents;
    }
}
