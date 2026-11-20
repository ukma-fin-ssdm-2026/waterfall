// AI: моделі пошуку на сайтах для клієнта (issue #62); перевірено: тести CatalogSiteSearchTests, самоперевірка автора (§8)

using System.Text.Json.Serialization;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Models.SiteSearch;

/// <summary>Запит POST /api/v1/site-searches: сайт і фільтри в позначеннях сайту.</summary>
public sealed record SiteSearchRequest
{
    public JobSource Source { get; init; }
    public List<string>? Categories { get; init; }
    public string? Experience { get; init; }
    public string? City { get; init; }
    public bool Remote { get; init; }
    public string? Employment { get; init; }
    public int? SalaryFrom { get; init; }
    public string? Keywords { get; init; }
    public int Pages { get; init; } = 1;

    /// <summary>З якої сторінки списку почати (1–50); читаються сторінки fromPage … fromPage + pages − 1.</summary>
    public int FromPage { get; init; } = 1;

    /// <summary>Інші фільтри Work.ua (параметри адреси) чи Robota.ua (після «params;») у позначеннях сайту.</summary>
    public Dictionary<string, string>? Options { get; init; }

    /// <summary>Лише Work.ua: тільки сторінки списку, без сторінки кожної вакансії (швидко, але опис скорочений).</summary>
    public bool ListOnly { get; init; }
}

/// <summary>Стан пошуку на сайті: прогрес і, коли завершено, знайдені вакансії.</summary>
public sealed record SiteSearchStatusDto
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public Guid Id { get; init; }
    public string State { get; init; } = Queued;
    public int PagesDone { get; init; }
    public int PagesTotal { get; init; }
    public int Found { get; init; }
    public bool FromCache { get; init; }
    public string? Message { get; init; }
    public List<VacancyDto>? Vacancies { get; init; }

    [JsonIgnore]
    public bool IsInProgress => State is Queued or Running;

    /// <summary>Частка прочитаних сторінок, 0…1, для шкали прогресу.</summary>
    [JsonIgnore]
    public double Progress => PagesTotal > 0 ? Math.Clamp((double)PagesDone / PagesTotal, 0, 1) : 0;
}
