// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Вкладка «Каталог»: пошук із затримкою (debounce), пагінація «завантажити ще», стани завантаження / порожньо / помилка.
/// </summary>
/// <remarks>
/// Кожне завантаження першої сторінки (старт, пошук, оновлення, повтор) скасовує попереднє разом
/// із його «завантажити ще», тож застарілі відповіді ніколи не змішуються з новим запитом.
/// Команди викликаються з UI-потоку, а await повертається на нього ж — колекцію змінюємо без маршалінгу.
/// </remarks>
public partial class CatalogViewModel : ObservableObject
{
    public const int PageSize = 20;
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(400);

    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _time;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _debounceCts;
    private string? _loadedQuery;
    private int _page;
    private bool _hasNextPage;
    private readonly List<VacancyDto> _loadedDtos = [];
    private bool _isLocalFilterActive;
    private bool _isSyncingSort;
    private readonly IUrlLauncher? _urlLauncher;
    private readonly IDistanceCalculatorService? _distanceService;

    public CatalogViewModel(
        IHttpGatewayService gateway,
        IMessenger messenger,
        TimeProvider time,
        IUrlLauncher? urlLauncher = null,
        IDistanceCalculatorService? distanceService = null)
    {
        _gateway = gateway;
        _messenger = messenger;
        _time = time;
        _urlLauncher = urlLauncher;
        _distanceService = distanceService;

        // Зміна «Обраного» з іншої вкладки: оновлюємо картку на місці, без перезавантаження списку.
        messenger.Register<CatalogViewModel, VacancySaveChangedMessage>(this, static (vm, message) =>
        {
            foreach (VacancyItemViewModel item in vm.Vacancies.Where(v => v.Id == message.VacancyId))
            {
                item.IsSaved = message.IsSaved;
            }
        });
    }

    public ObservableCollection<VacancyItemViewModel> Vacancies { get; } = [];

