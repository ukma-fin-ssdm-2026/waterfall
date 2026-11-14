// AI: моделі пошуку на сайтах на запит користувача (issue #62); перевірено: тести SiteSearchServiceTests і SiteSearchesControllerTests, самоперевірка автора (§8)
// AI: Work.ua і Robota.ua в пошуку на запит, додаткові фільтри сайту (options) (issue #62); перевірено: тести SiteSearchServiceTests, самоперевірка автора (§8)
// AI: швидкий пошук Work.ua лише за сторінками списку (listOnly) (issue #62); перевірено: тести SiteSearchServiceTests, самоперевірка автора (§8)
// AI: стартова сторінка fromPage і кілька значень фільтра через кому (issue #62, на прохання Дениса); перевірено: тести SiteSearchServiceTests, самоперевірка автора (§8)

using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Models.SiteSearch;

/// <summary>
/// Запит «знайти вакансії на сайті за фільтрами»: сервер читає сайт, зберігає знайдене в базу й повертає результат.
/// Значення фільтрів — у позначеннях самого сайту, як їх подає застосунок.
/// </summary>
public sealed partial class SiteSearchRequest : IValidatableObject
{
    public const int MaxPages = 5;
    public const int MaxCategories = 10;
    public const int MaxOptions = 20;
    public const int MaxFromPage = 50;

    /// <summary>Сайт: Djinni, Dou, WorkUa або RobotaUa.</summary>
    /// <example>Djinni</example>
    [Required(ErrorMessage = "Поле source є обов'язковим")]
    public JobSource? Source { get; init; }

    /// <summary>Категорії: для Djinni — ключі категорій сайту (Python, .NET, QA Automation…), для DOU — одна категорія (Python, QA…), для Work.ua — одна сфера з адреси сайту (it, sales…); Robota.ua категорій не має.</summary>
    /// <example>["Python"]</example>
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>Досвід у позначенні сайту: Djinni — no_exp, 1y … 10y; DOU — 0-1, 1-3, 3-5, 5plus.</summary>
    /// <example>2y</example>
    public string? Experience { get; init; }

    /// <summary>Місто: для Djinni, Work.ua і Robota.ua — код з адреси сайту (kyiv, lviv…), для DOU — назва (Київ, Львів…).</summary>
    /// <example>kyiv</example>
    [StringLength(50, ErrorMessage = "Довжина city не може перевищувати 50 символів")]
    public string? City { get; init; }

    /// <summary>Лише віддалена робота.</summary>
    public bool Remote { get; init; }

    /// <summary>Інші фільтри сайту в його позначеннях: для Work.ua — параметри адреси (employment=74, student=1, salaryfrom=5…; кілька значень через кому: category=1,9 стає category=1+9), для Robota.ua — параметри після «params;» (scheduleIds=1, experienceType=true, salary=40000).</summary>
    /// <example>{"employment":"74"}</example>
    public IReadOnlyDictionary<string, string>? Options { get; init; }

    /// <summary>Зайнятість на Djinni: remote, office або parttime (remote можна задати й через <see cref="Remote"/>).</summary>
    /// <example>office</example>
    public string? Employment { get; init; }

    /// <summary>Зарплата від, доларів (лише Djinni).</summary>
    /// <example>2000</example>
    [Range(0, 100_000, ErrorMessage = "salaryFrom має бути від 0 до 100000")]
    public int? SalaryFrom { get; init; }

    /// <summary>Ключові слова: на Djinni — усі слова мають бути у вакансії, на DOU — пошуковий рядок.</summary>
    /// <example>Django</example>
    [StringLength(100, ErrorMessage = "Довжина keywords не може перевищувати 100 символів")]
    public string? Keywords { get; init; }

    /// <summary>Скільки сторінок списку читати: 1–5 (DOU завжди віддає одну стрічку з 25 найсвіжіших).</summary>
    /// <example>2</example>
    [Range(1, MaxPages, ErrorMessage = "pages має бути від 1 до 5")]
    public int Pages { get; init; } = 1;

    /// <summary>З якої сторінки списку почати (1–50): так можна дочитати наступні сторінки, не перечитуючи першу. Читаються сторінки fromPage … fromPage + pages − 1.</summary>
    /// <example>2</example>
    [Range(1, MaxFromPage, ErrorMessage = "fromPage має бути від 1 до 50")]
    public int FromPage { get; init; } = 1;

