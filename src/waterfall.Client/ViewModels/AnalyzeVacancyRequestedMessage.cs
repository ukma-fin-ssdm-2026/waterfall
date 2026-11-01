// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Кнопка «ШІ-аналіз» на картці вакансії передає вакансію ШІ-консультанту (FR-010).
/// Обробляє <see cref="AIConsultantViewModel"/>: перемикає вкладку та запускає аналіз.
/// </summary>
/// <param name="Vacancy">Вакансія з картки (лише читання, без збереження стану картки).</param>
public sealed record AnalyzeVacancyRequestedMessage(VacancyDto Vacancy);
