using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Чанкований пошук кандидатів і O(1) словниковий індекс для пакетної синхронізації та upsert вакансій.
/// Замінює повне завантаження таблиці Vacancies та O(N*M) лінійний пошук.
/// </summary>
public static class VacancyCandidateLookup
{
    public const int CandidateChunkSize = 200;

    public static async Task<List<TVacancy>> LoadByExternalKeysAsync<TVacancy>(
        IQueryable<TVacancy> queryable,
        IEnumerable<(string Source, string ExternalId)> externalKeys,
        CancellationToken ct)
        where TVacancy : VacancyDB
    {
        Dictionary<Guid, TVacancy> loaded = [];
        await LoadExternalKeyMatchesAsync(queryable, externalKeys, loaded, ct).ConfigureAwait(false);
        return loaded.Values.ToList();
    }

    public static async Task<List<TVacancy>> LoadAsync<TVacancy>(
        IQueryable<TVacancy> queryable,
        IEnumerable<Guid> ids,
        IEnumerable<(string Source, string ExternalId)> externalKeys,
        IEnumerable<string> sourceUrls,
        CancellationToken ct)
        where TVacancy : VacancyDB
    {
        Dictionary<Guid, TVacancy> loaded = [];

        await LoadIdMatchesAsync(queryable, ids, loaded, ct).ConfigureAwait(false);
        await LoadExternalKeyMatchesAsync(queryable, externalKeys, loaded, ct).ConfigureAwait(false);
        await LoadUrlMatchesAsync(queryable, sourceUrls, loaded, ct).ConfigureAwait(false);

        return loaded.Values.ToList();
    }

    private static async Task LoadIdMatchesAsync<TVacancy>(
        IQueryable<TVacancy> queryable,
        IEnumerable<Guid> ids,
        Dictionary<Guid, TVacancy> loaded,
        CancellationToken ct)
        where TVacancy : VacancyDB
    {
        List<Guid> distinctIds = ids
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        foreach (Guid[] chunk in distinctIds.Chunk(CandidateChunkSize))
        {
            List<TVacancy> rows = await queryable
                .Where(v => chunk.Contains(v.Id))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            AddToMap(loaded, rows);
        }
    }

    private static async Task LoadExternalKeyMatchesAsync<TVacancy>(
        IQueryable<TVacancy> queryable,
        IEnumerable<(string Source, string ExternalId)> externalKeys,
        Dictionary<Guid, TVacancy> loaded,
        CancellationToken ct)
        where TVacancy : VacancyDB
    {
        IEnumerable<IGrouping<string, string>> bySource = externalKeys
            .Where(k => !string.IsNullOrWhiteSpace(k.Source) && !string.IsNullOrWhiteSpace(k.ExternalId))
            .GroupBy(k => k.Source, k => k.ExternalId, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, string> group in bySource)
        {
            string source = group.Key;
            List<string> distinctExternalIds = group.Distinct(StringComparer.Ordinal).ToList();

            foreach (string[] chunk in distinctExternalIds.Chunk(CandidateChunkSize))
            {
                List<TVacancy> rows = await queryable
                    .Where(v => v.Source == source && v.ExternalId != null && chunk.Contains(v.ExternalId))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                AddToMap(loaded, rows);
            }
        }
    }

    private static async Task LoadUrlMatchesAsync<TVacancy>(
        IQueryable<TVacancy> queryable,
        IEnumerable<string> sourceUrls,
        Dictionary<Guid, TVacancy> loaded,
        CancellationToken ct)
        where TVacancy : VacancyDB
    {
        HashSet<string> candidateUrls = new(StringComparer.Ordinal);
        foreach (string rawUrl in sourceUrls)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                continue;
            }

            candidateUrls.Add(rawUrl);
            string lowered = rawUrl.ToLowerInvariant();
            if (!string.Equals(lowered, rawUrl, StringComparison.Ordinal))
            {
                candidateUrls.Add(lowered);
            }
        }

        foreach (string[] chunk in candidateUrls.Chunk(CandidateChunkSize))
        {
            List<TVacancy> rows = await queryable
                .Where(v => chunk.Contains(v.SourceUrl))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            AddToMap(loaded, rows);
        }
    }

    private static void AddToMap<TVacancy>(Dictionary<Guid, TVacancy> loaded, List<TVacancy> rows)
        where TVacancy : VacancyDB
    {
        foreach (TVacancy row in rows)
        {
            loaded.TryAdd(row.Id, row);
        }
    }
}