    /// <summary>
    /// Текст пошукового рядка; зміна запускає пошук після паузи <see cref="SearchDebounce"/>.
    /// </summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Завантажується перша сторінка (старт / пошук / повтор).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Прив'язка до RefreshView («потягнути, щоб оновити»).
    /// </summary>
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowTotal))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalText))]
    public partial int TotalCount { get; set; }

    /// <summary>
    /// Показувати розширену панель фільтрів (US-002, FR-004).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterToggleText))]
    [NotifyPropertyChangedFor(nameof(IsDouPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsWorkUaPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsDjinniPanelVisible))]
    [NotifyPropertyChangedFor(nameof(DouButtonText))]
    [NotifyPropertyChangedFor(nameof(WorkUaButtonText))]
    [NotifyPropertyChangedFor(nameof(DjinniButtonText))]
    public partial bool IsFilterPanelVisible { get; set; }

    /// <summary>
    /// Обрана платформа у панелі фільтрів: "DOU", "WorkUa" або "Djinni".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDouSelected))]
    [NotifyPropertyChangedFor(nameof(IsWorkUaSelected))]
    [NotifyPropertyChangedFor(nameof(IsDjinniSelected))]
    [NotifyPropertyChangedFor(nameof(IsDouPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsWorkUaPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsDjinniPanelVisible))]
    [NotifyPropertyChangedFor(nameof(ActivePlatformTitle))]
    [NotifyPropertyChangedFor(nameof(ActivePlatformSubtitle))]
    [NotifyPropertyChangedFor(nameof(DouButtonText))]
    [NotifyPropertyChangedFor(nameof(WorkUaButtonText))]
    [NotifyPropertyChangedFor(nameof(DjinniButtonText))]
    public partial string SelectedPlatform { get; set; } = "DOU";

    public bool IsDouSelected => SelectedPlatform == "DOU";
    public bool IsWorkUaSelected => SelectedPlatform == "WorkUa";
    public bool IsDjinniSelected => SelectedPlatform == "Djinni";

    public bool IsDouPanelVisible => IsFilterPanelVisible && IsDouSelected;
    public bool IsWorkUaPanelVisible => IsFilterPanelVisible && IsWorkUaSelected;
    public bool IsDjinniPanelVisible => IsFilterPanelVisible && IsDjinniSelected;

    public string FilterToggleText => IsFilterPanelVisible ? "Сховати фільтри ▲" : "Фільтри платформ ⚙";

    public string DouButtonText => $"🔵 DOU ({DouPagesToParse} стор.) {(IsDouPanelVisible ? "▲" : "▼")}";
    public string WorkUaButtonText => $"🟠 Work.ua ({WorkUaPagesToParse} стор.) {(IsWorkUaPanelVisible ? "▲" : "▼")}";
    public string DjinniButtonText => $"🟣 Djinni ({DjinniPagesToParse} стор.) {(IsDjinniPanelVisible ? "▲" : "▼")}";

    public string ActivePlatformTitle => SelectedPlatform switch
    {
        "DOU" => "🔵 Фільтри платформи DOU (jobs.dou.ua)",
        "WorkUa" => "🟠 Фільтри платформи Work.ua (work.ua)",
        "Djinni" => "🟣 Фільтри платформи Djinni (djinni.co)",
        _ => "Фільтри платформи"
    };

    public string ActivePlatformSubtitle => SelectedPlatform switch
    {
        "DOU" => "Усі автентичні параметри пошуку jobs.dou.ua/vacancies та налаштування глибини парсингу",
        "WorkUa" => "Усі автентичні параметри пошуку work.ua/jobs та налаштування глибини парсингу",
        "Djinni" => "Усі автентичні параметри пошуку djinni.co/jobs та налаштування глибини парсингу",
        _ => string.Empty
    };

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
    public partial string WorkUaPagesToParse { get; set; } = "3";

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

    public IReadOnlyList<string> WorkUaEmploymentOptions { get; } =
    [
        "Будь-яка зайнятість",
        "Повна зайнятість (employment=74)",
        "Неповна зайнятість (employment=75)",
        "Позмінна робота (employment=76)",
        "Дистанційна робота"
    ];

    public IReadOnlyList<string> WorkUaExperienceOptions { get; } =
    [
        "Будь-який досвід",
        "Без досвіду (experience=0)",
        "До 1 року (experience=1)",
        "Від 1 року (experience=164)",
        "Від 2 років (experience=165)",
        "Від 5 років (experience=166)"
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
        "Англійська — Початковий",
        "Англійська — Середній",
        "Англійська — Вище середнього",
        "Англійська — Вільно",
        "Німецька",
        "Польська"
    ];

    public IReadOnlyList<string> WorkUaDaysOptions { get; } =
    [
        "За 30 днів (за замовчуванням)",
        "За 1 день (days=122)",
        "За 3 дні (days=123)",
        "За 7 днів (days=124)",
        "За 14 днів (days=125)"
    ];

    public IReadOnlyList<string> WorkUaSortOptions { get; } =
    [
        "За датою (спочатку нові)",
        "За датою (спочатку старі)",
        "За зарплатою (від найвищої ↓)",
        "За зарплатою (від найнижчої ↑)",
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

    /// <summary>
    /// Хоча б один запит каталогу завершився успішно.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowTotal))]
    public partial bool HasLoaded { get; private set; }

    public bool HasError => ErrorMessage is not null;

    /// <summary>
    /// Запит виконано успішно, але вакансій немає.
    /// </summary>
    public bool IsEmpty => HasLoaded && !IsLoading && ErrorMessage is null && Vacancies.Count == 0;

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

    [RelayCommand]
    private Task FetchFromSitesAsync()
    {
        string siteRequestInfo = SelectedPlatform switch
        {
            "DOU" => $"DOU (jobs.dou.ua): стор.={DouPagesToParse}, категорія='{DouSelectedCategory}', місто='{DouSelectedCity}', досвід='{DouSelectedExperience}'",
            "WorkUa" => $"Work.ua (work.ua): стор.={WorkUaPagesToParse}, категорія='{WorkUaSelectedCategory}', місто='{WorkUaSelectedCity}', досвід='{WorkUaSelectedExperience}', зайнятість='{WorkUaSelectedEmployment}'",
            "Djinni" => $"Djinni (djinni.co): стор.={DjinniPagesToParse}, категорія='{DjinniSelectedCategory}', досвід='{DjinniSelectedExperience}', зайнятість='{DjinniSelectedEmployment}', англ.='{DjinniSelectedEnglish}'",
            _ => string.Empty
        };
        StaleCacheText = $"🌐 Запит на сайт та оновлення списку [{siteRequestInfo}] (інтеграція з парсером очікується)";
        return LoadFirstPageAsync();
    }

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
    /// Скидання фільтрів та оновлення каталогу (US-002 AC-2/3, Test-07, Test-33).
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

        StaleCacheText = null;
        return LoadFirstPageAsync();
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
    /// Перше відкриття вкладки: завантажити, якщо ще не завантажували.
    /// </summary>
    [RelayCommand]
    private Task EnsureLoadedAsync() => HasLoaded || IsLoading ? Task.CompletedTask : LoadFirstPageAsync();

    /// <summary>
    /// Повтор після помилки.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RetryAsync() => LoadFirstPageAsync();

    /// <summary>
    /// Кнопка пошуку / Enter: шукати одразу, без очікування debounce.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync()
    {
        _debounceCts?.Cancel();
        return Normalize(SearchText) == _loadedQuery && HasLoaded && !HasError
            ? Task.CompletedTask
            : LoadFirstPageAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        try
        {
            await LoadFirstPageAsync(showSpinner: false);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Наступна сторінка (RemainingItemsThreshold). Ігнорується, якщо сторінок більше немає або йде завантаження.
    /// </summary>
    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoading || IsLoadingMore || !_hasNextPage || _loadCts is null)
        {
            return;
        }

        CancellationToken ct = _loadCts.Token;
        IsLoadingMore = true;
        try
        {
            PagedResult<VacancyDto> page = await _gateway.GetVacanciesAsync(_loadedQuery, _page + 1, PageSize, ct);
            Append(page);
            if (_isLocalFilterActive || HasActiveLocalFilterOrSort())
            {
                ApplyLocalFilterAndSort();
            }
        }
        catch (ApiException ex) when (!ct.IsCancellationRequested)
        {
            ErrorMessage = ex.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Замінено новим пошуком — він і керує станом.
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoadingMore = false;
            }
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

        if (Normalize(SearchText) != _loadedQuery || !HasLoaded)
        {
            await LoadFirstPageAsync();
        }
    }

    private async Task LoadFirstPageAsync(bool showSpinner = true)
    {
        _loadCts?.Cancel();
        CancellationTokenSource cts = new();
        _loadCts = cts;
        CancellationToken ct = cts.Token;

        string? query = Normalize(SearchText);
        IsLoadingMore = false;
        ErrorMessage = null;
        IsLoading = showSpinner;
        try
        {
            PagedResult<VacancyDto> page = await _gateway.GetVacanciesAsync(query, 1, PageSize, ct);
            Vacancies.Clear();
            _loadedDtos.Clear();
            _loadedQuery = query;
            Append(page);
            HasLoaded = true;

            if (_isLocalFilterActive || HasActiveLocalFilterOrSort())
            {
                await FetchRemainingPagesForLocalFilterAsync(ct);
                ApplyLocalFilterAndSort();
            }
        }
        catch (ApiException ex) when (!ct.IsCancellationRequested)
        {
            ErrorMessage = ex.Message;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Замінено новішим завантаженням.
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    private void Append(PagedResult<VacancyDto> page)
    {
        foreach (VacancyDto vacancy in page.Items)
        {
            _loadedDtos.Add(vacancy);
            Vacancies.Add(new VacancyItemViewModel(vacancy, _gateway, _messenger, message => ErrorMessage = message, _urlLauncher, _distanceService));
        }

        _page = page.Page;
        _hasNextPage = page.HasNextPage;
        TotalCount = page.TotalCount;
    }

    private async Task ExecuteLocalFilterAndSortAsync(bool updateBanner)
    {
        _isLocalFilterActive = true;

        if (!HasLoaded)
        {
            await LoadFirstPageAsync();
        }
        else if (_hasNextPage && _loadCts is not null && !IsLoading)
        {
            CancellationToken ct = _loadCts.Token;
            IsLoading = true;
            try
            {
                await FetchRemainingPagesForLocalFilterAsync(ct);
            }
            catch (ApiException ex) when (!ct.IsCancellationRequested)
            {
                ErrorMessage = ex.Message;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                {
                    IsLoading = false;
                }
            }
        }

        ApplyLocalFilterAndSort();

        if (updateBanner)
        {
            string whiteInfo = WhiteListWords.Count > 0 ? $", вайт-лист=[{string.Join(", ", WhiteListWords)}]" : string.Empty;
            string banInfo = BanListWords.Count > 0 ? $", бан-слова=[{string.Join(", ", BanListWords)}]" : string.Empty;
            StaleCacheText = $"⚡ Відфільтровано вакансії з БД: знайдено {TotalCount} із {_loadedDtos.Count} (сортування: {SelectedSortOption}{whiteInfo}{banInfo})";
        }
    }

    private async Task FetchRemainingPagesForLocalFilterAsync(CancellationToken ct)
    {
        int safetyPages = 0;
        while (_hasNextPage && safetyPages < 25 && !ct.IsCancellationRequested)
        {
            safetyPages++;
            PagedResult<VacancyDto> nextPage = await _gateway.GetVacanciesAsync(_loadedQuery, _page + 1, PageSize, ct);
            foreach (VacancyDto dto in nextPage.Items)
            {
                if (_loadedDtos.All(existing => existing.Id != dto.Id))
                {
                    _loadedDtos.Add(dto);
                }
            }

            _page = nextPage.Page;
            _hasNextPage = nextPage.HasNextPage;
        }
    }

    private bool HasActiveLocalFilterOrSort()
    {
        return SelectedSortOption != SortOptions[0]
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
            Vacancies.Add(new VacancyItemViewModel(adjusted, _gateway, _messenger, message => ErrorMessage = message, _urlLauncher, _distanceService));
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

        // 2. Вайт-лист слів: перевіряється тільки за назвою посади та списком навичок Skills
        if (WhiteListWords.Count > 0)
        {
            if (!WhiteListWords.Any(word => ContainsKeyword(v, word, titleOnly)))
            {
                return false;
            }
        }

        // 3. Бан-слова: виключає вакансію, якщо слово знайдено в назві або навичках
        if (BanListWords.Count > 0)
        {
            if (BanListWords.Any(word => ContainsKeyword(v, word, titleOnly: false)))
            {
                return false;
            }
        }

        // 4. Пошуковий запит (назва посади, навички або компанія)
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
            || v.Category.Contains(word, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<VacancyDto> SortVacancies(IEnumerable<VacancyDto> items, string sortOption)
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
