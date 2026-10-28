// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.SystemSettings;

/// <summary>
/// Модель налаштувань Ingestion Service, завантажена з appsettings.json.
/// </summary>
public sealed record IngestionSettingsModel(
    string? FilePath,
    bool FileEnabled,
    string Interval,
    int DjinniPages,
    int WorkUaPages,
    int DouPages,
    string RequestDelay,
    string? RawDataDirectory);

