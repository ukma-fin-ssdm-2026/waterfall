// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Mvc;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Models.Vacancies;

/// <summary>
/// Параметри пагінації та фільтрації каталогу вакансій для HTTP API з атрибутами [FromQuery].
/// </summary>
public class VacancyFilterParameters : Waterfall.Services.Models.Vacancies.VacancyFilterParameters
{
    /// <summary>
    /// Номер запитуваної сторінки (починається з 1).
    /// </summary>
    /// <example>1</example>
    [FromQuery(Name = "page")]
    public new int Page { get => base.Page; set => base.Page = value; }

    /// <summary>
    /// Кількість елементів на одній сторінці (від 1 до 100).
    /// </summary>
    /// <example>10</example>
    [FromQuery(Name = "page_size")]
    public new int PageSize { get => base.PageSize; set => base.PageSize = value; }

    /// <summary>
    /// Пошуковий запит (пошук за назвою посади, компанією або навичками).
    /// </summary>
    /// <example>.NET</example>
    [FromQuery(Name = "search")]
    public new string? Search { get => base.Search; set => base.Search = value; }

    /// <summary>
    /// Фільтр за категорією/спеціалізацією (наприклад, .NET, QA, DevOps).
    /// </summary>
    /// <example>.NET</example>
    [FromQuery(Name = "category")]
    public new string? Category { get => base.Category; set => base.Category = value; }

    /// <summary>
    /// Фільтр за мінімальною заробітною платою.
    /// </summary>
    /// <example>2000</example>
    [FromQuery(Name = "min_salary")]
    public new decimal? MinSalary { get => base.MinSalary; set => base.MinSalary = value; }

    /// <summary>
    /// Фільтр за максимальною заробітною платою.
    /// </summary>
    /// <example>5000</example>
    [FromQuery(Name = "max_salary")]
    public new decimal? MaxSalary { get => base.MaxSalary; set => base.MaxSalary = value; }

    /// <summary>
    /// Фільтр за необхідним досвідом (наприклад: 3+ роки, 1-3 роки).
    /// </summary>
    /// <example>3+ роки</example>
    [FromQuery(Name = "experience")]
    public new string? Experience { get => base.Experience; set => base.Experience = value; }

    /// <summary>
    /// Фільтр за джерелом агрегації (WorkUa, RobotaUa, Djinni, Dou).
    /// </summary>
    /// <example>Djinni</example>
    [FromQuery(Name = "source")]
    public new JobSource? Source { get => base.Source; set => base.Source = value; }

    /// <summary>
    /// Ознака вибірки тільки активних вакансій (за замовчуванням true).
    /// Якщо false — повертає як активні, так і закриті (архівні) вакансії.
    /// </summary>
    /// <example>true</example>
    [FromQuery(Name = "only_active")]
    public new bool OnlyActive { get => base.OnlyActive; set => base.OnlyActive = value; }
}
