// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: пошук на сайтах за фільтрами панелі зі шкалою прогресу (issue #62, зі згоди Дениса); перевірено: тести CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: пошук на Work.ua і Robota.ua з панелей (коди адрес звірено з сайтами 10.11.2026), спільні ключові слова блоку 1 (issue #62, зі згоди Дениса); перевірено: CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: каталог без базового списку з БД — лише результати «Знайти», рядок пошуку й фільтри працюють локально (рішення команди 10.11.2026, зі згоди Дениса); перевірено: CatalogViewModelTests, самоперевірка автора (§8)
// AI: вайт-, бан-лист і рядок пошуку шукають слова також в описі вакансії (issue #62); перевірено: CatalogViewModelTests, самоперевірка автора (§8)
// AI: перемикач «відкривати кожну вакансію» Work.ua з попередженням про тривалість (issue #62); перевірено: CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: кнопка Robota.ua у рядку «Парсинг:» (issue #62); перевірено: CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: «Від N грн» на Robota.ua приховує вакансії без зарплати (salaryType=false, звірено з сайтом 14.11.2026) (issue #62); перевірено: CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: Djinni — англійська, тип компанії, домен, регіон і країна в запиті (коди звірено з djinni.co 15.11.2026); без неробочих «Рівень освіти» Work.ua і сторінок DOU; Robota.ua у фільтрі джерела (issue #62); перевірено: CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: на прохання Дениса — мультивибір сфер, мов і зайнятості Work.ua (коди звірено з сайтом 15.11.2026), «Для студентів», «Включно без ЗП», режим вайт-листа «Усі / Будь-яке», «Приховати переглянуті», стартова сторінка пошуку (issue #62); перевірено: CatalogViewModelTests і CatalogSiteSearchTests, самоперевірка автора (§8)

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Collections;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.SiteSearch;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Вкладка «Каталог». Базового списку з БД немає: поки користувач не натиснув «Знайти» (пошук на сайті),
/// список порожній і показує підказку. Знайдені вакансії далі уточнюють рядок пошуку (із затримкою debounce),
/// сортування й фільтри блоку 2 — локально, без запитів до сервера.
/// </summary>
/// <remarks>
/// Команди викликаються з UI-потоку, а await повертається на нього ж — колекцію змінюємо без маршалінгу.
/// </remarks>
public partial class CatalogViewModel : ObservableObject
{
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(400);

    /// <summary>Як часто питати сервер про прогрес пошуку на сайті.</summary>
    public static readonly TimeSpan SiteSearchPollInterval = TimeSpan.FromMilliseconds(700);

    /// <summary>Більше сторінок сервер за один пошук не читає (кожна сторінка Djinni — 15 вакансій).</summary>
    public const int MaxSitePages = 5;

    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _time;

    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _siteSearchCts;
    private CancellationTokenSource? _distancePrefetchCts;

    // Останній застосований текст рядка пошуку: той самий запит (наприклад, з доданим пробілом) не записуємо в історію двічі
    private string? _appliedQuery;

    // Результат останнього пошуку на сайті без локальних фільтрів; список на екрані — його відфільтрована частина
    private readonly List<VacancyDto> _loadedDtos = [];

    // Вакансії, переглянуті в цьому сеансі (сервер позначає IsViewed лише на момент пошуку)
    private readonly HashSet<Guid> _viewedIds = [];
    private bool _isLocalFilterActive;
    private int _fetchGeneration;
    private bool _isSyncingSort;
    private bool _hasExplicitSiteSelection;
    private bool _isSyncingPlatformFlags;
    private bool _isSyncingFromPage;
    private readonly IUrlLauncher? _urlLauncher;
    private readonly IDistanceCalculatorService? _distanceService;
    private readonly IAuthTokenStore? _tokenStore;
    private readonly ILocalVacancyStore? _localVacancyStore;
    private readonly IOfflineSiteCollector? _offlineCollector;
    private readonly IFileDialogService? _fileDialog;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, double?> _distanceCache = new();

    public CatalogViewModel(
        IHttpGatewayService gateway,
        IMessenger messenger,
        TimeProvider time,
        IUrlLauncher? urlLauncher = null,
        IDistanceCalculatorService? distanceService = null,
        IAuthTokenStore? tokenStore = null,
        ILocalVacancyStore? localVacancyStore = null,
        IOfflineSiteCollector? offlineCollector = null,
        IFileDialogService? fileDialog = null)
    {
        _gateway = gateway;
        _messenger = messenger;
        _time = time;
        _urlLauncher = urlLauncher;
        _distanceService = distanceService;
        _tokenStore = tokenStore;
        _localVacancyStore = localVacancyStore;
        _offlineCollector = offlineCollector;
        _fileDialog = fileDialog;

        // Зміна «Обраного» з іншої вкладки: оновлюємо картку на місці, без перезавантаження списку.
        messenger.Register<CatalogViewModel, VacancySaveChangedMessage>(this, static (vm, message) =>
        {
            foreach (VacancyItemViewModel item in vm.Vacancies.Where(v => v.Id == message.VacancyId))
            {
                item.IsSaved = message.IsSaved;
            }
        });

        // Перегляд вакансії в картці: запам'ятовуємо для фільтра «Приховати переглянуті» (сам список одразу не змінюємо)
        messenger.Register<CatalogViewModel, VacancyViewedMessage>(this, static (vm, message) => vm._viewedIds.Add(message.VacancyId));

        // Зміна стану авторизації користувача
        messenger.Register<CatalogViewModel, UserAuthStateChangedMessage>(this, static (vm, message) =>
        {
            vm.IsUserLoggedIn = message.IsLoggedIn;
            if (!message.IsLoggedIn)
            {
                vm.IsGuestBannerDismissed = false;
            }
        });

        // Оновлення поточної адреси користувача: очищуємо кеш відстаней та перераховуємо сортування за відстанню.
        messenger.Register<CatalogViewModel, UserAddressChangedMessage>(this, static (vm, message) =>
        {
            vm._distanceCache.Clear();
            if (IsDistanceSort(vm.SelectedSortOption) && vm.HasLoaded)
            {
                _ = vm.ExecuteLocalFilterAndSortAsync(updateBanner: true);
            }
        });
    }

    private readonly BulkObservableCollection<VacancyItemViewModel> _vacancies = new();
    public ObservableCollection<VacancyItemViewModel> Vacancies => _vacancies;

