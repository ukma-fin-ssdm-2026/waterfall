// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: моделі системних налаштувань для ролі Admin; перевірено: тести AdminSettingsControllerTests, самоперевірка автора (§8)

namespace Waterfall.Api.Models.Admin;

/// <summary>
/// Системні налаштування сервера, які бачить адміністратор: фоновий збір вакансій (секція Ingestion).
/// </summary>
public sealed record SystemSettingsDto
{
    /// <summary>Чи збирає сервер вакансії у фоні (Ingestion:Enabled); зміна діє без перезапуску.</summary>
    public bool BackgroundParsingEnabled { get; init; }

    /// <summary>Як часто збирати (Ingestion:Interval); новий інтервал діє після перезапуску API.</summary>
    /// <example>00:30:00</example>
    public TimeSpan Interval { get; init; }

    /// <summary>Скільки сторінок Djinni читати за один збір.</summary>
    public int DjinniPages { get; init; }

    /// <summary>Скільки сторінок Work.ua читати за один збір.</summary>
    public int WorkUaPages { get; init; }

    /// <summary>Скільки сторінок Robota.ua читати за один збір.</summary>
    public int RobotaUaPages { get; init; }

    /// <summary>Чи читати RSS-стрічку DOU (1) чи ні (0).</summary>
    public int DouPages { get; init; }

    /// <summary>Пауза перед кожним запитом до сайту.</summary>
    /// <example>00:00:02</example>
    public TimeSpan RequestDelay { get; init; }
}

/// <summary>
/// Що адміністратор може змінити: увімкнення фонового збору та його інтервал (те, що редагується у вкладці «Системні налаштування»).
/// </summary>
public sealed record UpdateSystemSettingsRequest
{
    /// <summary>Увімкнути чи вимкнути фоновий збір.</summary>
    public bool BackgroundParsingEnabled { get; init; }

    /// <summary>Інтервал збору: від 10 хвилин до доби.</summary>
    /// <example>00:30:00</example>
    public TimeSpan Interval { get; init; }
}
