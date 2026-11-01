// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;
using Waterfall.Repository.Entities;

namespace Waterfall.Repository.Normalization;

/// <summary>
/// Обчислює похідні від полів вакансії атрибути — <see cref="VacancyDB.SearchText"/>, <see cref="VacancyDB.WorkFormat"/>
/// та <see cref="VacancyDB.CategoryGroup"/>. Викликається з <c>WaterfallDbContext.SaveChanges</c> для кожної доданої
/// чи зміненої вакансії, тож усі шляхи запису (парсери, POST/PUT каталогу, сідування) дають однакові дані.
/// </summary>
public static class VacancyNormalizer
{
    // Джерела пишуть формат роботи словами в Location і Schedule: Djinni — «Віддалено» (TELECOMMUTE),
    // DOU — «Київ, віддалено», Work.ua — «Повна зайнятість · Гібридний формат»
    private static readonly string[] RemoteMarkers =
        ["віддален", "дистанційн", "remote", "telecommute", "work from home"];

    private static readonly string[] HybridMarkers =
        ["гібрид", "hybrid", "змішан"];

    private static readonly string[] OfficeMarkers =
        ["офіс", "office", "на місці", "on-site", "onsite"];

    // Локації без міста: самі по собі вони не означають роботу в офісі
    private static readonly string[] NonCityMarkers =
        ["не вказано", "не відомо", "україна", "ukraine", "за кордоном", "abroad", "worldwide", "anywhere", "кілька країн"];

    /// <summary>Перераховує похідні поля вакансії з її назви, компанії, опису, тегів, локації, графіка й категорії.</summary>
    public static void Apply(VacancyDB vacancy)
    {
        vacancy.SearchText = BuildSearchText(vacancy);
        vacancy.WorkFormat = ResolveWorkFormat(vacancy.Location, vacancy.Schedule);
        vacancy.CategoryGroup = VacancyCategoryMap.Resolve(vacancy.Category);
    }

    /// <summary>
    /// Текст для пошуку: назва, компанія, опис і теги в нижньому регістрі. SQLite порівнює кирилицю з урахуванням
    /// регістру (lower() там лише для латиниці), тож пошук іде по цьому вже зниженому тексту — і «бухгалтер»
    /// знаходить «Бухгалтер» на обох провайдерах БД.
    /// </summary>
    public static string BuildSearchText(VacancyDB vacancy) =>
        string.Join(
            ' ',
            new[] { vacancy.Title, vacancy.Company, vacancy.Description, string.Join(' ', ParseTags(vacancy.SkillsJson)) }
                .Select(part => part ?? string.Empty))
            .ToLowerInvariant();

    /// <summary>
    /// Формат роботи з тексту локації та графіка. Правило (свідоме рішення, а не вгадування): явна згадка формату
    /// завжди перемагає, тому «Київ, віддалено» — це лише <see cref="WorkFormat.Remote"/>, а не Remote|Office.
    /// Місто без згадки формату означає роботу в офісі, бо джерело вказало фізичне місце роботи.
    /// </summary>
    public static WorkFormat ResolveWorkFormat(string? location, string? schedule)
    {
        string text = string.Join(' ', location ?? string.Empty, schedule ?? string.Empty).ToLowerInvariant();

        WorkFormat format = WorkFormat.None;
        if (ContainsAny(text, RemoteMarkers))
        {
            format |= WorkFormat.Remote;
        }

        if (ContainsAny(text, HybridMarkers))
        {
            format |= WorkFormat.Hybrid;
        }

        if (ContainsAny(text, OfficeMarkers))
        {
            format |= WorkFormat.Office;
        }

        if (format == WorkFormat.None && HasCity(location))
        {
            format = WorkFormat.Office;
        }

        return format;
    }

    private static bool ContainsAny(string text, string[] markers) =>
        markers.Any(marker => text.Contains(marker, StringComparison.Ordinal));

    // Чи лишається в локації щось, крім «не вказано», назви країни чи «за кордоном»
    private static bool HasCity(string? location)
    {
        string text = (location ?? string.Empty).ToLowerInvariant();
        foreach (string marker in NonCityMarkers)
        {
            text = text.Replace(marker, " ", StringComparison.Ordinal);
        }

        return text.Any(char.IsLetter);
    }

    private static IReadOnlyList<string> ParseTags(string? skillsJson)
    {
        if (string.IsNullOrWhiteSpace(skillsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(skillsJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
