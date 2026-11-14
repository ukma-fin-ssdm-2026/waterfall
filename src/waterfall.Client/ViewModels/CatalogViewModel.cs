// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: пошук на сайтах за фільтрами панелі зі шкалою прогресу (issue #62, зі згоди Дениса); перевірено: тести CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: пошук на Work.ua і Robota.ua з панелей (коди адрес звірено з сайтами 10.11.2026), спільні ключові слова блоку 1 (issue #62, зі згоди Дениса); перевірено: CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: каталог без базового списку з БД — лише результати «Знайти», рядок пошуку й фільтри працюють локально (рішення команди 10.11.2026, зі згоди Дениса); перевірено: CatalogViewModelTests, самоперевірка автора (§8)
// AI: вайт-, бан-лист і рядок пошуку шукають слова також в описі вакансії (issue #62); перевірено: CatalogViewModelTests, самоперевірка автора (§8)
// AI: перемикач «відкривати кожну вакансію» Work.ua з попередженням про тривалість (issue #62); перевірено: CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: кнопка Robota.ua у рядку «Парсинг:» (issue #62); перевірено: CatalogSiteSearchTests, самоперевірка автора (§8)
// AI: «Від N грн» на Robota.ua приховує вакансії без зарплати (salaryType=false, звірено з сайтом 14.11.2026) (issue #62); перевірено: CatalogSiteSearchTests, самоперевірка автора (§8)

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.SiteSearch;
using Waterfall.Client.Models.Vacancies;

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

    // Останній застосований текст рядка пошуку: той самий запит (наприклад, з доданим пробілом) не записуємо в історію двічі
    private string? _appliedQuery;

    // Результат останнього пошуку на сайті без локальних фільтрів; список на екрані — його відфільтрована частина
    private readonly List<VacancyDto> _loadedDtos = [];
    private bool _isLocalFilterActive;
    private bool _isSyncingSort;
    private readonly IUrlLauncher? _urlLauncher;
    private readonly IDistanceCalculatorService? _distanceService;
    private readonly IAuthTokenStore? _tokenStore;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, double?> _distanceCache = new();

    public CatalogViewModel(
        IHttpGatewayService gateway,
        IMessenger messenger,
        TimeProvider time,
        IUrlLauncher? urlLauncher = null,
        IDistanceCalculatorService? distanceService = null,
        IAuthTokenStore? tokenStore = null)
    {
        _gateway = gateway;
        _messenger = messenger;
        _time = time;
        _urlLauncher = urlLauncher;
        _distanceService = distanceService;
        _tokenStore = tokenStore;

        // Зміна «Обраного» з іншої вкладки: оновлюємо картку на місці, без перезавантаження списку.
        messenger.Register<CatalogViewModel, VacancySaveChangedMessage>(this, static (vm, message) =>
        {
            foreach (VacancyItemViewModel item in vm.Vacancies.Where(v => v.Id == message.VacancyId))
            {
                item.IsSaved = message.IsSaved;
            }
        });

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

    public ObservableCollection<VacancyItemViewModel> Vacancies { get; } = [];

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

    public string FilterToggleText => IsFilterPanelVisible ? "Сховати фільтри ▲" : "Фільтри платформ ⚙";

    public string DouButtonText => $"🔵 DOU ({DouPagesToParse} стор.) {(IsDouPanelVisible ? "▲" : "▼")}";
    public string WorkUaButtonText => $"🟠 Work.ua ({WorkUaPagesToParse} стор.) {(IsWorkUaPanelVisible ? "▲" : "▼")}";
    public string DjinniButtonText => $"🟣 Djinni ({DjinniPagesToParse} стор.) {(IsDjinniPanelVisible ? "▲" : "▼")}";
    public string RobotaUaButtonText => $"🔴 Robota.ua ({RobotaUaPagesToParse} стор.) {(IsRobotaUaPanelVisible ? "▲" : "▼")}";

    public string ActivePlatformTitle => SelectedPlatform switch
    {
        "DOU" => "🔵 Фільтри платформи DOU (jobs.dou.ua)",
        "WorkUa" => "🟠 Фільтри платформи Work.ua (work.ua)",
        "Djinni" => "🟣 Фільтри платформи Djinni (djinni.co)",
        "RobotaUa" => "🔴 Фільтри платформи Robota.ua (robota.ua)",
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
    public partial string RobotaUaPagesToParse { get; set; } = "1";

    [ObservableProperty]
    public partial string RobotaUaSelectedCity { get; set; } = "Вся Україна";

    [ObservableProperty]
    public partial string RobotaUaSelectedEmployment { get; set; } = "Будь-яка зайнятість";

    [ObservableProperty]
    public partial string RobotaUaSelectedSalary { get; set; } = "Будь-яка зарплата";

    [ObservableProperty]
    public partial string RobotaUaSelectedExperience { get; set; } = "Будь-який досвід";

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
    public partial string WorkUaPagesToParse { get; set; } = "3";

    /// <summary>
    /// Відкривати сторінку кожної вакансії Work.ua: повний опис, навички, досвід і графік, але близько хвилини
    /// на сторінку списку. Вимкнено — лише картки списку (секунди, опис 1–2 рядки).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkUaJobPagesHint))]
    public partial bool WorkUaOpenJobPages { get; set; } = true;

    /// <summary>Попередження під перемикачем: скільки чекати і що втрачаємо без сторінок вакансій.</summary>
    public string WorkUaJobPagesHint => WorkUaOpenJobPages
        ? $"⏱ Повний опис і навички, але пошук довгий: близько {PagesOf(WorkUaPagesToParse)} хв (≈1 хв на сторінку з 14 вакансій)."
        : "⚡ Швидко, за секунди, але опис — 1–2 рядки з картки, без навичок, досвіду й графіка: вайт- і бан-лист бачать менше.";

    [ObservableProperty]
    public partial string WorkUaSearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WorkUaSelectedWordMatch { get; set; } = "З синонімами (за замовчуванням)";

    [ObservableProperty]
    public partial bool WorkUaTitleOnly { get; set; }

    [ObservableProperty]
    public partial string WorkUaSelectedCategory { get; set; } = "Усі категорії";

    [ObservableProperty]
    public partial string WorkUaSelectedCity { get; set; } = "Вся Україна";

    [ObservableProperty]
    public partial string WorkUaSalaryFrom { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WorkUaSalaryTo { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool WorkUaIncludeNoSalary { get; set; } = true;

    [ObservableProperty]
    public partial string WorkUaSelectedEmployment { get; set; } = "Будь-яка зайнятість";

    [ObservableProperty]
    public partial string WorkUaSelectedExperience { get; set; } = "Будь-який досвід";

    [ObservableProperty]
    public partial string WorkUaSelectedEducation { get; set; } = "Будь-яка освіта";

    [ObservableProperty]
    public partial string WorkUaSelectedLanguage { get; set; } = "Не має значення";

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

    public IReadOnlyList<string> WorkUaCategoryOptions { get; } =
    [
        "Усі категорії",
        "IT, комп'ютери, інтернет",
        "Адміністрація, керівництво середньої ланки",
        "Будівництво, архітектура",
        "Бухгалтерія, аудит",
        "Готельно-ресторанний бізнес, туризм",
        "Дизайн, творчість",
        "ЗМІ, видавництво, поліграфія",
        "Краса, фітнес, спорт",
        "Культура, музика, шоу-бізнес",
        "Логістика, склад, ЗЕД",
        "Маркетинг, реклама, PR",
        "Медицина, фармацевтика",
        "Нерухомість",
        "Освіта, наука",
        "Охорона, безпека",
        "Продаж, закупівля",
        "Робочі спеціальності, виробництво",
        "Роздрібна торгівля",
        "Секретаріат, діловодство, АГВ",
        "Сільське господарство, агробізнес",
        "Страхування",
        "Сфера обслуговування",
        "Телекомунікації та зв'язок",
        "Топменеджмент, керівництво вищої ланки",
        "Транспорт, автобізнес",
        "Управління персоналом, HR",
        "Фінанси, банк",
        "Юриспруденція"
    ];

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

    // Коди фільтрів Work.ua звірено з сайтом 10.11.2026: «досвід від N років» і «позмінна робота» сайт уже не пропонує
    public IReadOnlyList<string> WorkUaEmploymentOptions { get; } =
    [
        "Будь-яка зайнятість",
        "Повна зайнятість (employment=74)",
        "Неповна зайнятість (employment=75)",
        "Гібридний формат (hybrid=1)",
        "Дистанційна робота"
    ];

    public IReadOnlyList<string> WorkUaExperienceOptions { get; } =
    [
        "Будь-який досвід",
        "Без досвіду (experience=1)"
    ];

    public IReadOnlyList<string> WorkUaEducationOptions { get; } =
    [
        "Будь-яка освіта",
        "Вища (education=66)",
        "Незакінчена вища (education=67)",
        "Середня спеціальна (education=68)",
        "Середня (education=69)"
    ];

    public IReadOnlyList<string> WorkUaLanguageOptions { get; } =
    [
        "Не має значення",
        "Англійська (language=1)",
        "Українська (language=41)",
        "Німецька (language=2)",
        "Польська (language=3)",
        "Іспанська (language=4)",
        "Французька (language=5)"
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
        "Без англійської (no_english)",
        "Basic (A1)",
        "Pre-Intermediate (A2)",
        "Intermediate (B1)",
        "Upper-Intermediate (B2)",
        "Advanced / Fluent (C1)",
        "Proficient (C2)",
        "Native speaker"
    ];

    public IReadOnlyList<string> DjinniCompanyTypeOptions { get; } =
    [
        "Усі типи компаній",
        "Product (Продуктова)",
        "Outsource (Аутсорс)",
        "Outstaff (Аутстаф)",
        "Startup (Стартап)",
        "In-house IT",
        "Agency (Агенція)",
        "Mixed",
        "Government (Державна)"
    ];

    public IReadOnlyList<string> DjinniDomainOptions { get; } =
    [
        "Усі домени",
        "FinTech (fintech)",
        "E-commerce (ecommerce)",
        "SaaS (saas)",
        "MilTech / DefTech (miltech)",
        "ML / AI (ml)",
        "GameDev (gamedev)",
        "Healthcare (healthcare)",
        "EdTech (edutech)",
        "Crypto / Web3 (crypto)",
        "Cybersecurity (security)",
        "Mobile (mobile)",
        "Automotive (automotive)",
        "Telecom (telecom)",
        "Logistics (logistics)",
        "Media (media)",
        "Advertising / AdTech (advertising)",
        "GovTech (govtech)",
        "LegalTech (legaltech)",
        "PropTech (proptech)",
        "HRTech (hrtech)",
        "Travel (travel)",
        "Energy (energy)",
        "Agriculture (agriculture)",
        "Hardware (hardware)",
        "Gambling (gambling)",
        "Dating (dating)",
        "Adult (adult)",
        "Sports & Fitness (sports_fitness)",
        "Manufacturing (manufacturing)",
        "Entertainment (entertainment)",
        "Other (other)"
    ];

    public IReadOnlyList<string> DjinniRegionOptions { get; } =
    [
        "Усі регіони",
        "Весь світ (worldwide)",
        "Україна (UKR)",
        "Євросоюз (eu)",
        "Інші країни (other)"
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
        "Україна (UKR)",
        "Польща (POL)",
        "Кіпр (CYP)",
        "Румунія (ROU)",
        "Іспанія (ESP)",
        "Чехія (CZE)",
        "Словаччина (SVK)",
        "Грузія (GEO)",
        "Вірменія (ARM)",
        "Португалія (PRT)",
        "Молдова (MDA)",
        "США (USA)",
        "Німеччина (DEU)",
        "Болгарія (BGR)",
        "Литва (LTU)",
        "Канада (CAN)"
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
    public partial string SelectedSortOption { get; set; } = "За датою (спочатку нові)";

    public IReadOnlyList<string> SourceFilterOptions { get; } = ["Усі платформи", "DOU", "Work.ua", "Djinni"];

    [ObservableProperty]
    public partial string SelectedSourceFilter { get; set; } = "Усі платформи";

    public IReadOnlyList<string> SalaryCurrencyOptions { get; } = ["$ USD", "₴ ГРН"];

    [ObservableProperty]
    public partial string SelectedSalaryCurrency { get; set; } = "$ USD";

    [ObservableProperty]
    public partial string UnifiedCompanyFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool UnifiedTitleOnly { get; set; }

    [ObservableProperty]
    public partial bool UnifiedOnlyWithSalary { get; set; }

    [ObservableProperty]
    public partial bool UnifiedOnlyActive { get; set; }

    [ObservableProperty]
    public partial bool UnifiedIsRemote { get; set; }

    // Загальні властивості сумісності
    [ObservableProperty]
    public partial string SkillFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedFormat { get; set; } = "Усі формати";

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "Усі категорії";

    [ObservableProperty]
    public partial string MinSalaryFilter { get; set; } = string.Empty;

    [ObservableProperty]
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
        if (TryAddWordsToCollection(NewWhiteListWord, WhiteListWords))
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
        if (TryAddWordsToCollection(NewBanListWord, BanListWords))
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

    private static bool TryAddWordsToCollection(string? input, ObservableCollection<string> target)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        bool added = false;
        string[] parts = input.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part) &&
                !target.Any(existing => string.Equals(existing, part, StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(part);
                added = true;
            }
        }

        return added;
    }

    private void NotifyWordListProperties()
    {
        OnPropertyChanged(nameof(HasWhiteListWords));
        OnPropertyChanged(nameof(IsWhiteListEmpty));
        OnPropertyChanged(nameof(HasBanListWords));
        OnPropertyChanged(nameof(IsBanListEmpty));
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
    /// «Знайти» в блоці «Запит на сайт»: сервер читає Djinni чи DOU з фільтрами панелі, зберігає вакансії в каталог
    /// і повертає їх. Поки йде пошук, шкала показує прочитані сторінки; результат замінює список, а фільтри блоку 2
    /// далі уточнюють його локально, без повторного звернення до сайту.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFetchFromSites))]
    private async Task FetchFromSitesAsync()
    {
        SiteSearchRequest? request = BuildSiteSearchRequest();
        if (request is null)
        {
            StaleCacheText = "🌐 Оберіть сайт: DOU, Work.ua, Djinni або Robota.ua.";
            return;
        }

        _siteSearchCts?.Cancel();
        CancellationTokenSource cts = new();
        _siteSearchCts = cts;
        string site = request.Source switch
        {
            JobSource.Djinni => "Djinni",
            JobSource.WorkUa => "Work.ua",
            JobSource.RobotaUa => "Robota.ua",
            _ => "DOU"
        };
        // Сервер відкриває кожну вакансію: Robota.ua завжди, Work.ua — якщо користувач не обрав швидкий пошук
        bool slow = request.Source == JobSource.RobotaUa || request.Source == JobSource.WorkUa && !request.ListOnly;
        IsSiteSearchRunning = true;
        SiteSearchProgress = 0;
        SiteSearchStatusText = $"🌐 {site}: надсилаємо запит…";
        ErrorMessage = null;
        try
        {
            SiteSearchStatusDto status = await _gateway.StartSiteSearchAsync(request, cts.Token);
            while (status.IsInProgress)
            {
                ShowSiteSearchProgress(site, slow, status);
                await Task.Delay(SiteSearchPollInterval, _time, cts.Token);
                status = await _gateway.GetSiteSearchAsync(status.Id, cts.Token);
            }

            // Результат з кешу приходить уже завершеним, але без вакансій: їх віддає окремий запит стану
            if (status.State == SiteSearchStatusDto.Completed && status.Vacancies is null)
            {
                status = await _gateway.GetSiteSearchAsync(status.Id, cts.Token);
            }

            ShowSiteSearchResult(site, status);

            // Сортування за відстанню потребує відстаней до нових адрес — їх рахує повний прохід фільтрів
            if (status.State == SiteSearchStatusDto.Completed && IsDistanceSort(SelectedSortOption))
            {
                await ExecuteLocalFilterAndSortAsync(updateBanner: false);
            }
        }
        catch (ApiException ex) when (!cts.IsCancellationRequested)
        {
            StaleCacheText = ex.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "🔒 Увійдіть в обліковий запис, щоб шукати вакансії на сайтах."
                : $"⚠️ {ex.Message}";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Запущено новий пошук або користувач пішов зі сторінки
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

    private bool CanFetchFromSites() => !IsSiteSearchRunning;

    private void ShowSiteSearchProgress(string site, bool slow, SiteSearchStatusDto status)
    {
        SiteSearchProgress = status.Progress;
        // Сервер читає браузером по одній вакансії — попереджаємо, що це довше
        string note = slow ? " (кожну вакансію відкриваємо окремо, це до кількох хвилин)" : string.Empty;
        SiteSearchStatusText = status.State == SiteSearchStatusDto.Queued
            ? $"🌐 {site}: у черзі — сайт зараз обробляє інший пошук…"
            : $"🌐 {site}: сторінка {status.PagesDone} з {status.PagesTotal}, знайдено {status.Found}{note}";
    }

    private void ShowSiteSearchResult(string site, SiteSearchStatusDto status)
    {
        SiteSearchProgress = 1;
        if (status.State != SiteSearchStatusDto.Completed)
        {
            StaleCacheText = $"⚠️ {site}: {status.Message ?? "пошук не вдався. Спробуйте пізніше."}";
            return;
        }

        List<VacancyDto> found = status.Vacancies ?? [];
        Vacancies.Clear();
        _loadedDtos.Clear();
        foreach (VacancyDto vacancy in found)
        {
            _loadedDtos.Add(vacancy);
            Vacancies.Add(CreateVacancyItemViewModel(vacancy));
        }

        ErrorMessage = null;
        HasLoaded = true;
        TotalCount = found.Count;
        if (_isLocalFilterActive || HasActiveLocalFilterOrSort())
        {
            ApplyLocalFilterAndSort();
        }

        OnPropertyChanged(nameof(IsEmpty));
        string cache = status.FromCache ? " (такий пошук уже виконувався протягом 3 годин, тож показано збережене)" : string.Empty;
        string shown = Vacancies.Count < found.Count ? $", за фільтрами списку показано {Vacancies.Count}" : string.Empty;
        StaleCacheText = found.Count == 0
            ? $"🌐 {site}: {status.Message ?? "за цими фільтрами нічого не знайдено."}"
            : $"🌐 {site}: знайдено {found.Count} вакансій{cache}{shown}. Фільтри нижче уточнюють список без нового пошуку.";
    }

    /// <summary>Запит до сервера з фільтрів обраної панелі; null — для цього сайту пошук ще не підключено.</summary>
    internal SiteSearchRequest? BuildSiteSearchRequest() => SelectedPlatform switch
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
            Pages = PagesOf(DjinniPagesToParse)
        },
        "WorkUa" => new SiteSearchRequest
        {
            Source = JobSource.WorkUa,
            Categories = WorkUaCategorySlugs.TryGetValue(WorkUaSelectedCategory, out string? category) ? [category] : null,
            City = WorkUaCitySlugs.GetValueOrDefault(WorkUaSelectedCity),
            // «Дистанційна робота» на Work.ua — це окреме «місто» remote, тож лише коли місто не обрано
            Remote = WorkUaSelectedEmployment == "Дистанційна робота",
            Keywords = KeywordsOr(WorkUaSearchQuery),
            Options = WorkUaOptions(),
            Pages = PagesOf(WorkUaPagesToParse),
            ListOnly = !WorkUaOpenJobPages
        },
        "RobotaUa" => new SiteSearchRequest
        {
            Source = JobSource.RobotaUa,
            City = RobotaUaCities.FirstOrDefault(city => city.Name == RobotaUaSelectedCity).Slug,
            Keywords = KeywordsOr(null),
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
        Dictionary<string, string> options = OptionsOf(WorkUaSelectedEmployment, WorkUaSelectedExperience, WorkUaSelectedEducation, WorkUaSelectedLanguage, WorkUaSelectedDays) ?? [];
        if (WorkUaSalaryCode(WorkUaSalaryFrom, roundUp: false) is { } from)
        {
            options["salaryfrom"] = from;
        }

        if (WorkUaSalaryCode(WorkUaSalaryTo, roundUp: true) is { } to)
        {
            options["salaryto"] = to;
        }

        if (WorkUaSelectedWordMatch.Contains("anyword=1", StringComparison.Ordinal))
        {
            options["anyword"] = "1";
        }
        else if (WorkUaSelectedWordMatch.Contains("nosynonym=1", StringComparison.Ordinal))
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
    /// чекає лише вакансій з нею, тож додаємо опцію сайту «Приховати вакансії без зарплати» (salaryType=false).
    /// </summary>
    private Dictionary<string, string>? RobotaUaOptions()
    {
        Dictionary<string, string>? options = OptionsOf(RobotaUaSelectedEmployment, RobotaUaSelectedSalary, RobotaUaSelectedExperience);
        if (options?.ContainsKey("salary") == true)
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
            Match match = OptionCode().Match(option);
            int equals = match.Success ? match.Groups[1].Value.IndexOf('=', StringComparison.Ordinal) : -1;
            if (equals > 0)
            {
                options[match.Groups[1].Value[..equals]] = match.Groups[1].Value[(equals + 1)..];
            }
        }

        return options.Count == 0 ? null : options;
    }

    /// <summary>Сфери в панелі Work.ua → частина адреси сайту (/jobs-it/, /jobs-sales/…).</summary>
    private static readonly Dictionary<string, string> WorkUaCategorySlugs = new(StringComparer.Ordinal)
    {
        ["IT, комп'ютери, інтернет"] = "it",
        ["Адміністрація, керівництво середньої ланки"] = "administration",
        ["Будівництво, архітектура"] = "construction-architecture",
        ["Бухгалтерія, аудит"] = "accounting",
        ["Готельно-ресторанний бізнес, туризм"] = "hotel-restaurant-tourism",
        ["Дизайн, творчість"] = "design-art",
        ["ЗМІ, видавництво, поліграфія"] = "publishing-media",
        ["Краса, фітнес, спорт"] = "beauty-sports",
        ["Культура, музика, шоу-бізнес"] = "culture-music-showbiz",
        ["Логістика, склад, ЗЕД"] = "logistic-supply-chain",
        ["Маркетинг, реклама, PR"] = "marketing-advertising-pr",
        ["Медицина, фармацевтика"] = "healthcare",
        ["Нерухомість"] = "real-estate",
        ["Освіта, наука"] = "education-scientific",
        ["Охорона, безпека"] = "security",
        ["Продаж, закупівля"] = "sales",
        ["Робочі спеціальності, виробництво"] = "production-engineering",
        ["Роздрібна торгівля"] = "retail",
        ["Секретаріат, діловодство, АГВ"] = "office-secretarial",
        ["Сільське господарство, агробізнес"] = "agriculture",
        ["Страхування"] = "insurance",
        ["Сфера обслуговування"] = "customer-service",
        ["Телекомунікації та зв'язок"] = "telecommunications",
        ["Топменеджмент, керівництво вищої ланки"] = "management-executive",
        ["Транспорт, автобізнес"] = "auto-transport",
        ["Управління персоналом, HR"] = "hr-recruitment",
        ["Фінанси, банк"] = "banking-finance",
        ["Юриспруденція"] = "legal"
    };

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
        if (TryAddWordsToCollection(NewWhiteListWord, WhiteListWords))
        {
            NewWhiteListWord = string.Empty;
        }

        if (TryAddWordsToCollection(NewBanListWord, BanListWords))
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
        WorkUaOpenJobPages = true;
        WorkUaSearchQuery = string.Empty;
        WorkUaSelectedWordMatch = WorkUaWordMatchOptions[0];
        WorkUaTitleOnly = false;
        WorkUaSelectedCategory = WorkUaCategoryOptions[0];
        WorkUaSelectedCity = WorkUaCityOptions[0];
        WorkUaSalaryFrom = string.Empty;
        WorkUaSalaryTo = string.Empty;
        WorkUaIncludeNoSalary = true;
        WorkUaSelectedEmployment = WorkUaEmploymentOptions[0];
        WorkUaSelectedExperience = WorkUaExperienceOptions[0];
        WorkUaSelectedEducation = WorkUaEducationOptions[0];
        WorkUaSelectedLanguage = WorkUaLanguageOptions[0];
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
        RobotaUaPagesToParse = "1";
        RobotaUaSelectedCity = RobotaUaCityOptions[0];
        RobotaUaSelectedEmployment = RobotaUaEmploymentOptions[0];
        RobotaUaSelectedSalary = RobotaUaSalaryOptions[0];
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

    // # Mock
    [RelayCommand]
    private void ExportVacancies()
    {
        ErrorMessage = null;
        StaleCacheText = $"Експорт списку ({TotalCount} вакансій) виконано успішно у файл vacancies_export.json";
    }

    // # Mock
    [RelayCommand]
    private void ImportVacancies()
    {
        ErrorMessage = null;
        StaleCacheText = "Імпорт вакансій: завантажено 25 нових вакансій із локального файлу.";
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
                StaleCacheText = "🌐 Спочатку знайдіть вакансії: оберіть сайт і фільтри в блоці 1 та натисніть «Знайти».";
            }

            return;
        }

        if (IsDistanceSort(SelectedSortOption) && _distanceService != null && !string.IsNullOrWhiteSpace(_distanceService.UserAddress))
        {
            CancellationToken ct = CancellationToken.None;
            bool titleOnly = UnifiedTitleOnly
                || (SelectedPlatform == "WorkUa" && WorkUaTitleOnly)
                || (SelectedPlatform == "Djinni" && DjinniSelectedSearchType.Contains("title-only", StringComparison.OrdinalIgnoreCase));
            IEnumerable<VacancyDto> filtered = _loadedDtos.Where(v => MatchesLocalFilters(v, titleOnly));
            await PrecalculateDistancesAsync(filtered, ct);
        }

        ApplyLocalFilterAndSort();

        if (updateBanner)
        {
            string whiteInfo = WhiteListWords.Count > 0 ? $", вайт-лист=[{string.Join(", ", WhiteListWords)}]" : string.Empty;
            string banInfo = BanListWords.Count > 0 ? $", бан-слова=[{string.Join(", ", BanListWords)}]" : string.Empty;
            string geoWarning = IsDistanceSort(SelectedSortOption) && string.IsNullOrWhiteSpace(_distanceService?.UserAddress)
                ? " ⚠️ Вкажіть адресу в профілі для розрахунку відстані"
                : string.Empty;
            StaleCacheText = $"⚡ Відфільтровано знайдені вакансії: {TotalCount} із {_loadedDtos.Count} (сортування: {SelectedSortOption}{whiteInfo}{banInfo}){geoWarning}";
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
            || UnifiedOnlyActive;
    }

    private void ApplyLocalFilterAndSort()
    {
        bool titleOnly = UnifiedTitleOnly
            || (SelectedPlatform == "WorkUa" && WorkUaTitleOnly)
            || (SelectedPlatform == "Djinni" && DjinniSelectedSearchType.Contains("title-only", StringComparison.OrdinalIgnoreCase));

        IEnumerable<VacancyDto> query = _loadedDtos.Where(v => MatchesLocalFilters(v, titleOnly));
        var sorted = SortVacancies(query, SelectedSortOption).ToList();

        var savedStates = Vacancies.ToDictionary(v => v.Id, v => v.IsSaved);
        Vacancies.Clear();
        foreach (VacancyDto dto in sorted)
        {
            VacancyDto adjusted = savedStates.TryGetValue(dto.Id, out bool isSaved) && isSaved != dto.IsSaved
                ? dto with { IsSaved = isSaved }
                : dto;
            Vacancies.Add(CreateVacancyItemViewModel(adjusted));
        }

        TotalCount = Vacancies.Count;
        OnPropertyChanged(nameof(IsEmpty));
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

        // 2. Вайт-лист слів: назва, навички, компанія, категорія й опис (з «лише в назві» — тільки назва)
        if (WhiteListWords.Count > 0)
        {
            if (!WhiteListWords.Any(word => ContainsKeyword(v, word, titleOnly)))
            {
                return false;
            }
        }

        // 3. Бан-слова: виключає вакансію, якщо слово знайдено в назві, навичках, компанії, категорії чи описі
        if (BanListWords.Count > 0)
        {
            if (BanListWords.Any(word => ContainsKeyword(v, word, titleOnly: false)))
            {
                return false;
            }
        }

        // 4. Пошуковий запит (назва посади, навички, компанія, категорія або опис)
        string? searchKeyword = Normalize(SearchText);
        if (!string.IsNullOrWhiteSpace(searchKeyword) && !ContainsAllWords(v, searchKeyword, titleOnly))
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
        bool hasSalary = v.SalaryMin.HasValue || v.SalaryMax.HasValue;
        if (UnifiedOnlyWithSalary && !hasSalary)
        {
            return false;
        }

        bool isUsdFilter = SelectedSalaryCurrency.Contains('$') || SelectedSalaryCurrency.Contains("USD", StringComparison.OrdinalIgnoreCase);
        if (decimal.TryParse(MinSalaryFilter, out decimal unifiedMin) && unifiedMin > 0)
        {
            decimal vacSal = isUsdFilter ? GetSalaryInUsd(v) : GetSalaryInUah(v);
            if (!hasSalary || vacSal < unifiedMin)
            {
                return false;
            }
        }

        if (decimal.TryParse(MaxSalaryFilter, out decimal unifiedMax) && unifiedMax > 0)
        {
            decimal vacSal = isUsdFilter ? GetSalaryInUsd(v) : GetSalaryInUah(v);
            if (hasSalary && vacSal > unifiedMax)
            {
                return false;
            }
        }

        // 7. Тільки активні вакансії (властивість IsActive)
        if (UnifiedOnlyActive && !v.IsActive)
        {
            return false;
        }

        return true;
    }

    private static bool ContainsAllWords(VacancyDto v, string query, bool titleOnly)
    {
        string[] words = query.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.All(w => ContainsKeyword(v, w, titleOnly));
    }

    private static bool ContainsKeyword(VacancyDto v, string word, bool titleOnly)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return true;
        }

        if (v.Title.Contains(word, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (titleOnly)
        {
            return false;
        }

        return v.Skills.Any(s => s.Contains(word, StringComparison.OrdinalIgnoreCase))
            || v.Company.Contains(word, StringComparison.OrdinalIgnoreCase)
            || v.Category.Contains(word, StringComparison.OrdinalIgnoreCase)
            // Не в усіх сайтів є поле навичок: слово «C++» часто є лише в тексті вакансії
            || v.Description.Contains(word, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDistanceSort(string? sortOption) =>
        sortOption is "За відстанню (спочатку ближчі)" or "За відстанню (спочатку дальші)";

    private async Task PrecalculateDistancesAsync(IEnumerable<VacancyDto> items, CancellationToken ct)
    {
        if (_distanceService == null || string.IsNullOrWhiteSpace(_distanceService.UserAddress))
        {
            return;
        }

        List<Task> tasks = [];
        foreach (VacancyDto v in items)
        {
            if (_distanceCache.ContainsKey(v.Id))
            {
                continue;
            }

            string target = !string.IsNullOrWhiteSpace(v.FullAddress) ? v.FullAddress : v.Location;
            if (string.IsNullOrWhiteSpace(target) ||
                target.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                target.Contains("Віддалено", StringComparison.OrdinalIgnoreCase) ||
                target.Contains("не вказано точну адресу", StringComparison.OrdinalIgnoreCase))
            {
                _distanceCache[v.Id] = null;
                continue;
            }

            tasks.Add(CalculateAndCacheDistanceAsync(v.Id, target, ct));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }

    private async Task CalculateAndCacheDistanceAsync(Guid id, string target, CancellationToken ct)
    {
        try
        {
            double? km = await _distanceService!.CalculateDistanceKmAsync(target, ct);
            _distanceCache[id] = km;
        }
        catch
        {
            _distanceCache[id] = null;
        }
    }

    private IEnumerable<VacancyDto> SortVacancies(IEnumerable<VacancyDto> items, string sortOption)
    {
        return sortOption switch
        {
            "За датою (спочатку старі)" => items
                .OrderBy(v => v.PublishedAt)
                .ThenBy(v => v.FirstSeenAt),
            "За зарплатою (від найвищої ↓)" => items
                .OrderByDescending(v => v.SalaryMin.HasValue || v.SalaryMax.HasValue)
                .ThenByDescending(GetSalaryInUah)
                .ThenByDescending(v => v.PublishedAt),
            "За зарплатою (від найнижчої ↑)" => items
                .OrderByDescending(v => v.SalaryMin.HasValue || v.SalaryMax.HasValue)
                .ThenBy(GetSalaryInUah)
                .ThenByDescending(v => v.PublishedAt),
            "За відстанню (спочатку ближчі)" => items
                .OrderByDescending(v => GetDistanceKm(v).HasValue)
                .ThenBy(v => GetDistanceKm(v) ?? double.MaxValue)
                .ThenByDescending(v => v.PublishedAt),
            "За відстанню (спочатку дальші)" => items
                .OrderByDescending(v => GetDistanceKm(v).HasValue)
                .ThenByDescending(v => GetDistanceKm(v) ?? double.MinValue)
                .ThenByDescending(v => v.PublishedAt),
            "За назвою посади (А → Я)" => items
                .OrderBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(v => v.PublishedAt),
            "За назвою посади (Я → А)" => items
                .OrderByDescending(v => v.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(v => v.PublishedAt),
            "За компанією (А → Я)" => items
                .OrderBy(v => v.Company, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => items
                .OrderByDescending(v => v.PublishedAt)
                .ThenByDescending(v => v.LastSeenAt)
        };
    }

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

    private static decimal GetSalaryInUah(VacancyDto v)
    {
        decimal raw = v.SalaryMax ?? v.SalaryMin ?? 0m;
        if (raw <= 0m)
        {
            return 0m;
        }

        if (v.Currency.Contains("USD", StringComparison.OrdinalIgnoreCase) || v.Currency.Contains('$'))
        {
            return raw * 41.5m;
        }

        if (v.Currency.Contains("EUR", StringComparison.OrdinalIgnoreCase) || v.Currency.Contains('€'))
        {
            return raw * 45.0m;
        }

        return raw;
    }

    private static decimal GetSalaryInUsd(VacancyDto v)
    {
        decimal raw = v.SalaryMax ?? v.SalaryMin ?? 0m;
        if (raw <= 0m)
        {
            return 0m;
        }

        if (v.Currency.Contains("USD", StringComparison.OrdinalIgnoreCase) || v.Currency.Contains('$'))
        {
            return raw;
        }

        if (v.Currency.Contains("EUR", StringComparison.OrdinalIgnoreCase) || v.Currency.Contains('€'))
        {
            return raw * 1.08m;
        }

        return raw / 41.5m;
    }

    private static string? Normalize(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}
