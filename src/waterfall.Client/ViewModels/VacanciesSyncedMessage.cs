// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>Повідомлення про завершення двосторонньої синхронізації бази вакансій.</summary>
public sealed record VacanciesSyncedMessage(VacancySyncResult Result);

/// <summary>Повідомлення про надходження нових вакансій (після парсингу сайтів або локального імпорту).</summary>
public sealed record VacanciesUpdatedMessage(int Count);
