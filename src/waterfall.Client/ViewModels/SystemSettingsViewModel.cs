// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.SystemSettings;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Модель представлення вкладки «Системні налаштування».
/// Керує параметрами фонового парсингу та періодичністю Ingestion Service,
/// завантажуючи початкові значення з файлу налаштувань appsettings.json.
/// За замовчуванням фоновий парсинг вимкнено (false).
/// </summary>
public partial class SystemSettingsViewModel : ObservableObject
{
    private static readonly Dictionary<string, string> IntervalMapping = new()
    {
        ["00:15:00"] = "15 хвилин (00:15:00)",
        ["00:30:00"] = "30 хвилин (00:30:00)",
        ["01:00:00"] = "1 година (01:00:00)",
        ["02:00:00"] = "2 години (02:00:00)",
        ["04:00:00"] = "4 години (04:00:00)",
        ["06:00:00"] = "6 годин (06:00:00)",
        ["12:00:00"] = "12 годин (12:00:00)",
        // Добу TimeSpan записує як «1.00:00:00»: «24:00:00» .NET читає як 24 дні
        [OneDayInterval] = "24 години (1.00:00:00)"
    };

    private const string OneDayInterval = "1.00:00:00";

    // Так «24 години» записували раніше; сервер читав це як 24 дні
    private const string LegacyOneDayInterval = "24:00:00";

    private readonly string? _settingsFilePath;
    private readonly ILocalVacancyStore? _localVacancyStore;
    private readonly IVacancySyncService? _vacancySyncService;
    private readonly IDistanceCalculatorService? _distanceService;
    private string? _loadedFilePath;

    public SystemSettingsViewModel() : this(LocateAppSettingsFile())
    {
    }

    public SystemSettingsViewModel(
        ILocalVacancyStore? localVacancyStore,
        IVacancySyncService? vacancySyncService,
        IDistanceCalculatorService? distanceService = null,
        string? settingsFilePath = null)
        : this(settingsFilePath ?? LocateAppSettingsFile())
    {
        _localVacancyStore = localVacancyStore;
        _vacancySyncService = vacancySyncService;
        _distanceService = distanceService;
    }

    /// <summary>
    /// Читає налаштування із заданого файлу й зберігає їх у нього ж. Тести передають сюди тимчасовий файл,
    /// щоб не переписувати справжній src/api/appsettings.json. DI цей конструктор не обирає: рядка в контейнері немає.
    /// </summary>
    public SystemSettingsViewModel(string? settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
        IntervalOptions = new ObservableCollection<string>(IntervalMapping.Values);
        LoadSettingsFromIngestionService();
    }

    public ObservableCollection<string> IntervalOptions { get; }

    /// <summary>
    /// Прапорець увімкнення фонового парсингу (по дефолту фолз).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabelText))]
    [NotifyPropertyChangedFor(nameof(ParsingStatusBadgeText))]
    private bool _isBackgroundParsingEnabled;

    /// <summary>
    /// Значення інтервалу парсингу у форматі TimeSpan (напр. "00:30:00"),
    /// завантажене з файлу налаштувань Ingestion Service.
    /// </summary>
    [ObservableProperty]
    private string _selectedInterval = "00:30:00";

    /// <summary>
    /// Відображуваний рядок інтервалу для Picker у MAUI UI.
    /// </summary>
    [ObservableProperty]
    private string _selectedIntervalDisplay = "30 хвилин (00:30:00)";

    [ObservableProperty]
    private int _douPages = 1;

    [ObservableProperty]
    private int _workUaPages = 3;

    [ObservableProperty]
    private int _robotaUaPages = 1;

    [ObservableProperty]
    private int _djinniPages = 3;

    [ObservableProperty]
    private string _requestDelay = "00:00:02";

    [ObservableProperty]
    private string _configFilePath = "Завантажено з конфігурації за замовчуванням";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isSuccessStatus;

    [ObservableProperty]
    private int _localVacanciesCount;

    [ObservableProperty]
    private int _unsyncedVacanciesCount;

