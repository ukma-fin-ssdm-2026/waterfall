// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Вакансію додано до «Обраного» або прибрано з нього (надсилає картка через IMessenger).
/// Отримувачі — вкладки, що показують ту саму вакансію в окремих картках.
/// </summary>
public sealed record VacancySaveChangedMessage(Guid VacancyId, bool IsSaved);
