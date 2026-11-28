// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: збереження системних налаштувань адміністратора в окремий файл конфігурації сервера; перевірено: тести SystemSettingsStoreTests, самоперевірка автора (§8)

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Waterfall.Api.Models.Admin;
using Waterfall.Api.Workers;

namespace Waterfall.Api.Services;

/// <summary>Читає й змінює системні налаштування сервера (фоновий збір вакансій).</summary>
public interface ISystemSettingsStore
{
    /// <summary>Поточні значення з конфігурації сервера (з урахуванням змін адміністратора).</summary>
    SystemSettingsDto Get();

    /// <summary>Зберігає зміни адміністратора й одразу перечитує конфігурацію.</summary>
    Task<SystemSettingsDto> UpdateAsync(UpdateSystemSettingsRequest request, CancellationToken ct = default);
}

/// <summary>
/// Зміни адміністратора пишуться в окремий файл <see cref="FileName"/> поруч з appsettings.json:
/// основний файл (він у git) не переписується, а Program.cs підключає цей файл останнім, тож його значення переважають.
/// Фоновий збір перечитує Ingestion:Enabled при кожній зміні конфігурації, тож вмикається й вимикається без перезапуску.
/// </summary>
public sealed class FileSystemSettingsStore : ISystemSettingsStore
{
    /// <summary>Файл змін адміністратора (у .gitignore).</summary>
    public const string FileName = "appsettings.Admin.json";

    private const string IngestionSection = "Ingestion";

    private readonly IConfiguration _configuration;
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileSystemSettingsStore(IConfiguration configuration, IHostEnvironment environment)
        : this(configuration, Path.Combine(environment.ContentRootPath, FileName))
    {
    }

    /// <summary>Тести передають тимчасовий файл, щоб не чіпати справжній src/api.</summary>
    public FileSystemSettingsStore(IConfiguration configuration, string filePath)
    {
        _configuration = configuration;
        _filePath = filePath;
    }

    public SystemSettingsDto Get()
    {
        IngestionOptions options = _configuration.GetSection(IngestionSection).Get<IngestionOptions>() ?? new IngestionOptions();
        return new SystemSettingsDto
        {
            BackgroundParsingEnabled = options.Enabled,
            Interval = options.Interval,
            DjinniPages = options.DjinniPages,
            WorkUaPages = options.WorkUaPages,
            RobotaUaPages = options.RobotaUaPages,
            DouPages = options.DouPages,
            RequestDelay = options.RequestDelay
        };
    }

    public async Task<SystemSettingsDto> UpdateAsync(UpdateSystemSettingsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _writeLock.WaitAsync(ct);
        try
        {
            JsonObject root = File.Exists(_filePath)
                ? JsonNode.Parse(await File.ReadAllTextAsync(_filePath, ct)) as JsonObject ?? new JsonObject()
                : new JsonObject();

            if (root[IngestionSection] is not JsonObject ingestion)
            {
                ingestion = new JsonObject();
                root[IngestionSection] = ingestion;
            }

            ingestion["Enabled"] = request.BackgroundParsingEnabled;
            // Формат "c" (1.00:00:00 для доби): «24:00:00» .NET прочитав би як 24 дні
            ingestion["Interval"] = request.Interval.ToString("c", CultureInfo.InvariantCulture);

            // Спершу тимчасовий файл: сервер не має прочитати напівзаписаний JSON
            string tempPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }

        // Не чекаємо, поки FileSystemWatcher помітить файл: відповідь і фоновий збір мають бачити нові значення одразу
        (_configuration as IConfigurationRoot)?.Reload();
        return Get();
    }
}
