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
    public partial bool IsFilterPanelVisible { get; set; }

    public string FilterToggleText => IsFilterPanelVisible ? "Сховати фільтри ▲" : "Фільтри ⚙";

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
    private Task ApplyFiltersAsync() => LoadFirstPageAsync();

    /// <summary>
    /// Скидання фільтрів та оновлення каталогу (US-002 AC-2/3, Test-07, Test-33).
    /// </summary>
    [RelayCommand]
    public Task ResetFiltersAsync()
    {
        SearchText = string.Empty;
        SkillFilter = string.Empty;
        SelectedFormat = FormatOptions[0];
        SelectedCategory = CategoryOptions[0];
        MinSalaryFilter = string.Empty;
        MaxSalaryFilter = string.Empty;
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
            _loadedQuery = query;
            Append(page);
            HasLoaded = true;
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
            Vacancies.Add(new VacancyItemViewModel(vacancy, _gateway, _messenger, message => ErrorMessage = message, _urlLauncher, _distanceService));
        }

        _page = page.Page;
        _hasNextPage = page.HasNextPage;
        TotalCount = page.TotalCount;
    }

    private static string? Normalize(string? search) => string.IsNullOrWhiteSpace(search) ? null : search.Trim();
}
