// AI: повідомлення «вакансію переглянуто» для фільтра «Приховати переглянуті» (issue #62, на прохання Дениса); перевірено: тести CatalogViewModelTests, самоперевірка автора (§8)

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Користувач розгорнув подробиці вакансії або відкрив її на сайті (надсилає картка через IMessenger).
/// Каталог запам'ятовує це, щоб фільтр «Приховати переглянуті» враховував і перегляди цього сеансу.
/// </summary>
public sealed record VacancyViewedMessage(Guid VacancyId);
