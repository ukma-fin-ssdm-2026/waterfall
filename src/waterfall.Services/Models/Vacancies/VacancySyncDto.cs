// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;

namespace Waterfall.Services.Models.Vacancies;

/// <summary>
/// Повний запис вакансії для двосторонньої синхронізації між локальною БД клієнта та сервером.
/// </summary>
public sealed record VacancySyncItemDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? ExternalId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? CategoryGroup { get; init; }
    public string Location { get; init; } = string.Empty;
    public string? FullAddress { get; init; }
    public string? Schedule { get; init; }
    public string? Experience { get; init; }
    public int? ExperienceMonths { get; init; }
    public WorkFormat WorkFormat { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public string Currency { get; init; } = string.Empty;
    public JobSource Source { get; init; } = JobSource.Unknown;
    public string SourceUrl { get; init; } = string.Empty;
    public IReadOnlyList<string> Skills { get; init; } = Array.Empty<string>();
    public DateTimeOffset PublishedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FirstSeenAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// Запит двосторонньої синхронізації бази вакансій: клієнт передає локально зібрані або змінені
/// в офлайні вакансії, а сервер об'єднує їх зі своєю БД і повертає актуальні серверні записи.
/// </summary>
public sealed record VacancySyncRequestDto
{
    /// <summary>Вакансії, зібрані або змінені клієнтом локально (офлайн).</summary>
    public List<VacancySyncItemDto> ClientVacancies { get; init; } = [];

    /// <summary>Момент останньої успішної синхронізації клієнта (UTC); null — повернути всю базу сервера.</summary>
    public DateTimeOffset? LastSyncedAt { get; init; }

    /// <summary>true — повернути повний зліпок бази сервера незалежно від LastSyncedAt.</summary>
    public bool FullSync { get; init; }
}

/// <summary>
/// Відповідь двосторонньої синхронізації вакансій: об'єднані дані сервера та статистика обміну.
/// </summary>
public sealed record VacancySyncResponseDto
{
    /// <summary>Актуальні вакансії сервера (усі або змінені з моменту LastSyncedAt + об'єднані записи).</summary>
    public List<VacancySyncItemDto> ServerVacancies { get; init; } = [];

    /// <summary>Час сервера (UTC), який клієнт зберігає як LastSyncedAt для наступної дельта-синхронізації.</summary>
    public DateTimeOffset ServerTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Скільки валідних записів отримано від клієнта у цьому запиті.</summary>
    public int ReceivedFromClient { get; init; }

    /// <summary>Чи сервер зберігає вакансії від клієнта (VacancySync:AcceptClientVacancies); false — надіслані clientVacancies проігноровано.</summary>
    public bool ClientVacanciesAccepted { get; init; }

    /// <summary>Скільки нових вакансій додано до серверної БД.</summary>
    public int CreatedOnServer { get; init; }

    /// <summary>Скільки існуючих вакансій оновлено на сервері.</summary>
    public int UpdatedOnServer { get; init; }

    /// <summary>Загальна кількість вакансій у серверній базі даних після синхронізації.</summary>
    public int TotalServerVacancies { get; init; }
}