    /// <summary>Лише Work.ua: читати тільки сторінки списку, без сторінки кожної вакансії. Пошук займає секунди замість хвилини на сторінку, але опис — 1–2 рядки з картки, без навичок, досвіду й графіка.</summary>
    public bool ListOnly { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Source is not (JobSource.Djinni or JobSource.Dou or JobSource.WorkUa or JobSource.RobotaUa))
        {
            yield return new ValidationResult("Пошук на сайтах підтримує Djinni, Dou, WorkUa і RobotaUa", [nameof(Source)]);
        }

        // На Work.ua і Robota.ua місто й сфера стають частиною шляху адреси, тож це лише коди сайту
        if (Source is JobSource.WorkUa or JobSource.RobotaUa
            && (City is { Length: > 0 } && !PathCode().IsMatch(City) || Categories?.Any(category => !PathCode().IsMatch(category)) == true))
        {
            yield return new ValidationResult("Для Work.ua і Robota.ua city та categories — коди з адреси сайту (kyiv, it…)", [nameof(City), nameof(Categories)]);
        }

        // Стрічка DOU одна, наступних сторінок у неї немає
        if (FromPage > 1 && Source == JobSource.Dou)
        {
            yield return new ValidationResult("fromPage недоступний для DOU: у стрічки лише одна сторінка", [nameof(FromPage)]);
        }

        // У списку Robota.ua лише назва й посилання: без сторінок вакансій зберігати нічого
        if (ListOnly && Source == JobSource.RobotaUa)
        {
            yield return new ValidationResult("listOnly недоступний для Robota.ua: у списку сайту немає нічого, крім назви", [nameof(ListOnly)]);
        }

        if (Options is { Count: > MaxOptions })
        {
            yield return new ValidationResult($"Не більше {MaxOptions} додаткових фільтрів", [nameof(Options)]);
        }

        if (Options?.Any(option => !OptionName().IsMatch(option.Key) || !OptionValue().IsMatch(option.Value ?? string.Empty)) == true)
        {
            yield return new ValidationResult("options — пари «назва=значення» з латинських літер, цифр, «_», «-», «.» і «,»", [nameof(Options)]);
        }

        if (Categories is { Count: > MaxCategories })
        {
            yield return new ValidationResult($"Не більше {MaxCategories} категорій", [nameof(Categories)]);
        }

        if (Categories?.Any(category => string.IsNullOrWhiteSpace(category) || category.Length > 60) == true)
        {
            yield return new ValidationResult("Категорія має бути непорожньою і не довшою за 60 символів", [nameof(Categories)]);
        }

        if (Employment is not (null or "" or "remote" or "office" or "parttime"))
        {
            yield return new ValidationResult("employment має бути remote, office або parttime", [nameof(Employment)]);
        }

        if (!string.IsNullOrEmpty(Experience) && !ExperienceCode().IsMatch(Experience))
        {
            yield return new ValidationResult("experience має бути кодом сайту (no_exp, 2y, 1-3, 5plus…)", [nameof(Experience)]);
        }
    }

    [GeneratedRegex("^[a-z0-9_-]{1,10}$", RegexOptions.None, 1000)]
    private static partial Regex ExperienceCode();

    [GeneratedRegex("^[a-z0-9_-]{1,40}$", RegexOptions.None, 1000)]
    private static partial Regex PathCode();

    [GeneratedRegex("^[A-Za-z_]{1,30}$", RegexOptions.None, 1000)]
    private static partial Regex OptionName();

    [GeneratedRegex("^[A-Za-z0-9_.,-]{1,200}$", RegexOptions.None, 1000)]
    private static partial Regex OptionValue();
}

/// <summary>Стан пошуку на сайті: прогрес для шкали і, коли завершено, знайдені вакансії.</summary>
public sealed record SiteSearchStatusDto
{
    public Guid Id { get; init; }

    /// <summary>queued — чекає, поки сайт звільниться від іншого пошуку; running; completed; failed.</summary>
    /// <example>running</example>
    public string State { get; init; } = SiteSearchStates.Queued;

    /// <summary>Прочитано сторінок списку.</summary>
    public int PagesDone { get; init; }

    /// <summary>Скільки сторінок планується прочитати (сайт може закінчитися раніше).</summary>
    public int PagesTotal { get; init; }

    /// <summary>Скільки вакансій уже знайдено.</summary>
    public int Found { get; init; }

    /// <summary>Результат узято з бази: такий самий пошук уже виконувався нещодавно, тож сайт не читали.</summary>
    public bool FromCache { get; init; }

    /// <summary>Пояснення для користувача (наприклад, причина збою).</summary>
    public string? Message { get; init; }

    /// <summary>Знайдені вакансії, найновіші першими; лише коли state = completed.</summary>
    public IReadOnlyList<VacancyDto>? Vacancies { get; init; }
}

public static class SiteSearchStates
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