    [ObservableProperty]
    private string _lastVacancySyncText = "Ще не синхронізовано";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncDatabasesNowCommand))]
    private bool _isSyncingDatabases;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public async Task RefreshSyncStatsAsync(CancellationToken ct = default)
    {
        if (_localVacancyStore is null)
        {
            return;
        }

        try
        {
            LocalVacancyStats stats = await _localVacancyStore.GetStatsAsync(ct);
            LocalVacanciesCount = stats.TotalCount;
            UnsyncedVacanciesCount = stats.UnsyncedCount;
            LastVacancySyncText = stats.LastSyncedAt.HasValue
                ? stats.LastSyncedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                : "Ще не синхронізовано";
        }
        catch
        {
            // Ігноруємо помилку читання локальної статистики
        }
    }

    [RelayCommand(CanExecute = nameof(CanSyncDatabasesNow))]
    public async Task SyncDatabasesNowAsync()
    {
        if (_vacancySyncService is null)
        {
            StatusMessage = "Сервіс синхронізації бази вакансій не налаштовано.";
            IsSuccessStatus = false;
            return;
        }

        IsSyncingDatabases = true;
        try
        {
            VacancySyncResult result = await _vacancySyncService.SyncWithServerAsync(fullSync: true);
            int geoSynced = 0;
            if (_distanceService is GeoDistanceService geoService)
            {
                geoSynced = await geoService.SyncWithServerAsync();
            }

            await RefreshSyncStatsAsync();
            if (result.Succeeded)
            {
                StatusMessage = $"Синхронізовано: надіслано {result.UploadedToServer}, отримано {result.DownloadedFromServer} (нових: {result.AddedLocally})";
                IsSuccessStatus = true;
            }
            else
            {
                StatusMessage = $"Помилка синхронізації: {result.ErrorMessage}";
                IsSuccessStatus = false;
            }
        }
        finally
        {
            IsSyncingDatabases = false;
        }
    }

    private bool CanSyncDatabasesNow() => !IsSyncingDatabases;

    public string StatusLabelText => IsBackgroundParsingEnabled ? "Увімкнено" : "Вимкнено";

    public string ParsingStatusBadgeText => IsBackgroundParsingEnabled
        ? $"Активно ({ExtractShortInterval(SelectedInterval)})"
        : "Вимкнено";

    partial void OnSelectedIntervalDisplayChanged(string value)
    {
        foreach (KeyValuePair<string, string> kvp in IntervalMapping)
        {
            if (string.Equals(kvp.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                SelectedInterval = kvp.Key;
                return;
            }
        }

        SelectedInterval = value;
    }

    partial void OnSelectedIntervalChanged(string value)
    {
        OnPropertyChanged(nameof(ParsingStatusBadgeText));
    }

    /// <summary>
    /// Завантажує налаштування із секції Ingestion у appsettings.json.
    /// Перемикач показує справжній стан Ingestion:Enabled з файлу; без файлу чи поля — вимкнено, як і на сервері.
    /// </summary>
    public void LoadSettingsFromIngestionService()
    {
        IngestionSettingsModel settings = ReadIngestionConfig(_settingsFilePath);

        _loadedFilePath = settings.FilePath;
        ConfigFilePath = !string.IsNullOrWhiteSpace(settings.FilePath)
            ? settings.FilePath
            : "src/api/appsettings.json (значення за замовчуванням)";

        // Раніше тут завжди був false: екран показував «Вимкнено», навіть коли збір ішов, а «Зберегти» його вимикало
        IsBackgroundParsingEnabled = settings.FileEnabled;

        SelectedInterval = !string.IsNullOrWhiteSpace(settings.Interval)
            ? settings.Interval
            : "00:30:00";

        // Файл, збережений до виправлення, показуємо як добу, і наступне збереження запише вже правильне значення
        if (SelectedInterval == LegacyOneDayInterval)
        {
            SelectedInterval = OneDayInterval;
        }

        if (IntervalMapping.TryGetValue(SelectedInterval, out string? display))
        {
            SelectedIntervalDisplay = display;
        }
        else
        {
            SelectedIntervalDisplay = $"{SelectedInterval} (інтервал)";
            if (!IntervalOptions.Contains(SelectedIntervalDisplay))
            {
                IntervalOptions.Add(SelectedIntervalDisplay);
            }
        }

        DouPages = settings.DouPages;
        WorkUaPages = settings.WorkUaPages;
        RobotaUaPages = settings.RobotaUaPages;
        DjinniPages = settings.DjinniPages;
        RequestDelay = settings.RequestDelay;
    }

    /// <summary>
    /// Перемикає стан фонового парсингу.
    /// </summary>
    [RelayCommand]
    public void ToggleBackgroundParsing() => IsBackgroundParsingEnabled = !IsBackgroundParsingEnabled;

    /// <summary>
    /// Зберігає налаштування фонового парсингу та інтервалу.
    /// За можливості оновлює appsettings.json сервісу.
    /// </summary>
    [RelayCommand]
    public void SaveSettings()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_loadedFilePath) && File.Exists(_loadedFilePath))
            {
                string jsonContent = File.ReadAllText(_loadedFilePath);
                var rootNode = JsonNode.Parse(jsonContent);

                if (rootNode is JsonObject rootObj)
                {
                    JsonObject ingestionObj;
                    if (rootObj.ContainsKey("Ingestion") && rootObj["Ingestion"] is JsonObject existingIngestion)
                    {
                        ingestionObj = existingIngestion;
                    }
                    else
                    {
                        ingestionObj = new JsonObject();
                        rootObj["Ingestion"] = ingestionObj;
                    }

                    ingestionObj["Enabled"] = IsBackgroundParsingEnabled;
                    ingestionObj["Interval"] = SelectedInterval;

                    File.WriteAllText(_loadedFilePath, rootObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    StatusMessage = "Налаштування збережено";
                    IsSuccessStatus = true;
                    return;
                }
            }

            StatusMessage = "Налаштування застосовано";
            IsSuccessStatus = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Помилка збереження у файл: {ex.Message}. Налаштування застосовано в пам'яті.";
            IsSuccessStatus = false;
        }
    }

    /// <summary>
    /// Скидає налаштування до початкових дефолтних значень (фоновий парсинг = false).
    /// </summary>
    [RelayCommand]
    public void ResetToDefaults()
    {
        IsBackgroundParsingEnabled = false;
        SelectedInterval = "00:30:00";
        if (IntervalMapping.TryGetValue(SelectedInterval, out string? display))
        {
            SelectedIntervalDisplay = display;
        }

        StatusMessage = "Налаштування скинуто до значень за замовчуванням (фоновий парсинг: вимкнено)";
        IsSuccessStatus = true;
    }

    private static IngestionSettingsModel ReadIngestionConfig(string? resolvedPath)
    {
        if (resolvedPath != null && File.Exists(resolvedPath))
        {
            try
            {
                string json = File.ReadAllText(resolvedPath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("Ingestion", out JsonElement ingestion))
                {
                    bool enabled = ingestion.TryGetProperty("Enabled", out JsonElement enProp) && enProp.GetBoolean();
                    string interval = ingestion.TryGetProperty("Interval", out JsonElement intProp)
                        ? intProp.GetString() ?? "00:30:00"
                        : "00:30:00";
                    int djinni = ingestion.TryGetProperty("DjinniPages", out JsonElement dj) ? dj.GetInt32() : 3;
                    int workUa = ingestion.TryGetProperty("WorkUaPages", out JsonElement wk) ? wk.GetInt32() : 3;
                    int robotaUa = ingestion.TryGetProperty("RobotaUaPages", out JsonElement ro) ? ro.GetInt32() : 1;
                    int dou = ingestion.TryGetProperty("DouPages", out JsonElement du) ? du.GetInt32() : 1;
                    string delay = ingestion.TryGetProperty("RequestDelay", out JsonElement dl) ? dl.GetString() ?? "00:00:02" : "00:00:02";
                    string? rawDir = ingestion.TryGetProperty("RawDataDirectory", out JsonElement rd) ? rd.GetString() : null;

                    return new IngestionSettingsModel(resolvedPath, enabled, interval, djinni, workUa, robotaUa, dou, delay, rawDir);
                }
            }
            catch
            {
                // У разі помилки читання повертаємо значення за замовчуванням
            }
        }

        // Значення за замовчуванням з секції Ingestion; збір без налаштувань вимкнений, як і на сервері
        return new IngestionSettingsModel(resolvedPath, false, "00:30:00", 3, 3, 1, 1, "00:00:02", string.Empty);
    }

    private static string? LocateAppSettingsFile()
    {
        // 1. Пошук відносно поточної робочої директорії
        string currentDir = Directory.GetCurrentDirectory();
        string[] directCandidates =
        [
            Path.Combine(currentDir, "src", "api", "appsettings.json"),
            Path.Combine(currentDir, "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "src", "api", "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.json")
        ];

        foreach (string candidate in directCandidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        // 2. Підйом по ієрархії каталогів вгору від AppContext.BaseDirectory
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (int i = 0; i < 7 && dir != null; i++)
        {
            string apiSettings = Path.Combine(dir.FullName, "src", "api", "appsettings.json");
            if (File.Exists(apiSettings))
            {
                return Path.GetFullPath(apiSettings);
            }

            string directSettings = Path.Combine(dir.FullName, "appsettings.json");
            if (File.Exists(directSettings))
            {
                return Path.GetFullPath(directSettings);
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string ExtractShortInterval(string interval)
    {
        return interval switch
        {
            "00:15:00" => "15 хв",
            "00:30:00" => "30 хв",
            "01:00:00" => "1 год",
            "02:00:00" => "2 год",
            "04:00:00" => "4 год",
            "06:00:00" => "6 год",
            "12:00:00" => "12 год",
            OneDayInterval => "24 год",
            _ => interval
        };
    }
}

