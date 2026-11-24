// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Vacancies;

/// <summary>
/// Знімок активних фільтрів каталогу, який зберігається на пристрої при кожній зміні (REL-002).
/// Лише налаштування користувача: жодних вакансій чи персональних даних.
/// </summary>
public sealed class CatalogFilterState
{
    /// <summary>Поточна версія формату. Знімок іншої версії ігнорується, а не відновлюється частково.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string SearchText { get; set; } = string.Empty;
    public string SortOption { get; set; } = string.Empty;
    public string SourceFilter { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string SalaryCurrency { get; set; } = string.Empty;
    public string CompanyFilter { get; set; } = string.Empty;
    public string MinSalary { get; set; } = string.Empty;
    public string MaxSalary { get; set; } = string.Empty;

    public bool TitleOnly { get; set; }
    public bool OnlyWithSalary { get; set; }
    public bool OnlyActive { get; set; }
    public bool HideViewed { get; set; }
    public bool IncludeNoSalary { get; set; }
    public bool IsRemote { get; set; }
    public bool WhiteListMatchAll { get; set; }

    public List<string> WhiteListWords { get; set; } = [];
    public List<string> BanListWords { get; set; } = [];

    /// <summary>Поля форм пошуку на сайтах: назва властивості ViewModel → значення рядком.</summary>
    public Dictionary<string, string>? SiteFilters { get; set; }

    /// <summary>Позначені галочки Work.ua у вигляді «параметр=код» (наприклад, «category=24»).</summary>
    public List<string>? SiteChoices { get; set; }
}
