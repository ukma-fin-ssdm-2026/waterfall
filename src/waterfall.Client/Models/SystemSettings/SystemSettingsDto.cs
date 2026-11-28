// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: клієнтські моделі системних налаштувань сервера (GET/PUT /api/v1/admin/settings, роль Admin); перевірено: тести SystemSettingsViewModelTests, HttpGatewayServiceTests

namespace Waterfall.Client.Models.SystemSettings;

/// <summary>Системні налаштування сервера (фоновий збір вакансій), які бачить адміністратор.</summary>
public sealed record SystemSettingsDto
{
    public bool BackgroundParsingEnabled { get; init; }

    public TimeSpan Interval { get; init; }

    public int DjinniPages { get; init; }

    public int WorkUaPages { get; init; }

    public int RobotaUaPages { get; init; }

    public int DouPages { get; init; }

    public TimeSpan RequestDelay { get; init; }
}

/// <summary>Що адміністратор змінює: увімкнення фонового збору та інтервал.</summary>
public sealed record UpdateSystemSettingsRequest
{
    public bool BackgroundParsingEnabled { get; init; }

    public TimeSpan Interval { get; init; }
}