/// <summary>
/// Індекс кандидатів у пам'яті для швидкого пошуку за (Source, ExternalId), Id або (Source, SourceUrl).
/// </summary>
public sealed class VacancyMatchIndex<TVacancy>
    where TVacancy : VacancyDB
{
    private readonly Dictionary<Guid, TVacancy> _byId = [];
    private readonly Dictionary<(string Source, string ExternalId), TVacancy> _byExternal = new(SourceExternalIdComparer.Instance);
    private readonly Dictionary<(string Source, string SourceUrl), List<TVacancy>> _byUrl = new(SourceUrlComparer.Instance);

    public VacancyMatchIndex(IEnumerable<TVacancy> initial)
    {
        foreach (TVacancy item in initial)
        {
            Add(item);
        }
    }

    public bool ContainsId(Guid id) => id != Guid.Empty && _byId.ContainsKey(id);

    public TVacancy? FindByExternalId(string source, string? externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        return _byExternal.GetValueOrDefault((source, externalId));
    }

    public TVacancy? FindMatch(Guid id, string source, string? externalId, string? sourceUrl)
    {
        TVacancy? byExternal = FindByExternalId(source, externalId);
        if (byExternal is not null)
        {
            return byExternal;
        }

        if (id != Guid.Empty && _byId.TryGetValue(id, out TVacancy? byId))
        {
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(sourceUrl)
            && _byUrl.TryGetValue((source, sourceUrl), out List<TVacancy>? urlCandidates))
        {
            return FindCompatibleUrlMatch(urlCandidates, externalId);
        }

        return null;
    }

    public void Add(TVacancy item)
    {
        if (item.Id != Guid.Empty)
        {
            _byId[item.Id] = item;
        }

        IndexExternalId(item);
        IndexSourceUrl(item);
    }

    public void IndexExternalId(TVacancy item)
    {
        if (!string.IsNullOrWhiteSpace(item.ExternalId))
        {
            _byExternal[(item.Source, item.ExternalId)] = item;
        }
    }

    public void IndexSourceUrl(TVacancy item)
    {
        if (string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            return;
        }

        (string Source, string SourceUrl) key = (item.Source, item.SourceUrl);
        if (!_byUrl.TryGetValue(key, out List<TVacancy>? bucket))
        {
            bucket = [];
            _byUrl[key] = bucket;
        }

        if (!bucket.Contains(item))
        {
            bucket.Add(item);
        }
    }

    public void Remove(TVacancy item)
    {
        if (item.Id != Guid.Empty)
        {
            _byId.Remove(item.Id);
        }

        if (!string.IsNullOrWhiteSpace(item.ExternalId)
            && _byExternal.TryGetValue((item.Source, item.ExternalId), out TVacancy? current)
            && ReferenceEquals(current, item))
        {
            _byExternal.Remove((item.Source, item.ExternalId));
        }

        if (!string.IsNullOrWhiteSpace(item.SourceUrl)
            && _byUrl.TryGetValue((item.Source, item.SourceUrl), out List<TVacancy>? bucket))
        {
            bucket.Remove(item);
            if (bucket.Count == 0)
            {
                _byUrl.Remove((item.Source, item.SourceUrl));
            }
        }
    }

    private static TVacancy? FindCompatibleUrlMatch(List<TVacancy> candidates, string? externalId)
    {
        foreach (TVacancy candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(externalId)
                || string.IsNullOrWhiteSpace(candidate.ExternalId)
                || string.Equals(candidate.ExternalId, externalId, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class SourceExternalIdComparer : IEqualityComparer<(string Source, string ExternalId)>
    {
        public static readonly SourceExternalIdComparer Instance = new();

        public bool Equals((string Source, string ExternalId) x, (string Source, string ExternalId) y) =>
            string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ExternalId, y.ExternalId, StringComparison.Ordinal);

        public int GetHashCode((string Source, string ExternalId) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Source ?? string.Empty),
                StringComparer.Ordinal.GetHashCode(obj.ExternalId ?? string.Empty));
    }

    private sealed class SourceUrlComparer : IEqualityComparer<(string Source, string SourceUrl)>
    {
        public static readonly SourceUrlComparer Instance = new();

        public bool Equals((string Source, string SourceUrl) x, (string Source, string SourceUrl) y) =>
            string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.SourceUrl, y.SourceUrl, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Source, string SourceUrl) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Source ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.SourceUrl ?? string.Empty));
    }
}
