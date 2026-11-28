// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Entities;

namespace Waterfall.Client.Models.Vacancies;

/// <summary>
/// Повний запис вакансії для двосторонньої синхронізації між клієнтом та сервером (POST /api/v1/vacancies/sync).
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
    public List<string> Skills { get; init; } = [];
    public DateTimeOffset PublishedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FirstSeenAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; init; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; init; } = true;
}

/// <summary>Запит двосторонньої синхронізації вакансій (POST /api/v1/vacancies/sync).</summary>
public sealed record VacancySyncRequestDto
{
    public List<VacancySyncItemDto> ClientVacancies { get; init; } = [];
    public DateTimeOffset? LastSyncedAt { get; init; }
    public bool FullSync { get; init; }
}

/// <summary>Відповідь двосторонньої синхронізації вакансій із сервера.</summary>
public sealed record VacancySyncResponseDto
{
    public List<VacancySyncItemDto> ServerVacancies { get; init; } = [];
    public DateTimeOffset ServerTime { get; init; } = DateTimeOffset.UtcNow;
    public int ReceivedFromClient { get; init; }

    /// <summary>
    /// Чи сервер зберігає вакансії, надіслані клієнтом (VacancySync:AcceptClientVacancies).
    /// false — надіслані clientVacancies проігноровано, тож локальні зміни лишаються на пристрої;
    /// null — сервер поля не надіслав (старіша версія), і тоді вважаємо вакансії прийнятими.
    /// </summary>
    public bool? ClientVacanciesAccepted { get; init; }

    public int CreatedOnServer { get; init; }
    public int UpdatedOnServer { get; init; }
    public int TotalServerVacancies { get; init; }
}

/// <summary>Параметри запиту сторінки бази вакансій (GET /api/v1/vacancies).</summary>
public sealed record VacancyQueryOptions
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 100;
    public string? Search { get; init; }
    public string? Category { get; init; }
    public string? CategoryGroup { get; init; }
    public string? Location { get; init; }
    public JobSource? Source { get; init; }
    public WorkFormat? WorkFormat { get; init; }
    public int? MaxExperienceMonths { get; init; }
    public decimal? MinSalary { get; init; }
    public decimal? MaxSalary { get; init; }
    public bool OnlyActive { get; init; } = true;
    public string? SortBy { get; init; }
    public string? SortOrder { get; init; }
}

/// <summary>Підсумок двосторонньої синхронізації бази вакансій на клієнті.</summary>
public sealed record VacancySyncResult
{
    public bool Succeeded { get; init; }
    public int UploadedToServer { get; init; }
    public int CreatedOnServer { get; init; }
    public int UpdatedOnServer { get; init; }
    public int DownloadedFromServer { get; init; }
    public int AddedLocally { get; init; }
    public int UpdatedLocally { get; init; }
    public int TotalLocalVacancies { get; init; }
    public int TotalServerVacancies { get; init; }
    public DateTimeOffset? SyncedAt { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Чи сервер прийняв локальні вакансії у цій синхронізації; null — невідомо (синхронізація не дійшла до сервера).
    /// </summary>
    public bool? ClientVacanciesAccepted { get; init; }
}

/// <summary>Статистика локальної SQLite бази вакансій клієнта.</summary>
public sealed record LocalVacancyStats(int TotalCount, int UnsyncedCount, DateTimeOffset? LastSyncedAt);
