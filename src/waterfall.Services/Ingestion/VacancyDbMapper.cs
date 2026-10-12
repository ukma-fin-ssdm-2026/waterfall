// AI: тимчасовий мапер нормалізованої вакансії в VacancyDB (issue #62); перевірено: юніт-тести tests/waterfall.Ingestion.Tests, самоперевірка автора (§8)

using System.Text.Json;
using Waterfall.Repository.Entities;

namespace Waterfall.Services.Ingestion;

/// <summary>
/// Тимчасовий мапер нормалізованої вакансії із JSON-дампу в сутність БД <see cref="VacancyDB"/>.
/// Тимчасовий, бо поки імпорт іде з одного джерела (Djinni) — так узгоджено з @s1ngleo;
/// коли джерел стане більше, мапінг варто перенести до шару репозиторію.
/// </summary>
public static class VacancyDbMapper
{
    public static VacancyDB ToNewEntity(NormalizedVacancy vacancy, DateTimeOffset now)
    {
        VacancyDB entity = new()
        {
            Id = Guid.NewGuid(),
            PublishedAt = vacancy.PublishedAt,
            FirstSeenAt = now
        };

        ApplyUpdate(entity, vacancy, now);
        return entity;
    }

    /// <summary>
    /// Оновлює наявну вакансію даними з нового прогону. Id, дату публікації й момент першої появи
    /// не змінює: відносні дати («2 дні тому») під час кожного прогону рахувалися б наново.
    /// </summary>
    public static void ApplyUpdate(VacancyDB entity, NormalizedVacancy vacancy, DateTimeOffset now)
    {
        entity.Title = vacancy.Title;
        entity.Company = vacancy.Company;
        entity.Description = vacancy.Description;
        entity.Category = vacancy.Category;
        entity.Location = vacancy.Location;
        entity.Schedule = vacancy.Schedule;
        entity.Experience = vacancy.Experience;
        entity.SalaryMin = vacancy.SalaryMin;
        entity.SalaryMax = vacancy.SalaryMax;
        entity.Currency = vacancy.Currency;
        entity.Source = vacancy.Source.ToString();
        entity.SourceUrl = vacancy.SourceUrl;
        entity.ExternalId = vacancy.ExternalId;
        entity.SkillsJson = JsonSerializer.Serialize(vacancy.Skills);
        entity.LastSeenAt = now;
        entity.IsActive = true;
    }
}
