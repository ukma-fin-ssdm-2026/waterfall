// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: перемикач прийому вакансій від клієнта в /vacancies/sync (рішення команди до 08.12.2026); перевірено: тести VacancyServiceTests, самоперевірка автора (§8)

namespace Waterfall.Services.Configuration;

/// <summary>
/// Налаштування синхронізації бази вакансій (секція <c>VacancySync</c> у appsettings.json).
/// </summary>
public sealed class VacancySyncOptions
{
    /// <summary>
    /// Ім'я секції конфігурації.
    /// </summary>
    public const string SectionName = "VacancySync";

    /// <summary>
    /// Чи зберігати в спільну базу вакансії, які надіслав клієнт (зібрані на пристрої офлайн або імпортовані з файлу).
    /// false — сервер ігнорує <c>clientVacancies</c> і лише віддає свої вакансії: у базу пише тільки серверний парсер.
    /// Перемикач на сервері, а не в застосунку: застосунок контролює користувач, і запит на /vacancies/sync
    /// можна надіслати й без нього (threat model, зона 1).
    /// </summary>
    public bool AcceptClientVacancies { get; set; }
}