    /// <summary>
    /// Текст пошукового рядка; після паузи <see cref="SearchDebounce"/> звужує знайдені вакансії.
    /// </summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Прив'язка до RefreshView («потягнути, щоб оновити»).
    /// </summary>
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowTotal))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalText))]
    public partial int TotalCount { get; set; }

    /// <summary>
    /// Дія для переходу на екран входу/профілю з плашки неавторизованого користувача.
    /// </summary>
    public Func<Task>? NavigateToLoginAction { get; set; }

    /// <summary>
    /// Чи авторизований користувач у системі.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUserLoggedOut))]
    [NotifyPropertyChangedFor(nameof(IsGuestBannerVisible))]
    public partial bool IsUserLoggedIn { get; set; }

    public bool IsUserLoggedOut => !IsUserLoggedIn;

    /// <summary>
    /// Чи приховав користувач плашку гостя вручну (кнопка ✕).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGuestBannerVisible))]
    public partial bool IsGuestBannerDismissed { get; set; }

    /// <summary>
    /// Відображати плашку неавторизованого користувача (якщо не залогінений і не закрив плашку).
    /// </summary>
    public bool IsGuestBannerVisible => IsUserLoggedOut && !IsGuestBannerDismissed;

    /// <summary>
    /// Приховати плашку неавторизованого користувача на поточну сесію.
    /// </summary>
    [RelayCommand]
    private void DismissGuestBanner()
    {
        IsGuestBannerDismissed = true;
    }

    /// <summary>
    /// Перехід до екрану входу/реєстрації з плашки гостя.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToLoginAsync()
    {
        if (NavigateToLoginAction is not null)
        {
            await NavigateToLoginAction();
        }
    }

    /// <summary>
    /// Оновлює статус авторизації на основі збереженого токена.
    /// </summary>
    public async Task RefreshAuthStatusAsync(CancellationToken ct = default)
    {
        if (_tokenStore is not null)
        {
            try
            {
                string? token = await _tokenStore.GetTokenAsync(ct);
                Waterfall.Client.Models.Auth.AuthSession? session = token is null ? null : Waterfall.Client.Models.Auth.AuthSession.FromToken(token);
                IsUserLoggedIn = session is not null && !session.IsExpired(_time.GetUtcNow());
            }
            catch
            {
                IsUserLoggedIn = false;
            }
        }
    }

    /// <summary>
    /// Показувати розширену панель фільтрів (US-002, FR-004).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterToggleText))]
    [NotifyPropertyChangedFor(nameof(IsDouPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsWorkUaPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsDjinniPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsRobotaUaPanelVisible))]
    [NotifyPropertyChangedFor(nameof(DouButtonText))]
    [NotifyPropertyChangedFor(nameof(WorkUaButtonText))]
    [NotifyPropertyChangedFor(nameof(DjinniButtonText))]
    [NotifyPropertyChangedFor(nameof(RobotaUaButtonText))]
    public partial bool IsFilterPanelVisible { get; set; }

    /// <summary>Показувати блок налаштувань парсингу сайтів.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ParsePanelToggleText))]
    public partial bool IsParsePanelVisible { get; set; } = true;

    /// <summary>Показувати блок локальних фільтрів знайдених вакансій.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocalFilterPanelToggleText))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool IsLocalFilterPanelVisible { get; set; }

    public string ParsePanelToggleText => IsParsePanelVisible ? "Парсинг сайтів ▲" : "Парсинг сайтів ▼";
    public string LocalFilterPanelToggleText => IsLocalFilterPanelVisible ? "Фільтри списку ▲" : "Фільтри списку ▼";

    /// <summary>
    /// Обрана платформа у панелі фільтрів: "DOU", "WorkUa", "Djinni" або "RobotaUa".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDouSelected))]
    [NotifyPropertyChangedFor(nameof(IsWorkUaSelected))]
    [NotifyPropertyChangedFor(nameof(IsDjinniSelected))]
    [NotifyPropertyChangedFor(nameof(IsRobotaUaSelected))]
    [NotifyPropertyChangedFor(nameof(IsDouPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsWorkUaPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsDjinniPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsRobotaUaPanelVisible))]
    [NotifyPropertyChangedFor(nameof(ActivePlatformTitle))]
    [NotifyPropertyChangedFor(nameof(ActivePlatformSubtitle))]
    [NotifyPropertyChangedFor(nameof(DouButtonText))]
    [NotifyPropertyChangedFor(nameof(WorkUaButtonText))]
    [NotifyPropertyChangedFor(nameof(DjinniButtonText))]
    [NotifyPropertyChangedFor(nameof(RobotaUaButtonText))]
    public partial string SelectedPlatform { get; set; } = "DOU";

    public bool IsDouSelected => SelectedPlatform == "DOU";
    public bool IsWorkUaSelected => SelectedPlatform == "WorkUa";
    public bool IsDjinniSelected => SelectedPlatform == "Djinni";
    public bool IsRobotaUaSelected => SelectedPlatform == "RobotaUa";

    public bool IsDouPanelVisible => IsFilterPanelVisible && IsDouSelected;
    public bool IsWorkUaPanelVisible => IsFilterPanelVisible && IsWorkUaSelected;
    public bool IsDjinniPanelVisible => IsFilterPanelVisible && IsDjinniSelected;
    public bool IsRobotaUaPanelVisible => IsFilterPanelVisible && IsRobotaUaSelected;

    // ==================== БАГАТОСАЙТОВИЙ ПАРСИНГ (ОБРАННЯ N САЙТІВ) ====================

    /// <summary>Чи парсити DOU під час натискання «Спарсити».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesCount))]
    [NotifyPropertyChangedFor(nameof(FetchButtonText))]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial bool IsDouParseEnabled { get; set; } = true;

    /// <summary>Чи парсити Work.ua під час натискання «Спарсити».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesCount))]
    [NotifyPropertyChangedFor(nameof(FetchButtonText))]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial bool IsWorkUaParseEnabled { get; set; }

    /// <summary>Чи парсити Djinni під час натискання «Спарсити».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesCount))]
    [NotifyPropertyChangedFor(nameof(FetchButtonText))]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial bool IsDjinniParseEnabled { get; set; }

    /// <summary>Чи парсити Robota.ua під час натискання «Спарсити».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesCount))]
    [NotifyPropertyChangedFor(nameof(FetchButtonText))]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial bool IsRobotaUaParseEnabled { get; set; }

    /// <summary>Скільки сайтів зараз увімкнено для одночасного парсингу (0–4).</summary>
    public int SelectedParseSitesCount =>
        (IsDouParseEnabled ? 1 : 0)
        + (IsWorkUaParseEnabled ? 1 : 0)
        + (IsDjinniParseEnabled ? 1 : 0)
        + (IsRobotaUaParseEnabled ? 1 : 0);

    /// <summary>Текст головної кнопки запуску парсингу залежно від кількості обраних сайтів.</summary>
    public string FetchButtonText => SelectedParseSitesCount switch
    {
        0 => "Спарсити (0)",
        1 => "Спарсити",
        _ => $"Спарсити ({SelectedParseSitesCount})"
    };

    /// <summary>Короткий перелік сайтів та їхньої глибини, що будуть спарсені при запуску.</summary>
    public string SelectedParseSitesSummary
    {
        get
        {
            List<string> enabled = [];
            if (IsDouParseEnabled) enabled.Add("DOU (25 найсвіжіших)");
            if (IsWorkUaParseEnabled) enabled.Add($"Work.ua ({PagesOf(WorkUaPagesToParse)} стор., з {FromPageOf(WorkUaFromPage)})");
            if (IsDjinniParseEnabled) enabled.Add($"Djinni ({PagesOf(DjinniPagesToParse)} стор., з {FromPageOf(DjinniFromPage)})");
            if (IsRobotaUaParseEnabled) enabled.Add($"Robota.ua ({PagesOf(RobotaUaPagesToParse)} стор., з {FromPageOf(RobotaUaFromPage)})");

            return enabled.Count == 0
                ? "Жоден сайт не обрано — увімкніть хоча б один сайт для парсингу."
                : $"Буде спарсено ({enabled.Count}): {string.Join(" · ", enabled)}";
        }
    }

    partial void OnSelectedPlatformChanged(string value)
    {
        if (!_hasExplicitSiteSelection)
        {
            _isSyncingPlatformFlags = true;
            try
            {
                IsDouParseEnabled = value == "DOU";
                IsWorkUaParseEnabled = value == "WorkUa";
                IsDjinniParseEnabled = value == "Djinni";
                IsRobotaUaParseEnabled = value == "RobotaUa";
            }
            finally
            {
                _isSyncingPlatformFlags = false;
            }
        }

        SyncSiteFromPageFromActivePlatform();
    }

    partial void OnIsDouParseEnabledChanged(bool value) => MarkExplicitSiteSelection();
    partial void OnIsWorkUaParseEnabledChanged(bool value) => MarkExplicitSiteSelection();
    partial void OnIsDjinniParseEnabledChanged(bool value) => MarkExplicitSiteSelection();
    partial void OnIsRobotaUaParseEnabledChanged(bool value) => MarkExplicitSiteSelection();

    private void MarkExplicitSiteSelection()
    {
        if (!_isSyncingPlatformFlags)
        {
            _hasExplicitSiteSelection = true;
        }
    }

    [RelayCommand]
    private void ToggleDouParse() => IsDouParseEnabled = !IsDouParseEnabled;

    [RelayCommand]
    private void ToggleWorkUaParse() => IsWorkUaParseEnabled = !IsWorkUaParseEnabled;

    [RelayCommand]
    private void ToggleDjinniParse() => IsDjinniParseEnabled = !IsDjinniParseEnabled;

    [RelayCommand]
    private void ToggleRobotaUaParse() => IsRobotaUaParseEnabled = !IsRobotaUaParseEnabled;

    [RelayCommand]
    private void SelectAllParseSites()
    {
        _hasExplicitSiteSelection = true;
        IsDouParseEnabled = true;
        IsWorkUaParseEnabled = true;
        IsDjinniParseEnabled = true;
        IsRobotaUaParseEnabled = true;
    }

    [RelayCommand]
    private void ClearParseSites()
    {
        _hasExplicitSiteSelection = true;
        IsDouParseEnabled = false;
        IsWorkUaParseEnabled = false;
        IsDjinniParseEnabled = false;
        IsRobotaUaParseEnabled = false;
    }

    /// <summary>Індивідуальна стартова сторінка Work.ua (1–50).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial string WorkUaFromPage { get; set; } = "1";

    /// <summary>Індивідуальна стартова сторінка Djinni (1–50).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial string DjinniFromPage { get; set; } = "1";

    /// <summary>Індивідуальна стартова сторінка Robota.ua (1–50).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial string RobotaUaFromPage { get; set; } = "1";

    /// <summary>Індивідуальні ключові слова пошуку на Robota.ua.</summary>
    [ObservableProperty]
    public partial string RobotaUaSearchQuery { get; set; } = string.Empty;

    partial void OnWorkUaFromPageChanged(string value)
    {
        if (!_isSyncingFromPage && SelectedPlatform == "WorkUa")
        {
            SetSiteFromPageSilently(value);
        }
    }

    partial void OnDjinniFromPageChanged(string value)
    {
        if (!_isSyncingFromPage && SelectedPlatform == "Djinni")
        {
            SetSiteFromPageSilently(value);
        }
    }

    partial void OnRobotaUaFromPageChanged(string value)
    {
        if (!_isSyncingFromPage && SelectedPlatform == "RobotaUa")
        {
            SetSiteFromPageSilently(value);
        }
    }

    private void SyncSiteFromPageFromActivePlatform()
    {
        string activeFrom = SelectedPlatform switch
        {
            "WorkUa" => WorkUaFromPage,
            "Djinni" => DjinniFromPage,
            "RobotaUa" => RobotaUaFromPage,
            _ => "1"
        };
        SetSiteFromPageSilently(activeFrom);
    }

    private void SetSiteFromPageSilently(string value)
    {
        _isSyncingFromPage = true;
        try
        {
            SiteFromPage = value;
        }
        finally
        {
            _isSyncingFromPage = false;
        }
    }

    public string FilterToggleText => IsFilterPanelVisible ? "Сховати фільтри ▲" : "Фільтри платформ ⚙";

    // Стрічка DOU одна (25 найсвіжіших), тож кількості сторінок у неї немає
    public string DouButtonText => $"DOU (25 найсвіжіших) {(IsDouPanelVisible ? "▲" : "▼")}";
    public string WorkUaButtonText => $"Work.ua ({WorkUaPagesToParse} стор.) {(IsWorkUaPanelVisible ? "▲" : "▼")}";
    public string DjinniButtonText => $"Djinni ({DjinniPagesToParse} стор.) {(IsDjinniPanelVisible ? "▲" : "▼")}";
    public string RobotaUaButtonText => $"Robota.ua ({RobotaUaPagesToParse} стор.) {(IsRobotaUaPanelVisible ? "▲" : "▼")}";

    public string ActivePlatformTitle => SelectedPlatform switch
    {
        "DOU" => "Фільтри DOU (jobs.dou.ua)",
        "WorkUa" => "Фільтри Work.ua (work.ua)",
        "Djinni" => "Фільтри Djinni (djinni.co)",
        "RobotaUa" => "Фільтри Robota.ua (robota.ua)",
        _ => "Фільтри платформи"
    };

    public string ActivePlatformSubtitle => SelectedPlatform switch
    {
        "DOU" => "Усі автентичні параметри пошуку jobs.dou.ua/vacancies та налаштування глибини парсингу",
        "WorkUa" => "Усі автентичні параметри пошуку work.ua/jobs та налаштування глибини парсингу",
        "Djinni" => "Усі автентичні параметри пошуку djinni.co/jobs та налаштування глибини парсингу",
        "RobotaUa" => "Параметри пошуку robota.ua/zapros та налаштування глибини парсингу",
        _ => string.Empty
    };

    /// <summary>
    /// Ключові слова для пошуку на будь-якому сайті блоку 1 (python, бухгалтер…). Якщо порожньо, береться поле
    /// ключових слів самої платформи, якщо воно є.
    /// </summary>
    [ObservableProperty]
    public partial string SiteKeywords { get; set; } = string.Empty;

    // ==================== 4. ФІЛЬТРИ ROBOTA.UA (robota.ua) ====================

    /// <summary>Сторінок списку Robota.ua: кожну вакансію сервер відкриває окремо, тож одна сторінка — близько хвилини.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RobotaUaButtonText))]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial string RobotaUaPagesToParse { get; set; } = "1";

    [ObservableProperty]
    public partial string RobotaUaSelectedCity { get; set; } = "Вся Україна";

    [ObservableProperty]
    public partial string RobotaUaSelectedEmployment { get; set; } = "Будь-яка зайнятість";

    [ObservableProperty]
    public partial string RobotaUaSelectedSalary { get; set; } = "Будь-яка зарплата";

    [ObservableProperty]
    public partial string RobotaUaSelectedExperience { get; set; } = "Будь-який досвід";

    /// <summary>Лишати вакансії без зарплати, коли обрано «Зарплата від» (інакше сайту передаємо salaryType=false).</summary>
    [ObservableProperty]
    public partial bool RobotaUaIncludeNoSalary { get; set; }

    public IReadOnlyList<string> RobotaUaCityOptions { get; } = ["Вся Україна", .. RobotaUaCities.Select(city => city.Name)];

    public IReadOnlyList<string> RobotaUaEmploymentOptions { get; } =
    [
        "Будь-яка зайнятість",
        "Повна зайнятість (scheduleIds=1)",
        "Неповна зайнятість (scheduleIds=2)",
        "Віддалена робота (scheduleIds=3)"
    ];

    public IReadOnlyList<string> RobotaUaSalaryOptions { get; } =
    [
        "Будь-яка зарплата",
        "Від 10 000 грн (salary=10000)",
        "Від 20 000 грн (salary=20000)",
        "Від 30 000 грн (salary=30000)",
        "Від 40 000 грн (salary=40000)",
        "Від 60 000 грн (salary=60000)",
        "Від 80 000 грн (salary=80000)",
        "Від 100 000 грн (salary=100000)",
        "Від 120 000 грн (salary=120000)"
    ];

    public IReadOnlyList<string> RobotaUaExperienceOptions { get; } =
    [
        "Будь-який досвід",
        "Без досвіду (experienceType=true)"
    ];

    // ==================== 1. ФІЛЬТРИ DOU (jobs.dou.ua) ====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DouButtonText))]
    public partial string DouPagesToParse { get; set; } = "1";

    [ObservableProperty]
    public partial string DouSearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DouSearchInDescription { get; set; }

    [ObservableProperty]
    public partial string DouSelectedCategory { get; set; } = "Всі категорії";

    [ObservableProperty]
    public partial string DouSelectedExperience { get; set; } = "Будь-який досвід";

    [ObservableProperty]
    public partial string DouSelectedCity { get; set; } = "Всі міста";

    [ObservableProperty]
    public partial bool DouIsRemote { get; set; }

    [ObservableProperty]
    public partial bool DouIsRelocation { get; set; }

    [ObservableProperty]
    public partial bool DouIsFirstJob { get; set; }

    [ObservableProperty]
    public partial bool DouIsGovTech { get; set; }

    [ObservableProperty]
    public partial bool DouIsMilTech { get; set; }

    [ObservableProperty]
    public partial bool DouIsDefenseForces { get; set; }

    [ObservableProperty]
    public partial bool DouHasReservation { get; set; }

    public IReadOnlyList<string> DouCategoryOptions { get; } =
    [
        "Всі категорії",
        ".NET",
        "Account Manager",
        "AI/ML",
        "Analyst",
        "Android",
        "Animator",
        "Architect",
        "Artist",
        "Assistant",
        "Big Data",
        "Blockchain",
        "C++",
        "C-level",
        "Copywriter",
        "Data Engineer",
        "Data Science",
        "DBA",
        "Design",
        "DevOps",
        "Embedded",
        "Engineering Manager",
        "Erlang",
        "ERP/CRM",
        "Finance",
        "Flutter",
        "Front End",
        "Golang",
        "Hardware",
        "HR",
        "iOS/macOS",
        "Java",
        "Legal",
        "Marketing",
        "No-code",
        "Node.js",
        "Office Manager",
        "Other",
        "PHP",
        "Procurement",
        "Product Manager",
        "Project Manager",
        "Python",
        "QA",
        "React Native",
        "Ruby",
        "Rust",
        "Sales",
        "Salesforce",
        "SAP",
        "Scala",
        "Scrum Master",
        "Security",
        "SEO",
        "Support",
        "SysAdmin",
        "Technical Writer",
        "Unity",
        "Unreal Engine",
        "Військова справа"
    ];

    public IReadOnlyList<string> DouExperienceOptions { get; } =
    [
        "Будь-який досвід",
        "Без досвіду (First Job)",
        "< 1 року (exp=0-1)",
        "1…3 роки (exp=1-3)",
        "3…5 років (exp=3-5)",
        "5+ років (exp=5plus)"
    ];

    public IReadOnlyList<string> DouCityOptions { get; } =
    [
        "Всі міста",
        "Київ",
        "Львів",
        "Дніпро",
        "Івано-Франківськ",
        "Одеса",
        "Вінниця",
        "Тернопіль",
        "Харків",
        "Чернівці",
        "Ужгород",
        "Хмельницький",
        "Рівне",
        "Полтава",
        "Черкаси",
        "Краматорськ",
        "Луцьк",
        "Житомир",
        "Запоріжжя",
        "Моршин",
        "Чернігів",
        "Кременчук",
        "Суми",
        "Біла Церква",
        "Кривий Ріг",
        "Миколаїв"
    ];

    // ==================== 2. ФІЛЬТРИ WORK.UA (work.ua) ====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkUaButtonText))]
    [NotifyPropertyChangedFor(nameof(WorkUaJobPagesHint))]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial string WorkUaPagesToParse { get; set; } = "3";

    /// <summary>
    /// Відкривати сторінку кожної вакансії Work.ua: повний опис, навички, досвід і графік, але близько хвилини
    /// на сторінку списку. Вимкнено — лише картки списку (секунди, опис 1–2 рядки).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkUaJobPagesHint))]
    public partial bool WorkUaOpenJobPages { get; set; } = false;

    /// <summary>Попередження під перемикачем: скільки чекати і що втрачаємо без сторінок вакансій.</summary>
    public string WorkUaJobPagesHint => WorkUaOpenJobPages
        ? $"Повний опис і навички, але пошук довгий: близько {PagesOf(WorkUaPagesToParse)} хв (≈1 хв на сторінку з 14 вакансій)."
        : "Швидко, опис — 1–2 рядки без навичок, досвіду й графіка.";

    [ObservableProperty]
    public partial string WorkUaSearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WorkUaSelectedWordMatch { get; set; } = "З синонімами (за замовчуванням)";

    [ObservableProperty]
    public partial bool WorkUaTitleOnly { get; set; }

    [ObservableProperty]
    public partial string WorkUaSelectedCity { get; set; } = "Вся Україна";

    [ObservableProperty]
    public partial string WorkUaSalaryFrom { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WorkUaSalaryTo { get; set; } = string.Empty;

    /// <summary>З межею зарплати Work.ua прибирає вакансії без неї; увімкнено — сайт лишає й їх (nosalary=1).</summary>
    [ObservableProperty]
    public partial bool WorkUaIncludeNoSalary { get; set; }

    [ObservableProperty]
    public partial string WorkUaSelectedExperience { get; set; } = "Будь-який досвід";

    [ObservableProperty]
    public partial string WorkUaSelectedDays { get; set; } = "За 30 днів (за замовчуванням)";

    [ObservableProperty]
    public partial string WorkUaSelectedSort { get; set; } = "За датою (спочатку нові)";

    [ObservableProperty]
    public partial string WorkUaAgeFrom { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WorkUaAgeTo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool WorkUaForStudents { get; set; }

    [ObservableProperty]
    public partial bool WorkUaForPersonsWithDisabilities { get; set; }

    [ObservableProperty]
    public partial bool WorkUaForRetirees { get; set; }

    [ObservableProperty]
    public partial bool WorkUaForVeterans { get; set; }

    [ObservableProperty]
    public partial bool WorkUaHasReservation { get; set; }

    [ObservableProperty]
    public partial bool WorkUaWithoutResume { get; set; }

    public IReadOnlyList<string> WorkUaWordMatchOptions { get; } =
    [
        "З синонімами (за замовчуванням)",
        "Будь-яке зі слів (anyword=1)",
        "Без синонімів (nosynonym=1)"
    ];

    // Сфери, мови й вид зайнятості Work.ua можна обирати разом: сайт приймає кілька кодів через «+»
    // (category=1+9, language=1+41, employment=74+75). Коди звірено з сайтом 15.11.2026.
    public IReadOnlyList<SelectableOption> WorkUaCategoryChoices { get; } = [.. WorkUaCategoryCodes.Select(c => new SelectableOption(c.Name, "category", c.Code))];

    public IReadOnlyList<SelectableOption> WorkUaEmploymentChoices { get; } =
    [
        new("Повна зайнятість", "employment", "74"),
        new("Неповна зайнятість", "employment", "75"),
        new("Гібридний формат", "hybrid", "1")
    ];

    public IReadOnlyList<SelectableOption> WorkUaLanguageChoices { get; } =
    [
        new("Англійська", "language", "1"),
        new("Українська", "language", "41"),
        new("Німецька", "language", "2"),
        new("Польська", "language", "3"),
        new("Іспанська", "language", "4"),
        new("Французька", "language", "5")
    ];

    [RelayCommand]
    private void SelectAllChoices(IEnumerable<SelectableOption>? choices)
    {
        foreach (SelectableOption choice in choices ?? [])
        {
            choice.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearChoices(IEnumerable<SelectableOption>? choices)
    {
        foreach (SelectableOption choice in choices ?? [])
        {
            choice.IsSelected = false;
        }
    }

    public IReadOnlyList<string> WorkUaCityOptions { get; } =
    [
        "Вся Україна",
        "Дистанційно",
        "Київ",
        "Львів",
        "Дніпро",
        "Одеса",
        "Харків",
        "Вінниця",
        "Запоріжжя",
        "Івано-Франківськ",
        "Тернопіль",
        "Полтава",
        "Рівне",
        "Хмельницький",
        "Черкаси",
        "Чернівці",
        "Житомир",
        "Ужгород",
        "Луцьк",
        "Миколаїв",
        "Суми",
        "Чернігів",
        "Кропивницький",
        "Інші країни"
    ];

    public IReadOnlyList<string> WorkUaExperienceOptions { get; } =
    [
        "Будь-який досвід",
        "Без досвіду (experience=1)"
    ];

    public IReadOnlyList<string> WorkUaDaysOptions { get; } =
    [
        "За 30 днів (за замовчуванням)",
        "За 14 днів (days=124)",
        "За 7 днів (days=123)",
        "За 1 день (days=122)"
    ];

    public IReadOnlyList<string> WorkUaSortOptions { get; } =
    [
        "За датою (спочатку нові)",
        "За датою (спочатку старі)",
        "За зарплатою (від найвищої ↓)",
        "За зарплатою (від найнижчої ↑)",
        "За відстанню (спочатку ближчі)",
        "За відстанню (спочатку дальші)",
        "За назвою посади (А → Я)",
        "За назвою посади (Я → А)",
        "За компанією (А → Я)"
    ];

    // ==================== 3. ФІЛЬТРИ DJINNI (djinni.co) ====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DjinniButtonText))]
    [NotifyPropertyChangedFor(nameof(SelectedParseSitesSummary))]
    public partial string DjinniPagesToParse { get; set; } = "3";

    [ObservableProperty]
    public partial string DjinniAllKeywords { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DjinniAnyOfKeywords { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DjinniExcludeKeywords { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DjinniSelectedSearchType { get; set; } = "Звичайний пошук (basic-search)";

    [ObservableProperty]
    public partial string DjinniCompanyFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DjinniSelectedCategory { get; set; } = "Усі категорії";

    [ObservableProperty]
    public partial string DjinniSelectedSalary { get; set; } = "Будь-яка ($0+)";

    [ObservableProperty]
    public partial string DjinniCustomMinSalary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DjinniSelectedExperience { get; set; } = "Будь-який досвід";

    [ObservableProperty]
    public partial string DjinniSelectedEmployment { get; set; } = "Будь-яка зайнятість";

    [ObservableProperty]
    public partial string DjinniSelectedEnglish { get; set; } = "Будь-який рівень";

    [ObservableProperty]
    public partial string DjinniSelectedCompanyType { get; set; } = "Усі типи компаній";

    [ObservableProperty]
    public partial string DjinniSelectedDomain { get; set; } = "Усі домени";

    [ObservableProperty]
    public partial string DjinniSelectedRegion { get; set; } = "Усі регіони";

    [ObservableProperty]
    public partial string DjinniSelectedCity { get; set; } = "Усі міста";

    [ObservableProperty]
    public partial string DjinniSelectedCountry { get; set; } = "Усі країни";

    [ObservableProperty]
    public partial bool DjinniHasPublicSalary { get; set; }

    [ObservableProperty]
    public partial bool DjinniUkrainianProduct { get; set; }

    [ObservableProperty]
    public partial bool DjinniMilTech { get; set; }

    [ObservableProperty]
    public partial bool DjinniHasReservation { get; set; }

    [ObservableProperty]
    public partial bool DjinniHideStale { get; set; }

    public IReadOnlyList<string> DjinniSearchTypeOptions { get; } =
    [
        "Звичайний пошук (basic-search)",
        "У всьому тексті (full-text)",
        "Тільки в заголовку посади (title-only)"
    ];

    public IReadOnlyList<string> DjinniCategoryOptions { get; } =
    [
        "Усі категорії",
        "JavaScript / Front-End",
        "React.js",
        "Angular",
        "Vue.js",
        "Svelte",
        "Markup",
        "Fullstack",
        "C# / .NET",
        "ASP.NET",
        "Blazor",
        "MAUI / Xamarin",
        "WPF / WinForms",
        ".NET Gamedev",
        "Java",
        "Python",
        "PHP (Laravel / Symfony / WordPress)",
        "Node.js",
        "iOS",
        "Android",
        "React Native",
        "Flutter",
        "C / C++ / Embedded",
        "Golang",
        "Ruby",
        "Scala",
        "Rust",
        "Elixir",
        "Kotlin",
        "Salesforce",
        "ERP Systems (SAP / Odoo / MS Dynamics)",
        "No Code",
        "QA Manual",
        "QA Automation",
        "QA Hardware / Embedded",
        "Design (UI/UX, Product, Graphic)",
        "2D/3D Artist / Illustrator / Motion",
        "Gamedev (Unity / Unreal / Game Design)",
        "Business Analyst",
        "Data Science",
        "ML / AI",
        "Data Analyst / Web Analyst",
        "Data Engineer",
        "Project Manager",
        "Product Manager",
        "Delivery Manager",
        "Scrum Master",
        "Engineering Manager",
        "C-Level (CTO / CEO / CPO / CMO / COO)",
        "DevOps",
        "Security / Penetration Tester",
        "Sysadmin",
        "DefTech (UAV / EW / C4ISR / Weapons)",
        "Hardware / Firmware / FPGA / Robotics",
        "Engineering / CAD Design",
        "Marketing / PPC / Media Buying / SMM",
        "Sales / Business Development / Account Manager",
        "Lead Generation",
        "SEO",
        "HR / Recruiter",
        "Technical Writing",
        "Customer / Technical Support",
        "Finances / FinOps / Accountant",
        "(Other)"
    ];

    public IReadOnlyList<string> DjinniSalaryOptions { get; } =
    [
        "Будь-яка ($0+)",
        "від $500",
        "від $1000",
        "від $1500",
        "від $2000",
        "від $2500",
        "від $3000",
        "від $3500",
        "від $4000",
        "від $4500",
        "від $5000",
        "від $6000",
        "від $7000",
        "від $8000",
        "від $9000",
        "від $10000"
    ];

    public IReadOnlyList<string> DjinniExperienceOptions { get; } =
    [
        "Будь-який досвід",
        "Без досвіду (no_exp)",
        "1 рік (1y)",
        "2 роки (2y)",
        "3 роки (3y)",
        "4 роки (4y)",
        "5 років (5y)",
        "6 років (6y)",
        "7 років (7y)",
        "8 років (8y)",
        "9 років (9y)",
        "10+ років (10y)"
    ];

    public IReadOnlyList<string> DjinniEmploymentOptions { get; } =
    [
        "Будь-яка зайнятість",
        "Повна віддалена (employment=remote)",
        "Робота в офісі / гібрид (employment=office)",
        "Часткова зайнятість (employment=parttime)"
    ];

    public IReadOnlyList<string> DjinniEnglishOptions { get; } =
    [
        "Будь-який рівень",
        "Без англійської (english_level=no_english)",
        "Beginner / Elementary (english_level=basic)",
        "Pre-Intermediate (english_level=pre)",
        "Intermediate (english_level=intermediate)",
        "Upper-Intermediate (english_level=upper)",
        "Advanced / Fluent (english_level=fluent)",
        "Proficient (english_level=proficient)",
        "Native speaker (english_level=native)"
    ];

    public IReadOnlyList<string> DjinniCompanyTypeOptions { get; } =
    [
        "Усі типи компаній",
        "Продуктова (company_type=product)",
        "Сервісна / аутсорс (company_type=outsource)",
        "Аутстаф (company_type=outstaff)",
        "Стартап (company_type=startup)",
        "Змішана (company_type=mixed)",
        "Не ІТ-компанія, ІТ-відділ (company_type=inhouse_it)",
        "Агенція (company_type=agency)",
        "Державна / громадська (company_type=government)"
    ];

    public IReadOnlyList<string> DjinniDomainOptions { get; } =
    [
        "Усі домени",
        "FinTech (domain=fintech)",
        "E-commerce / Marketplace (domain=ecommerce)",
        "SaaS (domain=saas)",
        "DefTech (domain=miltech)",
        "Machine Learning / Big Data (domain=ml)",
        "GameDev (domain=gamedev)",
        "Healthcare / MedTech (domain=healthcare)",
        "Education (domain=edutech)",
        "Blockchain / Crypto (domain=crypto)",
        "Security (domain=security)",
        "Mobile (domain=mobile)",
        "Automotive (domain=automotive)",
        "Telecom (domain=telecom)",
        "Logistics (domain=logistics)",
        "Media (domain=media)",
        "Advertising / Marketing (domain=advertising)",
        "GovTech (domain=govtech)",
        "LegalTech (domain=legaltech)",
        "PropTech (domain=proptech)",
        "HRTech (domain=hrtech)",
        "Travel / Tourism (domain=travel)",
        "Energy / Utilities (domain=energy)",
        "Agriculture / Food Tech (domain=agriculture)",
        "Hardware / IoT (domain=hardware)",
        "Gambling (domain=gambling)",
        "Dating (domain=dating)",
        "Adult (domain=adult)",
        "Sports and Fitness (domain=sports_fitness)",
        "Manufacturing (domain=manufacturing)",
        "Entertainment / Streaming (domain=entertainment)",
        "Other (domain=other)"
    ];

    public IReadOnlyList<string> DjinniRegionOptions { get; } =
    [
        "Усі регіони",
        "Весь світ (region=worldwide)",
        "Україна (region=UKR)",
        "Країни ЄС (region=eu)",
        "Інші країни (region=other)"
    ];

    public IReadOnlyList<string> DjinniCityOptions { get; } =
    [
        "Усі міста",
        "Київ (kyiv)",
        "Львів (lviv)",
        "Дніпро (dnipro)",
        "Одеса (odesa)",
        "Харків (kharkiv)",
        "Вінниця (vinnytsia)",
        "Івано-Франківськ (ivano-frankivsk)",
        "Тернопіль (ternopil)",
        "Ужгород (uzhhorod)",
        "Чернівці (chernivtsi)",
        "Хмельницький (khmelnytskyi)",
        "Полтава (poltava)",
        "Рівне (rivne)",
        "Житомир (zhytomyr)",
        "Запоріжжя (zaporizhzhia)",
        "Луцьк (lutsk)",
        "Черкаси (cherkasy)",
        "Суми (sumy)",
        "Чернігів (chernihiv)",
        "Миколаїв (mykolaiv)",
        "Кропивницький (kropyvnytskyi)",
        "Херсон (kherson)"
    ];

    public IReadOnlyList<string> DjinniCountryOptions { get; } =
    [
        "Усі країни",
        "Україна (country=UKR)",
        "Польща (country=POL)",
        "Кіпр (country=CYP)",
        "Румунія (country=ROU)",
        "Іспанія (country=ESP)",
        "Чехія (country=CZE)",
        "Словаччина (country=SVK)",
        "Грузія (country=GEO)",
        "Вірменія (country=ARM)",
        "Португалія (country=PRT)",
        "Молдова (country=MDA)",
        "США (country=USA)",
        "Німеччина (country=DEU)",
        "Болгарія (country=BGR)",
        "Литва (country=LTU)",
        "Канада (country=CAN)"
    ];

    // ==================== 4. ВАЙТ-ЛИСТ СЛІВ, БАН-СЛОВА ТА СОРТУВАННЯ ПО БД ====================

    public ObservableCollection<string> WhiteListWords { get; } = [];

    public ObservableCollection<string> BanListWords { get; } = [];

    [ObservableProperty]
    public partial string NewWhiteListWord { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewBanListWord { get; set; } = string.Empty;

    public bool HasWhiteListWords => WhiteListWords.Count > 0;
    public bool IsWhiteListEmpty => WhiteListWords.Count == 0;

    public bool HasBanListWords => BanListWords.Count > 0;
    public bool IsBanListEmpty => BanListWords.Count == 0;

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "За датою (спочатку нові)",
        "За датою (спочатку старі)",
        "За зарплатою (від найвищої ↓)",
        "За зарплатою (від найнижчої ↑)",
        "За відстанню (спочатку ближчі)",
        "За відстанню (спочатку дальші)",
        "За назвою посади (А → Я)",
        "За назвою посади (Я → А)",
        "За компанією (А → Я)"
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomSortActive))]
    public partial string SelectedSortOption { get; set; } = "За датою (спочатку нові)";

    public bool IsCustomSortActive => !string.Equals(SelectedSortOption, SortOptions[0], StringComparison.Ordinal);

    public bool HasActiveLocalFilters =>
        SelectedSourceFilter != SourceFilterOptions[0]
        || SelectedFormat != FormatOptions[0]
        || WhiteListWords.Count > 0
        || BanListWords.Count > 0
        || !string.IsNullOrWhiteSpace(UnifiedCompanyFilter)
        || !string.IsNullOrWhiteSpace(MinSalaryFilter)
        || !string.IsNullOrWhiteSpace(MaxSalaryFilter)
        || UnifiedOnlyWithSalary
        || UnifiedOnlyActive
        || UnifiedHideViewed
        || UnifiedIncludeNoSalary
        || UnifiedTitleOnly;

    public bool IsFilterHighlightActive => IsLocalFilterPanelVisible || HasActiveLocalFilters;

    public IReadOnlyList<string> SourceFilterOptions { get; } = ["Усі платформи", "DOU", "Work.ua", "Djinni", "Robota.ua"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SelectedSourceFilter { get; set; } = "Усі платформи";

    public IReadOnlyList<string> SalaryCurrencyOptions { get; } = ["$ USD", "₴ ГРН"];

    [ObservableProperty]
    public partial string SelectedSalaryCurrency { get; set; } = "$ USD";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string UnifiedCompanyFilter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool UnifiedTitleOnly { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool UnifiedOnlyWithSalary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool UnifiedOnlyActive { get; set; }

    /// <summary>Сховати вакансії, які користувач уже переглядав (розгортав чи відкривав на сайті).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool UnifiedHideViewed { get; set; }

    /// <summary>Із межами «Зарплата від/до» лишати й вакансії без вказаної зарплати.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool UnifiedIncludeNoSalary { get; set; }

    /// <summary>Вайт-лист: true — вакансія має містити всі слова, false — хоча б одне.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhiteListMatchText))]
    public partial bool WhiteListMatchAll { get; set; }

    public string WhiteListMatchText => WhiteListMatchAll ? "Усі слова" : "Будь-яке слово";

    [ObservableProperty]
    public partial bool UnifiedIsRemote { get; set; }

    // Загальні властивості сумісності
    [ObservableProperty]
    public partial string SkillFilter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SelectedFormat { get; set; } = "Усі формати";

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "Усі категорії";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string MinSalaryFilter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveLocalFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string MaxSalaryFilter { get; set; } = string.Empty;

    public IReadOnlyList<string> FormatOptions { get; } = ["Усі формати", "Віддалено", "Офіс", "Гібрид"];

    public IReadOnlyList<string> CategoryOptions { get; } =
    [
        "Усі категорії",
        ".NET",
        "QA",
        "Mobile",
        "DevOps",
        "Full Stack",
        "Frontend",
        "Backend",
        "Data / AI"
    ];

    /// <summary>
    /// Інформаційний банер про застарілий кеш (REL-001).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStaleCache))]
    public partial string? StaleCacheText { get; set; }

    public bool HasStaleCache => !string.IsNullOrEmpty(StaleCacheText);

    /// <summary>Іде пошук на сайті: показуємо шкалу прогресу й блокуємо повторний запуск.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchFromSitesCommand))]
    public partial bool IsSiteSearchRunning { get; set; }

    /// <summary>Частка прочитаних сторінок сайту, 0…1.</summary>
    [ObservableProperty]
    public partial double SiteSearchProgress { get; set; }

    /// <summary>Підпис під шкалою: що саме зараз робить пошук.</summary>
    [ObservableProperty]
    public partial string? SiteSearchStatusText { get; set; }

    /// <summary>
    /// Хоча б один пошук на сайті завершився успішно: до того каталог порожній.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowTotal))]
    [NotifyPropertyChangedFor(nameof(IsBeforeFirstSearch))]
    public partial bool HasLoaded { get; private set; }

    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Пошуку ще не було: замість списку — підказка обрати сайт і фільтри та натиснути «Знайти».
    /// </summary>
    public bool IsBeforeFirstSearch => !HasLoaded;

    /// <summary>
    /// Пошук відбувся, але на екрані вакансій немає: сайт нічого не знайшов або все відсіяли фільтри.
    /// </summary>
    public bool IsEmpty => HasLoaded && ErrorMessage is null && Vacancies.Count == 0;

    public string TotalText => $"Знайдено вакансій: {TotalCount}";

    /// <summary>
    /// Лічильник показується лише для актуального результату (не поруч із помилкою).
    /// </summary>
    public bool ShowTotal => HasLoaded && !HasError;

    [RelayCommand]
    private void ToggleFilterPanel() => IsFilterPanelVisible = !IsFilterPanelVisible;

    [RelayCommand]
    private void ToggleParsePanel() => IsParsePanelVisible = !IsParsePanelVisible;

    [RelayCommand]
    private void ToggleLocalFilterPanel() => IsLocalFilterPanelVisible = !IsLocalFilterPanelVisible;

    [RelayCommand]
    private void SelectPlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return;
        }

        if (IsFilterPanelVisible && SelectedPlatform == platform)
        {
            IsFilterPanelVisible = false;
        }
        else
        {
            SelectedPlatform = platform;
            IsFilterPanelVisible = true;
        }
    }

    [RelayCommand]
    private void SelectDouFilter() => SelectPlatform("DOU");

    [RelayCommand]
    private void SelectWorkUaFilter() => SelectPlatform("WorkUa");

    [RelayCommand]
    private void SelectDjinniFilter() => SelectPlatform("Djinni");

    [RelayCommand]
    private void SelectRobotaUaFilter() => SelectPlatform("RobotaUa");

    [RelayCommand]
    private void SetDouPages(string? pages)
    {
        if (!string.IsNullOrWhiteSpace(pages))
        {
            DouPagesToParse = pages;
        }
    }

    [RelayCommand]
    private void SetWorkUaPages(string? pages)
    {
        if (!string.IsNullOrWhiteSpace(pages))
        {
            WorkUaPagesToParse = pages;
        }
    }

    [RelayCommand]
    private void SetDjinniPages(string? pages)
    {
        if (!string.IsNullOrWhiteSpace(pages))
        {
            DjinniPagesToParse = pages;
        }
    }

    [RelayCommand]
    private Task AddWhiteListWordAsync()
    {
        if (VacancyFilterEngine.TryAddWords(NewWhiteListWord, WhiteListWords))
        {
            NewWhiteListWord = string.Empty;
            NotifyWordListProperties();
            if (HasLoaded)
            {
                return FilterExistingAsync();
            }
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RemoveWhiteListWordAsync(string? word)
    {
        if (!string.IsNullOrWhiteSpace(word) && WhiteListWords.Remove(word))
        {
            NotifyWordListProperties();
            if (HasLoaded)
            {
                return FilterExistingAsync();
            }
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ClearWhiteListWordsAsync()
    {
        if (WhiteListWords.Count > 0)
        {
            WhiteListWords.Clear();
            NotifyWordListProperties();
            if (HasLoaded)
            {
                return FilterExistingAsync();
            }
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task AddBanListWordAsync()
    {
        if (VacancyFilterEngine.TryAddWords(NewBanListWord, BanListWords))
        {
            NewBanListWord = string.Empty;
            NotifyWordListProperties();
            if (HasLoaded)
            {
                return FilterExistingAsync();
            }
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RemoveBanListWordAsync(string? word)
    {
        if (!string.IsNullOrWhiteSpace(word) && BanListWords.Remove(word))
        {
            NotifyWordListProperties();
            if (HasLoaded)
            {
                return FilterExistingAsync();
            }
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ClearBanListWordsAsync()
    {
        if (BanListWords.Count > 0)
        {
            BanListWords.Clear();
            NotifyWordListProperties();
            if (HasLoaded)
            {
                return FilterExistingAsync();
            }
        }

        return Task.CompletedTask;
    }

    private void NotifyWordListProperties()
    {
        OnPropertyChanged(nameof(HasWhiteListWords));
        OnPropertyChanged(nameof(IsWhiteListEmpty));
        OnPropertyChanged(nameof(HasBanListWords));
        OnPropertyChanged(nameof(IsBanListEmpty));
        OnPropertyChanged(nameof(HasActiveLocalFilters));
        OnPropertyChanged(nameof(IsFilterHighlightActive));
    }

    partial void OnSelectedSortOptionChanged(string value)
    {
        if (_isSyncingSort)
        {
            return;
        }

        _isSyncingSort = true;
        try
        {
            if (WorkUaSortOptions.Contains(value))
            {
                WorkUaSelectedSort = value;
            }
        }
        finally
        {
            _isSyncingSort = false;
        }

        if (HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    partial void OnWorkUaSelectedSortChanged(string value)
    {
        if (_isSyncingSort)
        {
            return;
        }

        _isSyncingSort = true;
        try
        {
            if (SortOptions.Contains(value))
            {
                SelectedSortOption = value;
            }
        }
        finally
        {
            _isSyncingSort = false;
        }

        if (HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    partial void OnSelectedSourceFilterChanged(string value)
    {
        if (!_isSyncingSort && HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    partial void OnSelectedFormatChanged(string value)
    {
        if (!_isSyncingSort && HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    [RelayCommand]
    private void ToggleUnifiedOnlyWithSalary()
    {
        UnifiedOnlyWithSalary = !UnifiedOnlyWithSalary;
        if (HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    [RelayCommand]
    private void ToggleUnifiedHideViewed()
    {
        UnifiedHideViewed = !UnifiedHideViewed;
        if (HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    [RelayCommand]
    private void ToggleUnifiedIncludeNoSalary()
    {
        UnifiedIncludeNoSalary = !UnifiedIncludeNoSalary;
        if (HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    [RelayCommand]
    private void ToggleWhiteListMatchAll()
    {
        WhiteListMatchAll = !WhiteListMatchAll;
        if (HasLoaded && WhiteListWords.Count > 0)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    [RelayCommand]
    private void ToggleUnifiedTitleOnly()
    {
        UnifiedTitleOnly = !UnifiedTitleOnly;
        if (HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    [RelayCommand]
    private void ToggleUnifiedIsRemote()
    {
        UnifiedIsRemote = !UnifiedIsRemote;
        if (HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    [RelayCommand]
    private void ToggleUnifiedOnlyActive()
    {
        UnifiedOnlyActive = !UnifiedOnlyActive;
        if (HasLoaded)
        {
            _ = ExecuteLocalFilterAndSortAsync(updateBanner: true);
        }
    }

    /// <summary>
    /// «Спарсити» у блоці «Запит на сайт»: сервер читає один або одночасно кілька (1–4) обраних сайтів
    /// із власними налаштуваннями для кожного, зберігає вакансії в каталог і повертає їх.
    /// Якщо сервер недоступний (офлайн), за наявності <see cref="IOfflineSiteCollector"/> збирає вакансії локально
    /// з подальшою синхронізацією.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFetchFromSites))]
    private async Task FetchFromSitesAsync()
    {
        List<SiteSearchRequest> requests;
        try
        {
            requests = BuildSiteSearchRequests();
        }
        catch (Exception ex)
        {
            StaleCacheText = $"Помилка запиту: {ex.Message}";
            return;
        }

        if (requests.Count == 0)
        {
            StaleCacheText = "Оберіть сайт: DOU, Work.ua, Djinni або Robota.ua.";
            return;
        }

        if (_siteSearchCts is not null)
        {
            await _siteSearchCts.CancelAsync();
        }

        CancellationTokenSource cts = new();
        _siteSearchCts = cts;
        IsSiteSearchRunning = true;
        SiteSearchProgress = 0;
        ErrorMessage = null;

        try
        {
            if (requests.Count == 1)
            {
                await RunSingleSiteSearchAsync(requests[0], cts.Token);
            }
            else
            {
                await RunMultiSiteSearchAsync(requests, cts.Token);
            }
        }
        catch (ApiException ex) when (!cts.IsCancellationRequested)
        {
            if (ex.StatusCode is null && await TryCollectOfflineAsync(requests, cts.Token))
            {
                return;
            }

            StaleCacheText = ex.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "Увійдіть в обліковий запис для парсингу."
                : ex.Message;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Запущено новий пошук або користувач пішов зі сторінки
        }
        catch (Exception ex)
        {
            StaleCacheText = $"Помилка парсингу: {ex.Message}";
        }
        finally
        {
            if (_siteSearchCts == cts)
            {
                IsSiteSearchRunning = false;
                SiteSearchStatusText = null;
            }
        }
    }

    private async Task RunSingleSiteSearchAsync(SiteSearchRequest request, CancellationToken ct)
    {
        string site = SiteDisplayName(request.Source);
        // Сервер відкриває кожну вакансію: Robota.ua завжди, Work.ua — якщо користувач не обрав швидкий пошук
        bool slow = request.Source == JobSource.RobotaUa || request.Source == JobSource.WorkUa && !request.ListOnly;
        SiteSearchStatusText = $"{site}: надсилаємо запит…";

        SiteSearchStatusDto status = await _gateway.StartSiteSearchAsync(request, ct);
        while (status.IsInProgress)
        {
            ShowSiteSearchProgress(site, slow, status);
            await Task.Delay(SiteSearchPollInterval, _time, ct);
            status = await _gateway.GetSiteSearchAsync(status.Id, ct);
        }

        // Результат з кешу приходить уже завершеним, але без вакансій: їх віддає окремий запит стану
        if (status.State == SiteSearchStatusDto.Completed && status.Vacancies is null)
        {
            status = await _gateway.GetSiteSearchAsync(status.Id, ct);
        }

        await ShowSiteSearchResultAsync(site, status);

        if (status.State == SiteSearchStatusDto.Completed)
        {
            await PersistFetchedLocallyAsync(status.Vacancies, isSynced: true);
            if (IsDistanceSort(SelectedSortOption))
            {
                await ExecuteLocalFilterAndSortAsync(updateBanner: false);
            }
        }
    }

    private async Task RunMultiSiteSearchAsync(List<SiteSearchRequest> requests, CancellationToken ct)
    {
        string names = string.Join(", ", requests.Select(r => SiteDisplayName(r.Source)));
        SiteSearchStatusText = $"{names}: парсинг {requests.Count} сайтів…";

        MultiSiteSearchStatusDto status = await _gateway.StartMultiSiteSearchAsync(
            new MultiSiteSearchRequest { Requests = requests }, ct);

        while (status.IsInProgress)
        {
            ShowMultiSiteSearchProgress(status);
            await Task.Delay(SiteSearchPollInterval, _time, ct);
            status = await _gateway.GetMultiSiteSearchAsync(status.Id, ct);
        }

        if (status.State == SiteSearchStatusDto.Completed && status.Vacancies is null)
        {
            status = await _gateway.GetMultiSiteSearchAsync(status.Id, ct);
        }

        await ShowMultiSiteSearchResultAsync(status);

        if (status.State == SiteSearchStatusDto.Completed)
        {
            await PersistFetchedLocallyAsync(status.Vacancies, isSynced: true);
            if (IsDistanceSort(SelectedSortOption))
            {
                await ExecuteLocalFilterAndSortAsync(updateBanner: false);
            }
        }
    }

    private async Task<bool> TryCollectOfflineAsync(IReadOnlyList<SiteSearchRequest> requests, CancellationToken ct)
    {
        if (_offlineCollector is null || !requests.Any(r => _offlineCollector.Supports(r.Source)))
        {
            return false;
        }

        try
        {
            SiteSearchStatusText = "Офлайн-збір вакансій…";
            IReadOnlyList<VacancyDto> offlineFound = await _offlineCollector.CollectAsync(requests, ct);
            List<VacancyDto> foundList = offlineFound.ToList();

            await ApplyFetchedVacanciesAsync(foundList);
            await PersistFetchedLocallyAsync(foundList, isSynced: false);

            string unsupportedNote = requests.Any(r => !_offlineCollector.Supports(r.Source))
                ? " (Work.ua / Robota.ua потребують браузер сервера)"
                : string.Empty;
            StaleCacheText = foundList.Count == 0
                ? $"Офлайн: нічого не знайдено{unsupportedNote}."
                : $"Офлайн-збір: зібрано {foundList.Count} вакансій{unsupportedNote}.";
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task PersistFetchedLocallyAsync(IReadOnlyList<VacancyDto>? vacancies, bool isSynced)
    {
        if (_localVacancyStore is null || vacancies is null || vacancies.Count == 0)
        {
            return;
        }

        try
        {
            int saved = await _localVacancyStore.SaveVacanciesAsync(vacancies, isSynced, CancellationToken.None);
            if (saved > 0)
            {
                _messenger.Send(new VacanciesUpdatedMessage(saved));
            }
        }
        catch
        {
            // Помилка локального кешування не зриває показ уже знайдених вакансій
        }
    }

    private bool CanFetchFromSites() => !IsSiteSearchRunning;

    private static string SiteDisplayName(JobSource source) => source switch
    {
        JobSource.Djinni => "Djinni",
        JobSource.WorkUa => "Work.ua",
        JobSource.RobotaUa => "Robota.ua",
        _ => "DOU"
    };

    private void ShowSiteSearchProgress(string site, bool slow, SiteSearchStatusDto status)
    {
        SiteSearchProgress = status.Progress;
        string note = slow ? " (повний збір, може тривати кілька хвилин)" : string.Empty;
        SiteSearchStatusText = status.State == SiteSearchStatusDto.Queued
            ? $"{site}: у черзі…"
            : $"{site}: сторінка {status.PagesDone} з {status.PagesTotal}, знайдено {status.Found}{note}";
    }

    private void ShowMultiSiteSearchProgress(MultiSiteSearchStatusDto status)
    {
        SiteSearchProgress = status.Progress;
        string perSite = string.Join(" · ", status.Sites.Select(s =>
        {
            string name = SiteDisplayName(s.Source);
            return s.State switch
            {
                SiteSearchStatusDto.Completed => $"{name}: ✓ {s.Found}",
                SiteSearchStatusDto.Failed => $"{name}: ✕",
                SiteSearchStatusDto.Queued => $"{name}: черга",
                _ => $"{name}: {s.PagesDone}/{s.PagesTotal} ({s.Found})"
            };
        }));
        SiteSearchStatusText = $"Парсинг ({status.Sites.Count}): {status.PagesDone}/{status.PagesTotal}, знайдено {status.Found} — {perSite}";
    }

    private async Task ShowSiteSearchResultAsync(string site, SiteSearchStatusDto status)
    {
        SiteSearchProgress = 1;
        if (status.State != SiteSearchStatusDto.Completed)
        {
            StaleCacheText = $"{site}: {status.Message ?? "пошук не вдався. Спробуйте пізніше."}";
            return;
        }

        List<VacancyDto> found = status.Vacancies ?? [];
        await ApplyFetchedVacanciesAsync(found);

        string cache = status.FromCache ? " (кеш, протягом 3 годин)" : string.Empty;
        string shown = Vacancies.Count < found.Count ? $", за фільтрами списку показано {Vacancies.Count}" : string.Empty;
        StaleCacheText = found.Count == 0
            ? $"{site}: {status.Message ?? "нічого не знайдено."}"
            : $"{site}: знайдено {found.Count} вакансій{cache}{shown}";
    }

    private async Task ShowMultiSiteSearchResultAsync(MultiSiteSearchStatusDto status)
    {
        SiteSearchProgress = 1;
        if (status.State != SiteSearchStatusDto.Completed)
        {
            StaleCacheText = $"Парсинг: {status.Message ?? "пошук не вдався. Спробуйте пізніше."}";
            return;
        }

        List<VacancyDto> found = status.Vacancies ?? [];
        await ApplyFetchedVacanciesAsync(found);

        string breakdown = string.Join(", ", status.Sites.Select(s =>
            s.State == SiteSearchStatusDto.Failed
                ? $"{SiteDisplayName(s.Source)}: помилка"
                : $"{SiteDisplayName(s.Source)}: {s.Found}"));
        string cache = status.AllFromCache ? " (з кешу)" : string.Empty;
        string shown = Vacancies.Count < found.Count ? $", за фільтрами списку показано {Vacancies.Count}" : string.Empty;
        string warning = !string.IsNullOrWhiteSpace(status.Message) && found.Count > 0 ? $" · {status.Message}" : string.Empty;

        StaleCacheText = found.Count == 0
            ? $"Обрані сайти ({breakdown}): {status.Message ?? "нічого не знайдено."}"
            : $"Спарсено {status.Sites.Count} сайти ({breakdown}): {found.Count} вакансій{cache}{shown}{warning}";
    }

    private async Task ApplyFetchedVacanciesAsync(List<VacancyDto> found)
    {
        _loadedDtos.Clear();
        _loadedDtos.AddRange(found);

        int generation = ++_fetchGeneration;

        // Локальна фільтрація/сортування лишаються на UI-потоці (дешеві), а створення карток
        // (по одному VM на вакансію) виконується поза UI-потоком.
        List<VacancyDto> toCreate = _isLocalFilterActive || HasActiveLocalFilterOrSort()
            ? BuildLocalFilteredDtos()
            : found;

        List<VacancyItemViewModel> cards = await Task.Run(() => CreateCards(toCreate));

        if (generation != _fetchGeneration)
        {
            // Новіший результат пошуку вже замінив список — застарілий тихо відкидаємо.
            return;
        }

        ErrorMessage = null;
        HasLoaded = true;
        ApplyCardsToUi(cards);
    }

    private List<VacancyItemViewModel> CreateCards(List<VacancyDto> dtos)
    {
        List<VacancyItemViewModel> cards = new(dtos.Count);
        foreach (VacancyDto dto in dtos)
        {
            cards.Add(CreateVacancyItemViewModel(dto));
        }

        return cards;
    }

    private void ApplyCardsToUi(List<VacancyItemViewModel> cards)
    {
        _vacancies.ReplaceAll(cards);

        _distancePrefetchCts?.Cancel();
        CancellationTokenSource prefetchCts = new();
        _distancePrefetchCts = prefetchCts;
        _ = VacancyFilterEngine.PrefetchDistancesAsync(cards, _distanceService, prefetchCts.Token);

        TotalCount = cards.Count;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasActiveLocalFilters));
        OnPropertyChanged(nameof(IsFilterHighlightActive));
    }

    /// <summary>З якої сторінки списку почати пошук на активному сайті (синхронізується з індивідуальною сторінкою платформи).</summary>
    [ObservableProperty]
    public partial string SiteFromPage { get; set; } = "1";

    partial void OnSiteFromPageChanged(string value)
    {
        if (_isSyncingFromPage)
        {
            return;
        }

        _isSyncingFromPage = true;
        try
        {
            switch (SelectedPlatform)
            {
                case "WorkUa": WorkUaFromPage = value; break;
                case "Djinni": DjinniFromPage = value; break;
                case "RobotaUa": RobotaUaFromPage = value; break;
            }
        }
        finally
        {
            _isSyncingFromPage = false;
        }
    }

    /// <summary>Запит до сервера з фільтрів обраної панелі; null — для цього сайту пошук не підключено.</summary>
    internal SiteSearchRequest? BuildSiteSearchRequest() => BuildSiteSearchRequestForPlatform(SelectedPlatform);

    /// <summary>Список запитів для всіх сайтів, увімкнених галочками для одночасного парсингу (з власними налаштуваннями кожного).</summary>
    public List<SiteSearchRequest> BuildSiteSearchRequests()
    {
        List<SiteSearchRequest> list = [];
        if (IsDouParseEnabled && BuildSiteSearchRequestForPlatform("DOU") is { } dou) list.Add(dou);
        if (IsWorkUaParseEnabled && BuildSiteSearchRequestForPlatform("WorkUa") is { } work) list.Add(work);
        if (IsDjinniParseEnabled && BuildSiteSearchRequestForPlatform("Djinni") is { } djinni) list.Add(djinni);
        if (IsRobotaUaParseEnabled && BuildSiteSearchRequestForPlatform("RobotaUa") is { } robota) list.Add(robota);
        return list;
    }

    private SiteSearchRequest? BuildSiteSearchRequestForPlatform(string platform)
    {
        SiteSearchRequest? request = BuildPlatformRequest(platform);
        if (request is null || request.Source == JobSource.Dou)
        {
            return request;
        }

        string fromPage = platform switch
        {
            "WorkUa" => WorkUaFromPage,
            "Djinni" => DjinniFromPage,
            "RobotaUa" => RobotaUaFromPage,
            _ => SiteFromPage
        };
        return request with { FromPage = FromPageOf(fromPage) };
    }

    private static int FromPageOf(string page) =>
        Math.Clamp(int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 1, 1, MaxSiteFromPage);

    public const int MaxSiteFromPage = 50;

    private SiteSearchRequest? BuildPlatformRequest(string platform) => platform switch
    {
        "Djinni" => new SiteSearchRequest
        {
            Source = JobSource.Djinni,
            Categories = DjinniCategoryKeys.GetValueOrDefault(DjinniSelectedCategory)?.ToList(),
            Experience = CodeInParentheses(DjinniSelectedExperience),
            City = CodeInParentheses(DjinniSelectedCity),
            Employment = CodeInParentheses(DjinniSelectedEmployment),
            SalaryFrom = int.TryParse(DjinniCustomMinSalary, NumberStyles.Integer, CultureInfo.InvariantCulture, out int custom) && custom > 0
                ? custom
                : DigitsOf(DjinniSelectedSalary),
            Keywords = KeywordsOr(DjinniAllKeywords),
            // Англійська, тип компанії, домен, регіон і країна — параметри адреси djinni.co (звірено 15.11.2026)
            Options = OptionsOf(DjinniSelectedEnglish, DjinniSelectedCompanyType, DjinniSelectedDomain, DjinniSelectedRegion, DjinniSelectedCountry),
            Pages = PagesOf(DjinniPagesToParse)
        },
        "WorkUa" => new SiteSearchRequest
        {
            Source = JobSource.WorkUa,
            // Місто «Дистанційно» на Work.ua — окреме «місто» remote; сфери йдуть кодами в options (category=1,9)
            City = WorkUaCitySlugs.GetValueOrDefault(WorkUaSelectedCity),
            Keywords = KeywordsOr(WorkUaSearchQuery),
            Options = WorkUaOptions(),
            Pages = PagesOf(WorkUaPagesToParse),
            ListOnly = !WorkUaOpenJobPages
        },
        "RobotaUa" => new SiteSearchRequest
        {
            Source = JobSource.RobotaUa,
            City = RobotaUaCities.FirstOrDefault(city => city.Name == RobotaUaSelectedCity).Slug,
            Keywords = KeywordsOr(RobotaUaSearchQuery),
            Options = RobotaUaOptions(),
            Pages = PagesOf(RobotaUaPagesToParse)
        },
        "DOU" => new SiteSearchRequest
        {
            Source = JobSource.Dou,
            Categories = DouSelectedCategory == DouCategoryOptions[0] ? null : [DouSelectedCategory],
            // «Без досвіду (First Job)» на DOU — це стрічка «для початківців», тобто досвід до року
            Experience = DouSelectedExperience == DouExperienceOptions[0] ? null
                : DouSelectedExperience == DouExperienceOptions[1] ? "0-1"
                : CodeInParentheses(DouSelectedExperience),
            City = DouSelectedCity == DouCityOptions[0] ? null : DouSelectedCity,
            Remote = DouIsRemote,
            Keywords = KeywordsOr(DouSearchQuery),
            Pages = 1
        },
        _ => null
    };

    /// <summary>Спільні ключові слова блоку 1, а якщо їх немає — поле ключових слів самої платформи.</summary>
    private string? KeywordsOr(string? platformKeywords) =>
        !string.IsNullOrWhiteSpace(SiteKeywords) ? SiteKeywords.Trim()
        : !string.IsNullOrWhiteSpace(platformKeywords) ? platformKeywords.Trim()
        : null;

    private static int PagesOf(string pages) =>
        Math.Clamp(int.TryParse(pages, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 1, 1, MaxSitePages);

    /// <summary>Фільтри Work.ua, які сайт бере з параметрів адреси, а не зі шляху.</summary>
    private Dictionary<string, string>? WorkUaOptions()
    {
        Dictionary<string, string> options = OptionsOf(WorkUaSelectedExperience, WorkUaSelectedDays) ?? [];
        foreach (IGrouping<string, SelectableOption> chosen in WorkUaCategoryChoices.Concat(WorkUaEmploymentChoices).Concat(WorkUaLanguageChoices)
            .Where(choice => choice.IsSelected)
            .GroupBy(choice => choice.Key))
        {
            options[chosen.Key] = string.Join(',', chosen.Select(choice => choice.Value));
        }

        string? salaryFrom = WorkUaSalaryCode(WorkUaSalaryFrom, roundUp: false);
        string? salaryTo = WorkUaSalaryCode(WorkUaSalaryTo, roundUp: true);
        if (salaryFrom is not null)
        {
            options["salaryfrom"] = salaryFrom;
        }

        if (salaryTo is not null)
        {
            options["salaryto"] = salaryTo;
        }

        // З межею зарплати сайт прибирає вакансії без неї; «Включно без ЗП» повертає їх
        if ((salaryFrom ?? salaryTo) is not null && WorkUaIncludeNoSalary)
        {
            options["nosalary"] = "1";
        }

        if (WorkUaSelectedWordMatch?.Contains("anyword=1", StringComparison.Ordinal) == true)
        {
            options["anyword"] = "1";
        }
        else if (WorkUaSelectedWordMatch?.Contains("nosynonym=1", StringComparison.Ordinal) == true)
        {
            options["nosynonym"] = "1";
        }

        foreach ((bool on, string name) in new[]
        {
            (WorkUaForStudents, "student"), (WorkUaForPersonsWithDisabilities, "disability"), (WorkUaForRetirees, "retired"),
            (WorkUaForVeterans, "veteran"), (WorkUaHasReservation, "deferment"), (WorkUaWithoutResume, "noresume")
        })
        {
            if (on)
            {
                options[name] = "1";
            }
        }

        return options.Count == 0 ? null : options;
    }

    // Зарплата на Work.ua — не сума, а код межі: 2 — 10 000 грн, 3 — 15 000 … 8 — 100 000 грн
    private static readonly (int Uah, string Code)[] WorkUaSalarySteps =
        [(10_000, "2"), (15_000, "3"), (20_000, "4"), (30_000, "5"), (40_000, "6"), (50_000, "7"), (100_000, "8")];

    /// <summary>Сума в гривнях → код межі Work.ua: «від» округлюємо вниз, «до» — вгору, щоб не загубити вакансій.</summary>
    public static string? WorkUaSalaryCode(string? uah, bool roundUp)
    {
        if (!int.TryParse(uah, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
        {
            return null;
        }

        return roundUp
            ? WorkUaSalarySteps.FirstOrDefault(step => step.Uah >= value).Code ?? WorkUaSalarySteps[^1].Code
            : WorkUaSalarySteps.LastOrDefault(step => step.Uah <= value).Code;
    }

    /// <summary>Варіанти з кодом «(назва=значення)» наприкінці → фільтри сайту; варіанти без коду пропускаються.</summary>
    /// <summary>
    /// Фільтри Robota.ua. Сам сайт за «Від N грн» показує й вакансії без зарплати; користувач, що обрав зарплату,
    /// чекає лише вакансій з нею, тож додаємо опцію сайту «Приховати вакансії без зарплати» (salaryType=false),
    /// якщо не увімкнено «Включно без ЗП».
    /// </summary>
    private Dictionary<string, string>? RobotaUaOptions()
    {
        Dictionary<string, string>? options = OptionsOf(RobotaUaSelectedEmployment, RobotaUaSelectedSalary, RobotaUaSelectedExperience);
        if (options?.ContainsKey("salary") == true && !RobotaUaIncludeNoSalary)
        {
            options["salaryType"] = "false";
        }

        return options;
    }

    private static Dictionary<string, string>? OptionsOf(params string[] selected)
    {
        Dictionary<string, string> options = new(StringComparer.Ordinal);
        foreach (string option in selected)
        {
            if (string.IsNullOrEmpty(option))
            {
                continue;
            }

            Match match = OptionCode().Match(option);
            int equals = match.Success ? match.Groups[1].Value.IndexOf('=', StringComparison.Ordinal) : -1;
            if (equals > 0)
            {
                options[match.Groups[1].Value[..equals]] = match.Groups[1].Value[(equals + 1)..];
            }
        }

        return options.Count == 0 ? null : options;
    }

    /// <summary>Сфери Work.ua та їхні коди в параметрі category (звірено з сайтом 15.11.2026).</summary>
    private static readonly (string Name, string Code)[] WorkUaCategoryCodes =
    [
        ("IT, комп'ютери, інтернет", "1"),
        ("Адміністрація, керівництво середньої ланки", "2"),
        ("Будівництво, архітектура", "19"),
        ("Бухгалтерія, аудит", "3"),
        ("Готельно-ресторанний бізнес, туризм", "4"),
        ("Дизайн, творчість", "5"),
        ("ЗМІ, видавництво, поліграфія", "17"),
        ("Краса, фітнес, спорт", "6"),
        ("Культура, музика, шоу-бізнес", "7"),
        ("Логістика, склад, ЗЕД", "8"),
        ("Маркетинг, реклама, PR", "9"),
        ("Медицина, фармацевтика", "10"),
        ("Нерухомість", "11"),
        ("Освіта, наука", "12"),
        ("Охорона, безпека", "13"),
        ("Продаж, закупівля", "22"),
        ("Робочі спеціальності, виробництво", "14"),
        ("Роздрібна торгівля", "23"),
        ("Секретаріат, діловодство, АГВ", "15"),
        ("Страхування", "18"),
        ("Сфера обслуговування", "20"),
        ("Сільське господарство, агробізнес", "30"),
        ("Телекомунікації та зв'язок", "6792"),
        ("Топменеджмент, керівництво вищої ланки", "21"),
        ("Транспорт, автобізнес", "24"),
        ("Управління персоналом, HR", "25"),
        ("Фінанси, банк", "26"),
        ("Юриспруденція", "27")
    ];

    /// <summary>Міста в панелі Work.ua → частина адреси сайту; «Дистанційно» на Work.ua — теж «місто».</summary>
    private static readonly Dictionary<string, string> WorkUaCitySlugs = new(StringComparer.Ordinal)
    {
        ["Дистанційно"] = "remote",
        ["Київ"] = "kyiv",
        ["Львів"] = "lviv",
        ["Дніпро"] = "dnipro",
        ["Одеса"] = "odesa",
        ["Харків"] = "kharkiv",
        ["Вінниця"] = "vinnytsya",
        ["Запоріжжя"] = "zaporizhzhya",
        ["Івано-Франківськ"] = "ivano-frankivsk",
        ["Тернопіль"] = "ternopil",
        ["Полтава"] = "poltava",
        ["Рівне"] = "rivne",
        ["Хмельницький"] = "khmelnytskyi",
        ["Черкаси"] = "cherkasy",
        ["Чернівці"] = "chernivtsi_cv",
        ["Житомир"] = "zhytomyr",
        ["Ужгород"] = "uzhhorod",
        ["Луцьк"] = "lutsk",
        ["Миколаїв"] = "mykolaiv_nk",
        ["Суми"] = "sumy",
        ["Чернігів"] = "chernihiv",
        ["Кропивницький"] = "kropyvnytskyi",
        ["Інші країни"] = "other"
    };

    /// <summary>Міста Robota.ua та їхні коди в адресі (/zapros/python/kyiv); без міста — вся Україна.</summary>
    private static readonly (string Name, string Slug)[] RobotaUaCities =
    [
        ("Київ", "kyiv"), ("Львів", "lviv"), ("Дніпро", "dnipro"), ("Одеса", "odessa"), ("Харків", "kharkiv"),
        ("Вінниця", "vinnytsia"), ("Запоріжжя", "zaporizhia"), ("Івано-Франківськ", "ivano-frankivsk"), ("Тернопіль", "ternopil"),
        ("Полтава", "poltava"), ("Рівне", "rivne"), ("Хмельницький", "khmelnytskyi"), ("Черкаси", "cherkasy"),
        ("Чернівці", "chernivtsi"), ("Житомир", "zhitomir"), ("Ужгород", "uzhhorod"), ("Луцьк", "lutsk"),
        ("Миколаїв", "mykolaiv"), ("Суми", "sumy"), ("Чернігів", "chernihiv"), ("Кропивницький", "kropyvnytskyi")
    ];

    /// <summary>«1 рік (1y)» → «1y», «Київ (kyiv)» → «kyiv», «1…3 роки (exp=1-3)» → «1-3»; без дужок — null.</summary>
    public static string? CodeInParentheses(string option)
    {
        Match match = OptionCode().Match(option);
        if (!match.Success)
        {
            return null;
        }

        string code = match.Groups[1].Value;
        int equals = code.IndexOf('=', StringComparison.Ordinal);
        return equals >= 0 ? code[(equals + 1)..] : code;
    }

    private static int? DigitsOf(string option)
    {
        string digits = new(option.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : null;
    }

    [GeneratedRegex(@"\(([^()]+)\)\s*$", RegexOptions.None, 1000)]
    private static partial Regex OptionCode();

    /// <summary>Назви категорій у панелі Djinni → ключі категорій сайту (параметр primary_keyword).</summary>
    private static readonly Dictionary<string, string[]> DjinniCategoryKeys = new(StringComparer.Ordinal)
    {
        ["JavaScript / Front-End"] = ["JavaScript"],
        ["React.js"] = ["React.js"],
        ["Angular"] = ["Angular"],
        ["Vue.js"] = ["Vue.js"],
        ["Svelte"] = ["Svelte"],
        ["Markup"] = ["Markup"],
        ["Fullstack"] = ["Fullstack"],
        ["C# / .NET"] = [".NET"],
        ["ASP.NET"] = ["ASP.NET"],
        ["Blazor"] = ["Blazor"],
        ["MAUI / Xamarin"] = ["MAUI", "Xamarin"],
        ["WPF / WinForms"] = ["WPF", "WinForms"],
        [".NET Gamedev"] = [".NET Gamedev"],
        ["Java"] = ["Java"],
        ["Python"] = ["Python"],
        ["PHP (Laravel / Symfony / WordPress)"] = ["PHP"],
        ["Node.js"] = ["Node.js"],
        ["iOS"] = ["iOS"],
        ["Android"] = ["Android"],
        ["React Native"] = ["React Native"],
        ["Flutter"] = ["Flutter"],
        ["C / C++ / Embedded"] = ["C++"],
        ["Golang"] = ["Golang"],
        ["Ruby"] = ["Ruby"],
        ["Scala"] = ["Scala"],
        ["Rust"] = ["Rust"],
        ["Elixir"] = ["Elixir"],
        ["Kotlin"] = ["Kotlin"],
        ["Salesforce"] = ["Salesforce"],
        ["ERP Systems (SAP / Odoo / MS Dynamics)"] = ["ERP"],
        ["No Code"] = ["No Code"],
        ["QA Manual"] = ["QA"],
        ["QA Automation"] = ["QA Automation"],
        ["QA Hardware / Embedded"] = ["QA Hardware / Embedded"],
        ["Design (UI/UX, Product, Graphic)"] = ["Design"],
        ["2D/3D Artist / Illustrator / Motion"] = ["Artist"],
        ["Gamedev (Unity / Unreal / Game Design)"] = ["Unity"],
        ["Business Analyst"] = ["Business Analyst"],
        ["Data Science"] = ["Data Science"],
        ["ML / AI"] = ["ML AI"],
        ["Data Analyst / Web Analyst"] = ["Data Analyst", "Web Analyst"],
        ["Data Engineer"] = ["Data Engineer"],
        ["Project Manager"] = ["Project Manager"],
        ["Product Manager"] = ["Product Manager"],
        ["Delivery Manager"] = ["Delivery Manager"],
        ["Scrum Master"] = ["Scrum Master"],
        ["Engineering Manager"] = ["Engineering Manager"],
        ["C-Level (CTO / CEO / CPO / CMO / COO)"] = ["Head Chief"],
        ["DevOps"] = ["DevOps"],
        ["Security / Penetration Tester"] = ["Security"],
        ["Sysadmin"] = ["Sysadmin"],
        ["DefTech (UAV / EW / C4ISR / Weapons)"] = ["DefTech"],
        ["Hardware / Firmware / FPGA / Robotics"] = ["Hardware"],
        ["Engineering / CAD Design"] = ["Engineering / CAD Design"],
        ["Marketing / PPC / Media Buying / SMM"] = ["Marketing"],
        ["Sales / Business Development / Account Manager"] = ["Sales"],
        ["Lead Generation"] = ["Lead Generation"],
        ["SEO"] = ["SEO"],
        ["HR / Recruiter"] = ["HR"],
        ["Technical Writing"] = ["Technical Writing"],
        ["Customer / Technical Support"] = ["Support"],
        ["Finances / FinOps / Accountant"] = ["Finances"],
        ["(Other)"] = ["Other"],
    };

    [RelayCommand]
    private Task FilterExistingAsync()
    {
        if (VacancyFilterEngine.TryAddWords(NewWhiteListWord, WhiteListWords))
        {
            NewWhiteListWord = string.Empty;
        }

        if (VacancyFilterEngine.TryAddWords(NewBanListWord, BanListWords))
        {
            NewBanListWord = string.Empty;
        }

        NotifyWordListProperties();
        return ExecuteLocalFilterAndSortAsync(updateBanner: true);
    }

    [RelayCommand]
    private Task ApplyFiltersAsync() => FilterExistingAsync();

    /// <summary>
    /// Скидання фільтрів (US-002 AC-2/3, Test-07, Test-33): без нового запиту, список — знову весь знайдений результат.
    /// </summary>
    [RelayCommand]
    public Task ResetFiltersAsync()
    {
        _isLocalFilterActive = false;
        SearchText = string.Empty;
        SkillFilter = string.Empty;
        SelectedFormat = FormatOptions[0];
        SelectedCategory = CategoryOptions[0];
        MinSalaryFilter = string.Empty;
        MaxSalaryFilter = string.Empty;

        WhiteListWords.Clear();
        BanListWords.Clear();
        NewWhiteListWord = string.Empty;
        NewBanListWord = string.Empty;
        NotifyWordListProperties();

        _isSyncingSort = true;
        try
        {
            SelectedSortOption = SortOptions[0];
            WorkUaSelectedSort = WorkUaSortOptions[0];
            SelectedSourceFilter = SourceFilterOptions[0];
            SelectedFormat = FormatOptions[0];
        }
        finally
        {
            _isSyncingSort = false;
        }

        SelectedSalaryCurrency = SalaryCurrencyOptions[0];
        UnifiedCompanyFilter = string.Empty;
        UnifiedTitleOnly = false;
        UnifiedOnlyWithSalary = false;
        UnifiedOnlyActive = false;
        UnifiedIsRemote = false;
        UnifiedHideViewed = false;
        UnifiedIncludeNoSalary = false;
        WhiteListMatchAll = false;
        WorkUaFromPage = "1";
        DjinniFromPage = "1";
        RobotaUaFromPage = "1";
        SiteFromPage = "1";

        // Скидання вибору сайтів для парсингу назад до активної платформи
        _hasExplicitSiteSelection = false;
        _isSyncingPlatformFlags = true;
        try
        {
            IsDouParseEnabled = SelectedPlatform == "DOU";
            IsWorkUaParseEnabled = SelectedPlatform == "WorkUa";
            IsDjinniParseEnabled = SelectedPlatform == "Djinni";
            IsRobotaUaParseEnabled = SelectedPlatform == "RobotaUa";
        }
        finally
        {
            _isSyncingPlatformFlags = false;
        }

        // Скидання фільтрів DOU
        DouPagesToParse = "1";
        DouSearchQuery = string.Empty;
        DouSearchInDescription = false;
        DouSelectedCategory = DouCategoryOptions[0];
        DouSelectedExperience = DouExperienceOptions[0];
        DouSelectedCity = DouCityOptions[0];
        DouIsRemote = false;
        DouIsRelocation = false;
        DouIsFirstJob = false;
        DouIsGovTech = false;
        DouIsMilTech = false;
        DouIsDefenseForces = false;
        DouHasReservation = false;

        // Скидання фільтрів Work.ua
        WorkUaPagesToParse = "3";
        WorkUaOpenJobPages = false;
        WorkUaSearchQuery = string.Empty;
        WorkUaSelectedWordMatch = WorkUaWordMatchOptions[0];
        WorkUaTitleOnly = false;
        ClearChoices(WorkUaCategoryChoices);
        ClearChoices(WorkUaEmploymentChoices);
        ClearChoices(WorkUaLanguageChoices);
        WorkUaSelectedCity = WorkUaCityOptions[0];
        WorkUaSalaryFrom = string.Empty;
        WorkUaSalaryTo = string.Empty;
        WorkUaIncludeNoSalary = false;
        WorkUaSelectedExperience = WorkUaExperienceOptions[0];
        WorkUaSelectedDays = WorkUaDaysOptions[0];
        WorkUaAgeFrom = string.Empty;
        WorkUaAgeTo = string.Empty;
        WorkUaForStudents = false;
        WorkUaForPersonsWithDisabilities = false;
        WorkUaForRetirees = false;
        WorkUaForVeterans = false;
        WorkUaHasReservation = false;
        WorkUaWithoutResume = false;

        // Скидання фільтрів Djinni
        DjinniPagesToParse = "3";
        DjinniAllKeywords = string.Empty;
        DjinniAnyOfKeywords = string.Empty;
        DjinniExcludeKeywords = string.Empty;
        DjinniSelectedSearchType = DjinniSearchTypeOptions[0];
        DjinniCompanyFilter = string.Empty;
        DjinniSelectedCategory = DjinniCategoryOptions[0];
        DjinniSelectedSalary = DjinniSalaryOptions[0];
        DjinniCustomMinSalary = string.Empty;
        DjinniSelectedExperience = DjinniExperienceOptions[0];
        DjinniSelectedEmployment = DjinniEmploymentOptions[0];
        DjinniSelectedEnglish = DjinniEnglishOptions[0];
        DjinniSelectedCompanyType = DjinniCompanyTypeOptions[0];
        DjinniSelectedDomain = DjinniDomainOptions[0];
        DjinniSelectedRegion = DjinniRegionOptions[0];
        DjinniSelectedCity = DjinniCityOptions[0];
        DjinniSelectedCountry = DjinniCountryOptions[0];
        DjinniHasPublicSalary = false;
        DjinniUkrainianProduct = false;
        DjinniMilTech = false;
        DjinniHasReservation = false;
        DjinniHideStale = false;

        // Скидання фільтрів Robota.ua і спільних ключових слів сайту
        SiteKeywords = string.Empty;
        RobotaUaSearchQuery = string.Empty;
        RobotaUaPagesToParse = "1";
        RobotaUaSelectedCity = RobotaUaCityOptions[0];
        RobotaUaSelectedEmployment = RobotaUaEmploymentOptions[0];
        RobotaUaSelectedSalary = RobotaUaSalaryOptions[0];
        RobotaUaIncludeNoSalary = false;
        RobotaUaSelectedExperience = RobotaUaExperienceOptions[0];

        // Знайдені вакансії лишаються: скидаються лише фільтри, тож список знову показує весь результат пошуку
        StaleCacheText = null;
        _appliedQuery = null;
        if (HasLoaded)
        {
            ApplyLocalFilterAndSort();
        }

        return Task.CompletedTask;
    }

    private static string DefaultExportFilePath()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Waterfall");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "vacancies_export.json");
    }

    [RelayCommand]
    private async Task ExportVacanciesAsync()
    {
        ErrorMessage = null;
        string? path = _fileDialog is not null
            ? await _fileDialog.PickJsonFileToSaveAsync("vacancies_export.json", CancellationToken.None)
            : DefaultExportFilePath();

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await ExportVacanciesToAsync(path);
    }

    public async Task ExportVacanciesToAsync(string path)
    {
        ErrorMessage = null;
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            List<VacancyDto> toExport = Vacancies.Select(v => v.Vacancy).ToList();
            string json = JsonSerializer.Serialize(toExport, ClientJsonContext.Default.ListVacancyDto);
            await File.WriteAllTextAsync(path, json, CancellationToken.None);
            StaleCacheText = $"Експортовано {toExport.Count} вакансій у {path}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Не вдалося експортувати вакансії: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportVacanciesAsync()
    {
        ErrorMessage = null;
        string? path = _fileDialog is not null
            ? await _fileDialog.PickJsonFileToOpenAsync(CancellationToken.None)
            : DefaultExportFilePath();

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await ImportVacanciesFromAsync(path);
    }

    public async Task ImportVacanciesFromAsync(string path)
    {
        ErrorMessage = null;
        try
        {
            if (!File.Exists(path))
            {
                StaleCacheText = $"Файл не знайдено ({path}).";
                return;
            }

            string json = await File.ReadAllTextAsync(path, CancellationToken.None);
            List<VacancyDto>? imported = JsonSerializer.Deserialize(json, ClientJsonContext.Default.ListVacancyDto);
            if (imported is null || imported.Count == 0)
            {
                StaleCacheText = "У файлі немає вакансій.";
                return;
            }

            await ApplyFetchedVacanciesAsync(imported);
            await PersistFetchedLocallyAsync(imported, isSynced: false);
            StaleCacheText = $"Імпортовано {imported.Count} вакансій із {path}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Не вдалося імпортувати вакансії: {ex.Message}";
        }
    }

    /// <summary>
    /// Відкриття вкладки: лише оновити стан входу. Базового каталогу немає — вакансії з'являються після «Знайти».
    /// </summary>
    [RelayCommand]
    private Task EnsureLoadedAsync() => RefreshAuthStatusAsync(CancellationToken.None);

    /// <summary>
    /// Закрити банер помилки (наприклад, невдале додавання в «Обране»).
    /// </summary>
    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    /// <summary>
    /// Кнопка пошуку / Enter: застосувати рядок пошуку одразу, без очікування debounce.
    /// </summary>
    [RelayCommand]
    private void Search()
    {
        _debounceCts?.Cancel();
        ApplySearchText();
    }

    /// <summary>
    /// «Потягнути, щоб оновити»: сайти не перечитуємо (це робить «Знайти»), лише стан входу й фільтри списку.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            await RefreshAuthStatusAsync(CancellationToken.None);
            if (HasLoaded)
            {
                ApplyLocalFilterAndSort();
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    partial void OnSearchTextChanged(string value) => _ = SearchAfterPauseAsync();

    private async Task SearchAfterPauseAsync()
    {
        _debounceCts?.Cancel();
        CancellationTokenSource cts = new();
        _debounceCts = cts;
        try
        {
            await Task.Delay(SearchDebounce, _time, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        ApplySearchText();
    }

    /// <summary>
    /// Рядок пошуку звужує знайдені вакансії (назва, навички, компанія, категорія). Новий непорожній запит
    /// записується в історію пошуків — з неї будуються рекомендації.
    /// </summary>
    private void ApplySearchText()
    {
        string? query = Normalize(SearchText);
        if (query == _appliedQuery)
        {
            return;
        }

        _appliedQuery = query;
        if (query is not null)
        {
            _ = _gateway.RecordSearchAsync(query, CancellationToken.None);
        }

        if (HasLoaded)
        {
            ApplyLocalFilterAndSort();
        }
    }

    private VacancyItemViewModel CreateVacancyItemViewModel(VacancyDto dto)
    {
        double? cachedKm = _distanceCache.TryGetValue(dto.Id, out double? km) ? km : null;
        return new VacancyItemViewModel(
            dto,
            _gateway,
            _messenger,
            message => ErrorMessage = message,
            _urlLauncher,
            _distanceService,
            onDistanceCalculated: (id, dist) => _distanceCache[id] = dist,
            initialDistanceKm: cachedKm);
    }

    private async Task ExecuteLocalFilterAndSortAsync(bool updateBanner)
    {
        // Фільтри запам'ятовуються й застосуються до результату наступного пошуку на сайті
        _isLocalFilterActive = true;
        if (!HasLoaded)
        {
            if (updateBanner)
            {
                StaleCacheText = "Спочатку спарсіть вакансії.";
            }

            return;
        }

        if (IsDistanceSort(SelectedSortOption) && _distanceService != null && !string.IsNullOrWhiteSpace(_distanceService.UserAddress))
        {
            bool titleOnly = UnifiedTitleOnly
                || (SelectedPlatform == "WorkUa" && WorkUaTitleOnly)
                || (SelectedPlatform == "Djinni" && DjinniSelectedSearchType.Contains("title-only", StringComparison.OrdinalIgnoreCase));
            await VacancyFilterEngine.PrecalculateDistancesAsync(
                _loadedDtos.Where(v => MatchesLocalFilters(v, titleOnly)),
                _distanceService,
                _distanceCache,
                CancellationToken.None);
        }

        ApplyLocalFilterAndSort();

        if (updateBanner)
        {
            string whiteInfo = WhiteListWords.Count > 0 ? $", вайт-лист: {string.Join(", ", WhiteListWords)}" : string.Empty;
            string banInfo = BanListWords.Count > 0 ? $", бан-слова: {string.Join(", ", BanListWords)}" : string.Empty;
            string geoWarning = IsDistanceSort(SelectedSortOption) && string.IsNullOrWhiteSpace(_distanceService?.UserAddress)
                ? " · Вкажіть адресу в профілі для розрахунку відстані"
                : string.Empty;
            StaleCacheText = $"Показано {TotalCount} із {_loadedDtos.Count} ({SelectedSortOption}{whiteInfo}{banInfo}){geoWarning}";
        }
    }

    private bool HasActiveLocalFilterOrSort()
    {
        return SelectedSortOption != SortOptions[0]
            || !string.IsNullOrWhiteSpace(SearchText)
            || SelectedSourceFilter != SourceFilterOptions[0]
            || SelectedFormat != FormatOptions[0]
            || WhiteListWords.Count > 0
            || BanListWords.Count > 0
            || !string.IsNullOrWhiteSpace(UnifiedCompanyFilter)
            || !string.IsNullOrWhiteSpace(MinSalaryFilter)
            || !string.IsNullOrWhiteSpace(MaxSalaryFilter)
            || UnifiedOnlyWithSalary
            || UnifiedOnlyActive
            || UnifiedHideViewed;
    }

    private void ApplyLocalFilterAndSort()
    {
        ApplyCardsToUi(CreateCards(BuildLocalFilteredDtos()));
    }

    private List<VacancyDto> BuildLocalFilteredDtos()
    {
        bool titleOnly = UnifiedTitleOnly
            || (SelectedPlatform == "WorkUa" && WorkUaTitleOnly)
            || (SelectedPlatform == "Djinni" && DjinniSelectedSearchType.Contains("title-only", StringComparison.OrdinalIgnoreCase));

        IEnumerable<VacancyDto> query = _loadedDtos.Where(v => MatchesLocalFilters(v, titleOnly));
        List<VacancyDto> sorted = VacancyFilterEngine.SortVacancies(query, SelectedSortOption, GetDistanceKm).ToList();

        Dictionary<Guid, bool> savedStates = Vacancies.ToDictionary(v => v.Id, v => v.IsSaved);
        for (int i = 0; i < sorted.Count; i++)
        {
            VacancyDto dto = sorted[i];
            if (savedStates.TryGetValue(dto.Id, out bool isSaved) && isSaved != dto.IsSaved)
            {
                sorted[i] = dto with { IsSaved = isSaved };
            }
        }

        return sorted;
    }

    private bool MatchesLocalFilters(VacancyDto v, bool titleOnly)
    {
        // 0. Фільтр джерела (DOU / Work.ua / Djinni)
        if (SelectedSourceFilter == "DOU" && v.Source != JobSource.Dou)
        {
            return false;
        }

        if (SelectedSourceFilter == "Work.ua" && v.Source != JobSource.WorkUa)
        {
            return false;
        }

        if (SelectedSourceFilter == "Djinni" && v.Source != JobSource.Djinni)
        {
            return false;
        }

        if (SelectedSourceFilter == "Robota.ua" && v.Source != JobSource.RobotaUa)
        {
            return false;
        }

        // 1. Формат роботи (за готовими полями Schedule та Location моделі)
        if (SelectedFormat == "Віддалено")
        {
            bool isRemote = (v.Schedule?.Contains("дистанц", StringComparison.OrdinalIgnoreCase) ?? false)
                || (v.Schedule?.Contains("віддален", StringComparison.OrdinalIgnoreCase) ?? false)
                || (v.Schedule?.Contains("remote", StringComparison.OrdinalIgnoreCase) ?? false)
                || v.Location.Contains("віддален", StringComparison.OrdinalIgnoreCase)
                || v.Location.Contains("дистанц", StringComparison.OrdinalIgnoreCase)
                || v.Location.Contains("remote", StringComparison.OrdinalIgnoreCase);

            if (!isRemote)
            {
                return false;
            }
        }
        else if (SelectedFormat == "Офіс")
        {
            bool isOffice = (v.Schedule?.Contains("офіс", StringComparison.OrdinalIgnoreCase) ?? false)
                || (v.Schedule?.Contains("office", StringComparison.OrdinalIgnoreCase) ?? false)
                || v.Location.Contains("офіс", StringComparison.OrdinalIgnoreCase);

            if (!isOffice)
            {
                return false;
            }
        }
        else if (SelectedFormat == "Гібрид")
        {
            bool isHybrid = (v.Schedule?.Contains("гібрид", StringComparison.OrdinalIgnoreCase) ?? false)
                || (v.Schedule?.Contains("hybrid", StringComparison.OrdinalIgnoreCase) ?? false);

            if (!isHybrid)
            {
                return false;
            }
        }

        // 2–3. Вайт-лист та Бан-лист слів
        if (!VacancyFilterEngine.MatchesWordLists(v, WhiteListWords, WhiteListMatchAll, BanListWords, titleOnly))
        {
            return false;
        }

        // 4. Пошуковий запит (назва посади, навички, компанія, категорія або опис)
        string? searchKeyword = Normalize(SearchText);
        if (!string.IsNullOrWhiteSpace(searchKeyword) && !VacancyFilterEngine.ContainsAllWords(v, searchKeyword, titleOnly))
        {
            return false;
        }

        // 5. Фільтр за компанією
        if (!string.IsNullOrWhiteSpace(UnifiedCompanyFilter) &&
            !v.Company.Contains(UnifiedCompanyFilter.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 6. Фільтри заробітної плати
        if (!VacancyFilterEngine.MatchesSalary(v, UnifiedOnlyWithSalary, MinSalaryFilter, MaxSalaryFilter, SelectedSalaryCurrency, UnifiedIncludeNoSalary))
        {
            return false;
        }

        // 7. Тільки активні вакансії (властивість IsActive)
        if (UnifiedOnlyActive && !v.IsActive)
        {
            return false;
        }

        // 8. Переглянуті: позначка сервера на момент пошуку або перегляд у цьому сеансі
        if (UnifiedHideViewed && (v.IsViewed || _viewedIds.Contains(v.Id)))
        {
            return false;
        }

        return true;
    }

    public static bool IsDistanceSort(string? sortOption) =>
        VacancyFilterEngine.IsDistanceSort(sortOption);

    private double? GetDistanceKm(VacancyDto v)
    {
        if (_distanceCache.TryGetValue(v.Id, out double? km))
        {
            return km;
        }

        VacancyItemViewModel? existing = Vacancies.FirstOrDefault(item => item.Id == v.Id);
        if (existing?.DistanceKm != null)
        {
            _distanceCache[v.Id] = existing.DistanceKm;
            return existing.DistanceKm;
        }

        return null;
    }

    private static string? Normalize(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}
