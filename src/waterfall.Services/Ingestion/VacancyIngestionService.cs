// AI: один цикл фонового збору вакансій (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;

namespace Waterfall.Services.Ingestion;

/// <summary>
/// Один цикл збору: читає документи з джерела, розбирає їх, нормалізує записи й зберігає в БД
/// (нову вакансію створює, наявну — оновлює за парою «джерело + зовнішній id»).
/// Помилка в документі чи записі не зупиняє обробку решти.
/// </summary>
public sealed class VacancyIngestionService : IVacancyIngestionService
{
    private readonly IRawVacancySource _source;
    private readonly IRawVacancyParser _parser;
    private readonly VacancyNormalizer _normalizer;
    private readonly IVacancyRepository _repository;
    private readonly IVacancyCacheService _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VacancyIngestionService> _logger;

    public VacancyIngestionService(
        IRawVacancySource source,
        IRawVacancyParser parser,
        VacancyNormalizer normalizer,
        IVacancyRepository repository,
        IVacancyCacheService cache,
        TimeProvider timeProvider,
        ILogger<VacancyIngestionService> logger)
    {
        _source = source;
        _parser = parser;
        _normalizer = normalizer;
        _repository = repository;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IngestionReport> RunOnceAsync(CancellationToken ct = default)
    {
        long startedAt = _timeProvider.GetTimestamp();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        IngestionReport report = new();
        List<Guid> updatedIds = [];

        IReadOnlyList<RawVacancyDocument> documents = await _source.ReadDocumentsAsync(ct);
        foreach (RawVacancyDocument document in documents)
        {
            if (!document.IsReadable)
            {
                _logger.LogWarning("Документ {Document} не прочитано: {Reason}", document.Name, document.Error);
                report.FailedDocuments++;
                continue;
            }

            RawParseResult parsed = _parser.Parse(document.Content!);
            if (!parsed.IsValid)
            {
                _logger.LogWarning("Документ {Document} пропущено: {Reason}", document.Name, parsed.Error);
                report.FailedDocuments++;
                continue;
            }

            report.Documents++;
            report.Items += parsed.Items.Count + parsed.ItemErrors.Count;

            foreach (string itemError in parsed.ItemErrors)
            {
                _logger.LogWarning("Документ {Document}: {Reason}", document.Name, itemError);
                report.Skipped++;
            }

            foreach (RawVacancy raw in parsed.Items)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessItemAsync(document.Name, raw, now, report, updatedIds, ct);
            }
        }

        await InvalidateCacheAsync(report, updatedIds, ct);

        report.Duration = _timeProvider.GetElapsedTime(startedAt);
        return report;
    }

    private async Task ProcessItemAsync(
        string documentName,
        RawVacancy raw,
        DateTimeOffset now,
        IngestionReport report,
        List<Guid> updatedIds,
        CancellationToken ct)
    {
        NormalizationResult normalized;
        try
        {
            normalized = _normalizer.Normalize(raw, now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Нормалізатор не повинен падати, але непередбачений вхід (наприклад, тайм-аут регулярного виразу)
            // має відкинути лише цей запис
            _logger.LogWarning(ex, "Запис {ExternalId} з {Document} не вдалося нормалізувати.", raw.ExternalId, documentName);
            report.Skipped++;
            return;
        }

        if (!normalized.IsValid)
        {
            _logger.LogWarning("Запис {ExternalId} з {Document} пропущено: {Reason}", raw.ExternalId, documentName, normalized.Error);
            report.Skipped++;
            return;
        }

        NormalizedVacancy vacancy = normalized.Vacancy;
        try
        {
            VacancyDB? existing = await _repository.GetBySourceAndExternalIdAsync(vacancy.Source.ToString(), vacancy.ExternalId, ct);
            if (existing is null)
            {
                await _repository.CreateAsync(VacancyDbMapper.ToNewEntity(vacancy, now), ct);
                report.Created++;
            }
            else
            {
                VacancyDbMapper.ApplyUpdate(existing, vacancy, now);
                await _repository.UpdateAsync(existing, ct);
                updatedIds.Add(existing.Id);
                report.Updated++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Не вдалося зберегти вакансію {Source}/{ExternalId}.", vacancy.Source, vacancy.ExternalId);
            report.Failed++;
        }
    }

    private async Task InvalidateCacheAsync(IngestionReport report, List<Guid> updatedIds, CancellationToken ct)
    {
        if (report.Created + report.Updated == 0)
        {
            return;
        }

        // Без скидання кешу нові вакансії з'явилися б у каталозі лише після закінчення TTL
        try
        {
            await _cache.InvalidateCatalogAsync(ct);
            foreach (Guid id in updatedIds)
            {
                await _cache.InvalidateVacancyAsync(id, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Не вдалося скинути кеш каталогу після збору; дані оновляться після закінчення TTL.");
        }
    }
}
