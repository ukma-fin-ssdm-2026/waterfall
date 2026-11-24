// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: налаштування фонового збору читаються й зберігаються через сервер (GET/PUT /api/v1/admin/settings) і видимі лише ролі Admin; перевірено: тести SystemSettingsViewModelTests, самоперевірка автора (§8)

using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.SystemSettings;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Модель представлення вкладки «Системні налаштування».
/// Фоновий збір вакансій (увімкнення та інтервал) — налаштування сервера: їх бачить і змінює лише адміністратор,
/// а сервер сам перевіряє роль. Синхронізація локальної бази доступна кожному користувачу.
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
    private const string DefaultInterval = "00:30:00";

    private readonly IHttpGatewayService? _gateway;
    private readonly IAuthTokenStore? _tokens;
    private readonly ILocalVacancyStore? _localVacancyStore;
    private readonly IVacancySyncService? _vacancySyncService;
    private readonly IDistanceCalculatorService? _distanceService;

    // Що зараз на сервері: автозбереження сторінки не надсилає ті самі значення вдруге
    private (bool Enabled, TimeSpan Interval)? _serverState;

    public SystemSettingsViewModel()
        : this(null, null, null, null)
    {
    }

    public SystemSettingsViewModel(
        IHttpGatewayService? gateway,
        IAuthTokenStore? tokens,
        ILocalVacancyStore? localVacancyStore,
        IVacancySyncService? vacancySyncService,
        IDistanceCalculatorService? distanceService = null,
        IMessenger? messenger = null)
    {
        _gateway = gateway;
        _tokens = tokens;
        _localVacancyStore = localVacancyStore;
        _vacancySyncService = vacancySyncService;
        _distanceService = distanceService;
        IntervalOptions = new ObservableCollection<string>(IntervalMapping.Values);

        // Вхід чи вихід, поки вкладка відкрита, змінює роль: перечитуємо, що показувати
        messenger?.Register<SystemSettingsViewModel, UserAuthStateChangedMessage>(this, static (vm, message) =>
        {
            _ = message;
            _ = vm.LoadServerSettingsAsync();
        });
    }

    public ObservableCollection<string> IntervalOptions { get; }

    /// <summary>Чи увійшов адміністратор: лише він бачить і змінює налаштування фонового збору.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotAdmin))]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private bool _isAdmin;

    public bool IsNotAdmin => !IsAdmin;

    /// <summary>Прапорець фонового збору на сервері (за замовчуванням вимкнено).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabelText))]
    [NotifyPropertyChangedFor(nameof(ParsingStatusBadgeText))]
    private bool _isBackgroundParsingEnabled;

    /// <summary>Інтервал збору у форматі TimeSpan (напр. "00:30:00").</summary>
    [ObservableProperty]
    private string _selectedInterval = DefaultInterval;

    /// <summary>Відображуваний рядок інтервалу для Picker у MAUI UI.</summary>
    [ObservableProperty]
    private string _selectedIntervalDisplay = IntervalMapping[DefaultInterval];

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

    /// <summary>Де зберігаються налаштування: на сервері, у файлі поверх appsettings.json.</summary>
    [ObservableProperty]
    private string _configFilePath = "сервер, src/api/appsettings.Admin.json";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isSuccessStatus;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private bool _isSaving;

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

    public string StatusLabelText => IsBackgroundParsingEnabled ? "Увімкнено" : "Вимкнено";

    public string ParsingStatusBadgeText => IsBackgroundParsingEnabled
        ? $"Активно ({ExtractShortInterval(SelectedInterval)})"
        : "Вимкнено";

    /// <summary>
    /// Визначає роль за токеном і, якщо це адміністратор, читає налаштування з сервера.
    /// Роль із токена лише вирішує, що показати: зберегти без ролі Admin однаково не дасть сервер.
    /// </summary>
    public async Task LoadServerSettingsAsync(CancellationToken ct = default)
    {
        string? token = _tokens is null ? null : await _tokens.GetTokenAsync(ct);
        AuthSession? session = token is null ? null : AuthSession.FromToken(token);
        IsAdmin = session is { Role: UserRole.Admin };
        if (!IsAdmin || _gateway is null)
        {
            return;
        }

        try
        {
            Apply(await _gateway.GetSystemSettingsAsync(ct));
        }
        catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            // Токен застарів або роль змінилася на сервері: показуємо те саме, що й звичайному користувачу
            IsAdmin = false;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            StatusMessage = $"Не вдалося отримати налаштування з сервера: {ex.Message}";
            IsSuccessStatus = false;
        }
    }

    /// <summary>Зберігає увімкнення фонового збору та інтервал на сервері.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveSettings))]
    public async Task SaveSettingsAsync()
    {
        // Сторінка зберігає автоматично при кожній зміні, тож перевіряємо роль і тут, а не лише в CanExecute
        if (!IsAdmin)
        {
            return;
        }

        if (_gateway is null)
        {
            StatusMessage = "Немає з'єднання з сервером: налаштування не збережено.";
            IsSuccessStatus = false;
            return;
        }

        if (!TimeSpan.TryParseExact(SelectedInterval, "c", CultureInfo.InvariantCulture, out TimeSpan interval))
        {
            StatusMessage = $"Невідомий інтервал «{SelectedInterval}».";
            IsSuccessStatus = false;
            return;
        }

        if (_serverState == (IsBackgroundParsingEnabled, interval))
        {
            return;
        }

        IsSaving = true;
        try
        {
            SystemSettingsDto saved = await _gateway.UpdateSystemSettingsAsync(new UpdateSystemSettingsRequest
            {
                BackgroundParsingEnabled = IsBackgroundParsingEnabled,
                Interval = interval
            });
            Apply(saved);
            // Сервер перечитує Enabled одразу, а інтервал бере лише при старті фонового збору
            StatusMessage = "Налаштування збережено на сервері (новий інтервал — після перезапуску сервера)";
            IsSuccessStatus = true;
        }
        catch (ApiException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            IsAdmin = false;
            StatusMessage = "Змінювати системні налаштування може лише адміністратор.";
            IsSuccessStatus = false;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException or TaskCanceledException)
        {
            StatusMessage = $"Не вдалося зберегти налаштування: {ex.Message}";
            IsSuccessStatus = false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanSaveSettings() => IsAdmin && !IsSaving;

    /// <summary>Перемикає стан фонового збору (до збереження — лише на екрані).</summary>
    [RelayCommand]
    public void ToggleBackgroundParsing() => IsBackgroundParsingEnabled = !IsBackgroundParsingEnabled;

    /// <summary>Повертає на екрані значення за замовчуванням (фоновий збір вимкнено, 30 хвилин); на сервер — після «Зберегти».</summary>
    [RelayCommand]
    public void ResetToDefaults()
    {
        IsBackgroundParsingEnabled = false;
        SetInterval(DefaultInterval);
        StatusMessage = "Значення за замовчуванням: фоновий збір вимкнено, 30 хв";
        IsSuccessStatus = true;
    }

    /// <summary>Ховає банер повідомлення, нічого не змінюючи.</summary>
    [RelayCommand]
    public void DismissStatus() => StatusMessage = null;

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
                StatusMessage = $"Синхронізовано: отримано {result.DownloadedFromServer} (нових: {result.AddedLocally}), геокеш: {geoSynced}";
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

    private void Apply(SystemSettingsDto settings)
    {
        _serverState = (settings.BackgroundParsingEnabled, settings.Interval);
        IsBackgroundParsingEnabled = settings.BackgroundParsingEnabled;
        SetInterval(settings.Interval.ToString("c", CultureInfo.InvariantCulture));
        DjinniPages = settings.DjinniPages;
        WorkUaPages = settings.WorkUaPages;
        RobotaUaPages = settings.RobotaUaPages;
        DouPages = settings.DouPages;
        RequestDelay = settings.RequestDelay.ToString("c", CultureInfo.InvariantCulture);
    }

    private void SetInterval(string interval)
    {
        SelectedInterval = interval;
        if (IntervalMapping.TryGetValue(interval, out string? display))
        {
            SelectedIntervalDisplay = display;
            return;
        }

        // Значення не з нашого списку (наприклад, задане вручну на сервері) показуємо як є
        string custom = $"{interval} (інтервал)";
        if (!IntervalOptions.Contains(custom))
        {
            IntervalOptions.Add(custom);
        }

        SelectedIntervalDisplay = custom;
        SelectedInterval = interval;
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
