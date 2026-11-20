// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Models.SiteSearch;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.Interfaces;

/// <summary>
/// Офлайн-збирач вакансій без підключення до сервера Waterfall API:
/// збирає вакансії напряму з відкритих джерел (DOU RSS, Djinni) та зберігає їх локально для подальшої синхронізації.
/// </summary>
public interface IOfflineSiteCollector
{
    /// <summary>Чи підтримує дане джерело прямий збір без сервера Waterfall API.</summary>
    bool Supports(JobSource source);

    /// <summary>Збирає вакансії для заданих налаштувань пошуку напряму без сервера API.</summary>
    Task<IReadOnlyList<VacancyDto>> CollectAsync(IReadOnlyList<SiteSearchRequest> requests, CancellationToken ct = default);
}
