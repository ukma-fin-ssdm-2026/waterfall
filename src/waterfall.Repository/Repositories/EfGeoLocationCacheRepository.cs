// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Waterfall.Repository.Data;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;

namespace Waterfall.Repository.Repositories;

/// <summary>
/// Реалізація репозиторію кешу координат на базі Entity Framework Core.
/// Підтримує розумну нормалізацію: номери будинків, поверхи та службові префікси не враховуються,
/// що дозволяє знаходити вулицю навіть без вказання міста або конкретного номера.
/// </summary>
public partial class EfGeoLocationCacheRepository : IGeoLocationCacheRepository
{
    private readonly WaterfallDbContext _context;

    public EfGeoLocationCacheRepository(WaterfallDbContext context)
    {
        _context = context;
    }

    public async Task<GeoLocationCacheDB?> GetByAddressAsync(string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        string norm = NormalizeKey(address);
        if (string.IsNullOrWhiteSpace(norm))
        {
            return null;
        }

        // 1. Точний збіг нормалізованого ключа (без номерів будинків та службових префіксів)
        GeoLocationCacheDB? exact = await _context.GeoLocationCache
            .FirstOrDefaultAsync(c => c.NormalizedAddress == norm, ct);

        if (exact != null)
        {
            return exact;
        }

        string[] queryWords = ExtractKeywords(norm);
        if (queryWords.Length == 0)
        {
            return null;
        }

        List<GeoLocationCacheDB> cachedList = await _context.GeoLocationCache.ToListAsync(ct);

        // 2. Збіг за прямим входженням підрядка (наприклад, збережено "київ велика васильківська", а шукають "васильківська")
        // Якщо в запиті є назва вулиці, суто місто (напр. "київ") не повинно перехоплювати пошук конкретної вулиці
        foreach (GeoLocationCacheDB entry in cachedList)
        {
            if (entry.NormalizedAddress.Contains(norm, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }

            if (norm.Contains(entry.NormalizedAddress, StringComparison.OrdinalIgnoreCase) && !IsCityOnly(entry.NormalizedAddress))
            {
                return entry;
            }
        }

        // 3. Збіг за окремими значущими словами (наприклад, "київ глушкова" знаходить "київ академіка глушкова")
        foreach (GeoLocationCacheDB entry in cachedList)
        {
            if (ContainsAllKeywords(entry.NormalizedAddress, queryWords))
            {
                return entry;
            }
        }

        // Якщо введено одне слово (наприклад, назву вулиці "васильківська" без міста)
        if (queryWords.Length == 1)
        {
            string street = queryWords[0];
            foreach (GeoLocationCacheDB entry in cachedList)
            {
                if (ContainsStreet(entry.NormalizedAddress, street))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    private static bool IsCityOnly(string normalizedAddress) =>
        Waterfall.Repository.Helpers.GeoAddressHelper.KnownCities.Any(city => string.Equals(normalizedAddress.Trim(), city, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Перевіряє, чи містить збережена адреса всі ключові слова запиту.
    /// Наприклад: для запиту ["київ", "глушкова"] адреса "київ академіка глушкова" поверне true.
    /// </summary>
    private static bool ContainsAllKeywords(string cachedAddress, string[] keywords)
    {
        foreach (string keyword in keywords)
        {
            if (!cachedAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Перевіряє збіг кореня назви вулиці серед окремих слів адреси.
    /// </summary>
    private static bool ContainsStreet(string cachedAddress, string street)
    {
        string[] addressTokens = cachedAddress.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in addressTokens)
        {
            if (token.Contains(street, StringComparison.OrdinalIgnoreCase) ||
                street.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ExtractKeywords(string normalizedAddress) =>
        normalizedAddress
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length >= 3)
            .ToArray();

    public async Task<GeoLocationCacheDB> SetCoordinatesAsync(string address, double latitude, double longitude, CancellationToken ct = default)
    {
        string norm = NormalizeKey(address);
        if (string.IsNullOrWhiteSpace(norm))
        {
            norm = address.Trim().ToLowerInvariant();
        }

        GeoLocationCacheDB? existing = await _context.GeoLocationCache
            .FirstOrDefaultAsync(c => c.NormalizedAddress == norm, ct);

        if (existing != null)
        {
            existing.Latitude = latitude;
            existing.Longitude = longitude;
            existing.CreatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(ct);
            return existing;
        }

        GeoLocationCacheDB entry = new()
        {
            Id = Guid.NewGuid(),
            NormalizedAddress = norm,
            Latitude = latitude,
            Longitude = longitude,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.GeoLocationCache.Add(entry);
        await _context.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<IReadOnlyList<GeoLocationCacheDB>> GetAllCachedAsync(CancellationToken ct = default)
    {
        return await _context.GeoLocationCache
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task UpsertRangeAsync(IEnumerable<GeoLocationCacheDB> items, CancellationToken ct = default)
    {
        foreach (GeoLocationCacheDB item in items)
        {
            if (string.IsNullOrWhiteSpace(item.NormalizedAddress))
            {
                continue;
            }

            string norm = NormalizeKey(item.NormalizedAddress);
            if (string.IsNullOrWhiteSpace(norm))
            {
                norm = item.NormalizedAddress.Trim().ToLowerInvariant();
            }

            GeoLocationCacheDB? existing = await _context.GeoLocationCache
                .FirstOrDefaultAsync(c => c.NormalizedAddress == norm, ct);

            if (existing != null)
            {
                existing.Latitude = item.Latitude;
                existing.Longitude = item.Longitude;
                existing.CreatedAt = item.CreatedAt;
            }
            else
            {
                GeoLocationCacheDB entry = new()
                {
                    Id = item.Id != Guid.Empty ? item.Id : Guid.NewGuid(),
                    NormalizedAddress = norm,
                    Latitude = item.Latitude,
                    Longitude = item.Longitude,
                    CreatedAt = item.CreatedAt
                };
                _context.GeoLocationCache.Add(entry);
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Нормалізація адреси: видаляє номери будинків, поверхи, офіси та службові слова (вул, проспект, тощо).
    /// Залишає лише чисті назви міста та вулиці для максимальної гнучкості зіставлення.
    /// </summary>
    public static string NormalizeKey(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return string.Empty;
        }

        string result = address.Trim().ToLowerInvariant();

        // 1. Видаляємо дужки та все всередині (напр. "(БЦ Гулівер)", "(2 поверх)")
        result = ParenthesesRegex().Replace(result, " ");

        // 2. Видаляємо номери будинків, дробів, корпусів та окремих цифр (напр. "3", "1а", "2-а", "101", "55/2")
        result = HouseNumbersRegex().Replace(result, " ");

        // 3. Видаляємо службові адресні префікси
        result = StreetPrefixesRegex().Replace(result, " ");

        // 4. Замінюємо розділові знаки на пробіли
        result = PunctuationRegex().Replace(result, " ");

        // 5. Стискаємо подвійні пробіли
        return WhitespaceRegex().Replace(result, " ").Trim();
    }

    [GeneratedRegex(@"\([^)]*\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ParenthesesRegex();

    [GeneratedRegex(@"\b\d+[-/a-zA-Zа-яА-ЯіїєґІЇЄҐ0-9]*\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HouseNumbersRegex();

    [GeneratedRegex(@"\b(вул|вулиця|просп|проспект|пл|площа|бульвар|бул|провулок|буд|будинок|офіс|оф|поверх|корпус|корп|бц|трц|тоц|№)\b\.?", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StreetPrefixesRegex();

    [GeneratedRegex(@"[,./\\#\-–—"";:'`]", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRegex();
}
