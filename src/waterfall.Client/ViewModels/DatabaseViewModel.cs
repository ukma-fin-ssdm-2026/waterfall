// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Collections;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Normalization;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Вкладка «База вакансій»: при відкритті завантажує повний перелік усіх вакансій із БД (з усіх 4 сайтів —
/// DOU, Work.ua, Djinni, Robota.ua), підтримує максимально глибокий пошук, крос-сайтову фільтрацію,
/// офлайн-роботу через локальну SQLite БД та двосторонню синхронізацію із сервером.
/// </summary>
public partial class DatabaseViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);
    private const int ServerPageSize = 100;
    private const int MaxServerPages = 200;

    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _time;
    private readonly IUrlLauncher? _urlLauncher;
    private readonly IDistanceCalculatorService? _distanceService;
    private readonly ILocalVacancyStore? _localStore;
    private readonly IVacancySyncService? _syncService;
    private readonly IFileDialogService? _fileDialog;

    private readonly List<VacancyDto> _allVacancies = [];
    private readonly HashSet<Guid> _viewedIds = [];
    private readonly ConcurrentDictionary<Guid, double?> _distanceCache = new();
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _distancePrefetchCts;
    private bool _suppressAutoFilter;
    private bool _hasLoadedOnce;

    public DatabaseViewModel(
        IHttpGatewayService gateway,
        IMessenger messenger,
        TimeProvider time,
        IUrlLauncher? urlLauncher = null,
        IDistanceCalculatorService? distanceService = null,
        ILocalVacancyStore? localStore = null,
        IVacancySyncService? syncService = null,
        IFileDialogService? fileDialog = null)
    {
        _gateway = gateway;
        _messenger = messenger;
        _time = time;
        _urlLauncher = urlLauncher;
        _distanceService = distanceService;
        _localStore = localStore;
        _syncService = syncService;
        _fileDialog = fileDialog;

        CategoryGroupOptions = ["Усі групи", .. VacancyCategoryMap.All];
        AvailableCategories.Add("Усі категорії");
        AvailableLocations.Add("Усі міста");

        messenger.Register<DatabaseViewModel, VacancySaveChangedMessage>(this, static (vm, message) =>
        {
            for (int i = 0; i < vm._allVacancies.Count; i++)
            {
                if (vm._allVacancies[i].Id == message.VacancyId)
                {
                    vm._allVacancies[i] = vm._allVacancies[i] with { IsSaved = message.IsSaved };
                }
            }

            foreach (VacancyItemViewModel item in vm.Vacancies.Where(v => v.Id == message.VacancyId))
            {
                item.IsSaved = message.IsSaved;
            }

            if (vm.OnlySaved)
            {
                vm.ApplyFiltersAndSort();
            }
        });

        messenger.Register<DatabaseViewModel, VacancyViewedMessage>(this, static (vm, message) =>
        {
            vm._viewedIds.Add(message.VacancyId);
        });

        messenger.Register<DatabaseViewModel, VacanciesUpdatedMessage>(this, static (vm, message) =>
        {
            if (vm._hasLoadedOnce && !vm.IsSyncing)
            {
                _ = vm.LoadDatabaseAsync();
            }
        });

        messenger.Register<DatabaseViewModel, VacanciesSyncedMessage>(this, static (vm, message) =>
        {
            // Якщо синхронізацію запустила сама вкладка «База вакансій» (IsSyncing == true),
            // вона перезавантажить список після завершення SyncDatabaseAsync — не запускаємо дублюючий GET.
            if (message.Result.Succeeded && vm._hasLoadedOnce && !vm.IsLoading && !vm.IsSyncing)
            {
                _ = vm.LoadDatabaseAsync();
            }
        });

        messenger.Register<DatabaseViewModel, UserAddressChangedMessage>(this, static (vm, message) =>
        {
            vm._distanceCache.Clear();
            if (CatalogViewModel.IsDistanceSort(vm.SelectedSortOption) && vm._hasLoadedOnce)
            {
                _ = vm.ApplyFiltersWithDistanceAsync();
            }
        });
    }

    private readonly BulkObservableCollection<VacancyItemViewModel> _vacancies = new();
    public ObservableCollection<VacancyItemViewModel> Vacancies => _vacancies;
    public ObservableCollection<string> AvailableCategories { get; } = [];
    public ObservableCollection<string> AvailableLocations { get; } = [];
    public ObservableCollection<string> WhiteListWords { get; } = [];
    public ObservableCollection<string> BanListWords { get; } = [];

    public IReadOnlyList<string> CategoryGroupOptions { get; }

    public IReadOnlyList<string> WorkFormatOptions { get; } =
    [
        "Усі формати",
        "Віддалено",
        "Гібрид",
        "Офіс",
        "Не вказано"
    ];

    public IReadOnlyList<string> ExperienceOptions { get; } =
    [
        "Будь-який досвід",
        "Без досвіду (0)",
        "До 1 року",
        "1–3 роки",
        "3–5 років",
        "5+ років",
        "Не вказано"
    ];

    public IReadOnlyList<string> SalaryCurrencyOptions { get; } = ["$ USD", "₴ ГРН", "€ EUR"];

    public IReadOnlyList<string> FreshnessOptions { get; } =
    [
        "За весь час",
        "За 24 години",
        "За 3 дні",
        "За 7 днів",
        "За 14 днів",
        "За 30 днів"
    ];

    public IReadOnlyList<string> StatusOptions { get; } =
    [
        "Усі статуси",
        "Лише активні",
        "Лише архівні"
    ];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "За датою публікації (спочатку нові)",
        "За датою публікації (спочатку старі)",
        "За датою додавання в БД (спочатку нові)",
        "За зарплатою (від найвищої ↓)",
        "За зарплатою (від найнижчої ↑)",
        "За досвідом (від меншого ↑)",
        "За досвідом (від більшого ↓)",
        "За відстанню (спочатку ближчі)",
        "За відстанню (спочатку дальші)",
        "За назвою посади (А → Я)",
        "За назвою посади (Я → А)",
        "За компанією (А → Я)",
        "За категорією (А → Я)"
    ];

    // ==================== СТАН ЗАВАНТАЖЕННЯ ТА СИНХРОНІЗАЦІЇ ====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(SyncDatabaseCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncDatabaseCommand))]
    public partial bool IsSyncing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionBadgeText))]
    public partial bool IsOfflineMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusBanner))]
    public partial string? StatusBannerText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSummaryText))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int TotalInDatabase { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalSummaryText))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int FilteredCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DouFilterLabel))]
    public partial int DouCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkUaFilterLabel))]
    public partial int WorkUaCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DjinniFilterLabel))]
    public partial int DjinniCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RobotaUaFilterLabel))]
    public partial int RobotaUaCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncBadgeText))]
    [NotifyPropertyChangedFor(nameof(SyncButtonText))]
    [NotifyPropertyChangedFor(nameof(HasUnsyncedChanges))]
    public partial int UnsyncedCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncBadgeText))]
    [NotifyPropertyChangedFor(nameof(LastSyncedMenuText))]
    public partial DateTimeOffset? LastSyncedAt { get; set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasStatusBanner => !string.IsNullOrWhiteSpace(StatusBannerText);
    public bool HasUnsyncedChanges => UnsyncedCount > 0;
    public bool IsEmpty => !IsLoading && FilteredCount == 0;
    public string SyncButtonText => UnsyncedCount > 0 ? $"Синхронізувати ({UnsyncedCount})" : "Синхронізувати";
    public string LastSyncedMenuText => LastSyncedAt.HasValue
        ? $"Оновлено о {LastSyncedAt.Value.ToLocalTime():HH:mm}"
        : "Ще не синхронізовано";
    public bool IsCustomSortActive => !string.Equals(SelectedSortOption, SortOptions[0], StringComparison.Ordinal);
    public bool IsFilterHighlightActive => IsFilterPanelVisible || HasActiveFilters;

    public bool HasActiveFilters =>
        !IncludeDou || !IncludeWorkUa || !IncludeDjinni || !IncludeRobotaUa
        || !string.Equals(SelectedCategoryGroup, CategoryGroupOptions[0], StringComparison.Ordinal)
        || !string.Equals(SelectedCategory, "Усі категорії", StringComparison.Ordinal)
        || !string.Equals(SelectedLocation, "Усі міста", StringComparison.Ordinal)
        || !string.Equals(SelectedWorkFormat, WorkFormatOptions[0], StringComparison.Ordinal)
        || !string.Equals(SelectedExperience, ExperienceOptions[0], StringComparison.Ordinal)
        || !string.Equals(SelectedFreshness, FreshnessOptions[0], StringComparison.Ordinal)
        || !string.Equals(SelectedStatus, StatusOptions[0], StringComparison.Ordinal)
        || !string.IsNullOrWhiteSpace(LocationFilter)
        || !string.IsNullOrWhiteSpace(CompanyFilter)
        || !string.IsNullOrWhiteSpace(SkillsFilter)
        || !string.IsNullOrWhiteSpace(MinSalaryFilter)
        || !string.IsNullOrWhiteSpace(MaxSalaryFilter)
        || OnlyWithSalary
        || IncludeNoSalary
        || OnlySaved
        || HideViewed
        || TitleOnly
        || WhiteListWords.Count > 0
        || BanListWords.Count > 0;

    public string ConnectionBadgeText => IsOfflineMode ? "Офлайн (локальна БД)" : "Онлайн (БД сервера)";

    public string SyncBadgeText
    {
        get
        {
            string pending = UnsyncedCount > 0 ? $"Офлайн-змін: {UnsyncedCount}" : "Синхронізовано";
            if (LastSyncedAt.HasValue)
            {
                return $"{pending} · {LastSyncedAt.Value.ToLocalTime():HH:mm}";
            }

            return pending;
        }
    }

    public string TotalSummaryText =>
        $"Показано {FilteredCount} із {TotalInDatabase} вакансій у базі (DOU: {DouCount} · Work.ua: {WorkUaCount} · Djinni: {DjinniCount} · Robota.ua: {RobotaUaCount})";

    public string DouFilterLabel => $"DOU ({DouCount})";
    public string WorkUaFilterLabel => $"Work.ua ({WorkUaCount})";
    public string DjinniFilterLabel => $"Djinni ({DjinniCount})";
    public string RobotaUaFilterLabel => $"Robota.ua ({RobotaUaCount})";

    // ==================== ПАНЕЛЬ ФІЛЬТРІВ (МАКСИМУМ ПО 4 САЙТАХ) ====================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterToggleText))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool IsFilterPanelVisible { get; set; }

    public string FilterToggleText => IsFilterPanelVisible ? "Сховати фільтри ▲" : "Усі фільтри ⚙";

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool IncludeDou { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool IncludeWorkUa { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool IncludeDjinni { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool IncludeRobotaUa { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SelectedCategoryGroup { get; set; } = "Усі групи";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SelectedCategory { get; set; } = "Усі категорії";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SelectedWorkFormat { get; set; } = "Усі формати";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SelectedExperience { get; set; } = "Будь-який досвід";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SelectedLocation { get; set; } = "Усі міста";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string LocationFilter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string CompanyFilter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SkillsFilter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string MinSalaryFilter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string MaxSalaryFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedSalaryCurrency { get; set; } = "$ USD";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SelectedFreshness { get; set; } = "За весь час";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial string SelectedStatus { get; set; } = "Усі статуси";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomSortActive))]
    public partial string SelectedSortOption { get; set; } = "За датою публікації (спочатку нові)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool OnlyWithSalary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool IncludeNoSalary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool OnlySaved { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool HideViewed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    [NotifyPropertyChangedFor(nameof(IsFilterHighlightActive))]
    public partial bool TitleOnly { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhiteListMatchText))]
    public partial bool WhiteListMatchAll { get; set; }

    public string WhiteListMatchText => WhiteListMatchAll ? "Усі слова" : "Будь-яке слово";

    [ObservableProperty]
    public partial string NewWhiteListWord { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewBanListWord { get; set; } = string.Empty;

    public bool HasWhiteListWords => WhiteListWords.Count > 0;
    public bool HasBanListWords => BanListWords.Count > 0;

    // ==================== АВТО-РЕАКЦІЯ НА ЗМІНУ ФІЛЬТРІВ ====================

    partial void OnSearchTextChanged(string value) => _ = DebounceSearchAsync();

    partial void OnIncludeDouChanged(bool value) => ReapplyIfReady();
    partial void OnIncludeWorkUaChanged(bool value) => ReapplyIfReady();
    partial void OnIncludeDjinniChanged(bool value) => ReapplyIfReady();
    partial void OnIncludeRobotaUaChanged(bool value) => ReapplyIfReady();
    partial void OnSelectedCategoryGroupChanged(string value) => ReapplyIfReady();
    partial void OnSelectedCategoryChanged(string value) => ReapplyIfReady();
    partial void OnSelectedWorkFormatChanged(string value) => ReapplyIfReady();
    partial void OnSelectedExperienceChanged(string value) => ReapplyIfReady();
    partial void OnSelectedLocationChanged(string value) => ReapplyIfReady();
    partial void OnSelectedSalaryCurrencyChanged(string value) => ReapplyIfReady();
    partial void OnSelectedFreshnessChanged(string value) => ReapplyIfReady();
    partial void OnSelectedStatusChanged(string value) => ReapplyIfReady();
    partial void OnOnlyWithSalaryChanged(bool value) => ReapplyIfReady();
    partial void OnIncludeNoSalaryChanged(bool value) => ReapplyIfReady();
    partial void OnOnlySavedChanged(bool value) => ReapplyIfReady();
    partial void OnHideViewedChanged(bool value) => ReapplyIfReady();
    partial void OnTitleOnlyChanged(bool value) => ReapplyIfReady();
    partial void OnWhiteListMatchAllChanged(bool value) => ReapplyIfReady();

    partial void OnSelectedSortOptionChanged(string value)
    {
        if (_suppressAutoFilter || !_hasLoadedOnce)
        {
            return;
        }

        if (VacancyFilterEngine.IsDistanceSort(value))
        {
            _ = ApplyFiltersWithDistanceAsync();
        }
        else
        {
            ApplyFiltersAndSort();
        }
    }

    private void ReapplyIfReady()
    {
        if (!_suppressAutoFilter && _hasLoadedOnce)
        {
            ApplyFiltersAndSort();
        }
    }

    private async Task DebounceSearchAsync()
    {
        CancellationTokenSource? previous = _debounceCts;
        CancellationTokenSource cts = new();
        _debounceCts = cts;
        if (previous is not null)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }
        try
        {
            await Task.Delay(SearchDebounce, _time, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_suppressAutoFilter && _hasLoadedOnce)
        {
            string? trimmed = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
            if (trimmed is not null && !IsOfflineMode)
            {
                _ = _gateway.RecordSearchAsync(trimmed, CancellationToken.None);
            }

            ApplyFiltersAndSort();
        }
    }

    // ==================== КОМАНДИ ЗАВАНТАЖЕННЯ ТА СИНХРОНІЗАЦІЇ ====================

    /// <summary>Викликається при перемиканні на вкладку: завантажує базу при першому відкритті (повторні переходи миттєві з RAM).</summary>
    [RelayCommand]
    public async Task EnsureLoadedAsync()
    {
        if (_hasLoadedOnce && _allVacancies.Count > 0)
        {
            return;
        }

        await LoadDatabaseAsync();
    }

    /// <summary>Оновлює повний список вакансій із сервера (або локальної SQLite БД в офлайн-режимі).</summary>
    [RelayCommand]
    public async Task LoadDatabaseAsync()
    {
        if (_loadCts is not null)
        {
            await _loadCts.CancelAsync();
            _loadCts.Dispose();
        }

        CancellationTokenSource cts = new();
        _loadCts = cts;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Якщо є несинхронізовані офлайн-вакансії і сервер доступний — спочатку обмінюємося даними
            if (_syncService is not null && _localStore is not null)
            {
                LocalVacancyStats preStats = await _localStore.GetStatsAsync(cts.Token);
                if (preStats.UnsyncedCount > 0)
                {
                    VacancySyncResult autoSync = await _syncService.SyncWithServerAsync(fullSync: false, cts.Token);
                    if (autoSync.Succeeded)
                    {
                        StatusBannerText = $"Синхронізовано: надіслано {autoSync.UploadedToServer}, отримано {autoSync.DownloadedFromServer}";
                    }
                }
            }

            List<VacancyDto> loaded = await FetchAllServerVacanciesAsync(cts.Token);
            IsOfflineMode = false;

            if (_localStore is not null && loaded.Count > 0)
            {
                await _localStore.SaveVacanciesAsync(loaded, isSynced: true, cts.Token);
            }

            SetDatabaseVacancies(loaded);
            await RefreshLocalStatsAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Скасовано новим оновленням
        }
        catch (ApiException ex) when (ex.StatusCode is null && _localStore is not null)
        {
            // Офлайн-режим: завантажуємо повну локальну базу SQLite клієнта
            IReadOnlyList<VacancyDto> local = await _localStore.GetAllAsync(CancellationToken.None);
            IsOfflineMode = true;
            SetDatabaseVacancies(local.ToList());
            await RefreshLocalStatsAsync(CancellationToken.None);
            StatusBannerText = $"Офлайн-режим: відкрито локальну базу ({local.Count} вакансій)";
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            if (_localStore is not null && _allVacancies.Count == 0)
            {
                IReadOnlyList<VacancyDto> local = await _localStore.GetAllAsync(CancellationToken.None);
                if (local.Count > 0)
                {
                    SetDatabaseVacancies(local.ToList());
                }
            }
        }
        finally
        {
            if (_loadCts == cts)
            {
                IsLoading = false;
                IsRefreshing = false;
            }
        }
    }

    private async Task<List<VacancyDto>> FetchAllServerVacanciesAsync(CancellationToken ct)
    {
        List<VacancyDto> all = [];
        HashSet<Guid> seen = [];

        for (int page = 1; page <= MaxServerPages; page++)
        {
            PagedResult<VacancyDto> pageResult = await _gateway.GetVacanciesAsync(
                new VacancyQueryOptions
                {
                    Page = page,
                    PageSize = ServerPageSize,
                    OnlyActive = false,
                    SortBy = "publishedAt",
                    SortOrder = "desc"
                },
                ct);

            foreach (VacancyDto dto in pageResult.Items)
            {
                if (seen.Add(dto.Id))
                {
                    all.Add(dto);
                }
            }

            if (!pageResult.HasNextPage || pageResult.Items.Count == 0 || (pageResult.TotalCount > 0 && all.Count >= pageResult.TotalCount))
            {
                break;
            }
        }

        return all;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadDatabaseAsync();
    }

    /// <summary>Запускає двосторонню синхронізацію локальної БД клієнта із сервером вручну.</summary>
    [RelayCommand(CanExecute = nameof(CanSyncDatabase))]
    public async Task SyncDatabaseAsync()
    {
        if (_syncService is null)
        {
            StatusBannerText = "Сервіс синхронізації не підключено.";
            return;
        }

        IsSyncing = true;
        ErrorMessage = null;
        try
        {
            VacancySyncResult result = await _syncService.SyncWithServerAsync(fullSync: true, CancellationToken.None);
            if (result.Succeeded)
            {
                IsOfflineMode = false;
                StatusBannerText = $"Синхронізація завершена: надіслано {result.UploadedToServer}, отримано {result.DownloadedFromServer} (нових: {result.AddedLocally})";
                await LoadDatabaseAsync();
            }
            else
            {
                IsOfflineMode = true;
                StatusBannerText = $"Помилка синхронізації: {result.ErrorMessage}";
            }
        }
        finally
        {
            IsSyncing = false;
            await RefreshLocalStatsAsync(CancellationToken.None);
        }
    }

    private bool CanSyncDatabase() => !IsSyncing && !IsLoading;

    private async Task RefreshLocalStatsAsync(CancellationToken ct)
    {
        if (_localStore is null)
        {
            return;
        }

        try
        {
            LocalVacancyStats stats = await _localStore.GetStatsAsync(ct);
            UnsyncedCount = stats.UnsyncedCount;
            LastSyncedAt = stats.LastSyncedAt;
        }
        catch
        {
            // Ігноруємо допоміжну помилку читання статистики
        }
    }

    // ==================== КОМАНДИ ФІЛЬТРАЦІЇ, ПОШУКУ ТА СОРТУВАННЯ ====================

    [RelayCommand]
    private void ToggleFilterPanel() => IsFilterPanelVisible = !IsFilterPanelVisible;

    [RelayCommand]
    private void ToggleDouFilter() => IncludeDou = !IncludeDou;

    [RelayCommand]
    private void ToggleWorkUaFilter() => IncludeWorkUa = !IncludeWorkUa;

    [RelayCommand]
    private void ToggleDjinniFilter() => IncludeDjinni = !IncludeDjinni;

    [RelayCommand]
    private void ToggleRobotaUaFilter() => IncludeRobotaUa = !IncludeRobotaUa;

    [RelayCommand]
    private void SelectAllSources()
    {
        _suppressAutoFilter = true;
        try
        {
            IncludeDou = true;
            IncludeWorkUa = true;
            IncludeDjinni = true;
            IncludeRobotaUa = true;
        }
        finally
        {
            _suppressAutoFilter = false;
        }

        ReapplyIfReady();
    }

    [RelayCommand]
    private void ToggleOnlyWithSalary() => OnlyWithSalary = !OnlyWithSalary;

    [RelayCommand]
    private void ToggleIncludeNoSalary() => IncludeNoSalary = !IncludeNoSalary;

    [RelayCommand]
    private void ToggleOnlySaved() => OnlySaved = !OnlySaved;

    [RelayCommand]
    private void ToggleHideViewed() => HideViewed = !HideViewed;

    [RelayCommand]
    private void ToggleTitleOnly() => TitleOnly = !TitleOnly;

    [RelayCommand]
    private void ToggleWhiteListMatchAll() => WhiteListMatchAll = !WhiteListMatchAll;

    private void NotifyWordListChanged()
    {
        OnPropertyChanged(nameof(HasWhiteListWords));
        OnPropertyChanged(nameof(HasBanListWords));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(IsFilterHighlightActive));
    }

    [RelayCommand]
    private void ApplyFilters()
    {
        bool wordsChanged = false;
        if (VacancyFilterEngine.TryAddWords(NewWhiteListWord, WhiteListWords))
        {
            NewWhiteListWord = string.Empty;
            wordsChanged = true;
        }

        if (VacancyFilterEngine.TryAddWords(NewBanListWord, BanListWords))
        {
            NewBanListWord = string.Empty;
            wordsChanged = true;
        }

        if (wordsChanged)
        {
            NotifyWordListChanged();
        }

        if (VacancyFilterEngine.IsDistanceSort(SelectedSortOption))
        {
            _ = ApplyFiltersWithDistanceAsync();
        }
        else
        {
            ApplyFiltersAndSort();
        }
    }

    [RelayCommand]
    private void AddWhiteListWord()
    {
        if (VacancyFilterEngine.TryAddWords(NewWhiteListWord, WhiteListWords))
        {
            NewWhiteListWord = string.Empty;
            NotifyWordListChanged();
            ReapplyIfReady();
        }
    }

    [RelayCommand]
    private void RemoveWhiteListWord(string? word)
    {
        if (!string.IsNullOrWhiteSpace(word) && WhiteListWords.Remove(word))
        {
            NotifyWordListChanged();
            ReapplyIfReady();
        }
    }

    [RelayCommand]
    private void ClearWhiteListWords()
    {
        if (WhiteListWords.Count > 0)
        {
            WhiteListWords.Clear();
            NotifyWordListChanged();
            ReapplyIfReady();
        }
    }

    [RelayCommand]
    private void AddBanListWord()
    {
        if (VacancyFilterEngine.TryAddWords(NewBanListWord, BanListWords))
        {
            NewBanListWord = string.Empty;
            NotifyWordListChanged();
            ReapplyIfReady();
        }
    }

    [RelayCommand]
    private void RemoveBanListWord(string? word)
    {
        if (!string.IsNullOrWhiteSpace(word) && BanListWords.Remove(word))
        {
            NotifyWordListChanged();
            ReapplyIfReady();
        }
    }

    [RelayCommand]
    private void ClearBanListWords()
    {
        if (BanListWords.Count > 0)
        {
            BanListWords.Clear();
            NotifyWordListChanged();
            ReapplyIfReady();
        }
    }

    [RelayCommand]
    public void ResetFilters()
    {
        _suppressAutoFilter = true;
        try
        {
            SearchText = string.Empty;
            IncludeDou = true;
            IncludeWorkUa = true;
            IncludeDjinni = true;
            IncludeRobotaUa = true;
            SelectedCategoryGroup = CategoryGroupOptions[0];
            SelectedCategory = AvailableCategories[0];
            SelectedWorkFormat = WorkFormatOptions[0];
            SelectedExperience = ExperienceOptions[0];
            SelectedLocation = AvailableLocations[0];
            LocationFilter = string.Empty;
            CompanyFilter = string.Empty;
            SkillsFilter = string.Empty;
            MinSalaryFilter = string.Empty;
            MaxSalaryFilter = string.Empty;
            SelectedSalaryCurrency = SalaryCurrencyOptions[0];
            SelectedFreshness = FreshnessOptions[0];
            SelectedStatus = StatusOptions[0];
            SelectedSortOption = SortOptions[0];
            OnlyWithSalary = false;
            IncludeNoSalary = false;
            OnlySaved = false;
            HideViewed = false;
            TitleOnly = false;
            WhiteListMatchAll = false;
            WhiteListWords.Clear();
            BanListWords.Clear();
            NewWhiteListWord = string.Empty;
            NewBanListWord = string.Empty;
            NotifyWordListChanged();
        }
        finally
        {
            _suppressAutoFilter = false;
        }

        ApplyFiltersAndSort();
    }

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    // ==================== ЕКСПОРТ / ІМПОРТ JSON (ТАКОЖ ДЛЯ ОФЛАЙН-НАПОВНЕННЯ) ====================

    private static string DefaultExportPath()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Waterfall");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "waterfall_db_export.json");
    }

    [RelayCommand]
    public async Task ExportDatabaseAsync()
    {
        string? path = _fileDialog is not null
            ? await _fileDialog.PickJsonFileToSaveAsync("waterfall_db_export.json", CancellationToken.None)
            : DefaultExportPath();

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await ExportDatabaseToAsync(path);
    }

    public async Task ExportDatabaseToAsync(string path)
    {
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
            StatusBannerText = $"Експортовано {toExport.Count} вакансій у {path}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Помилка експорту бази: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ImportDatabaseAsync()
    {
        string? path = _fileDialog is not null
            ? await _fileDialog.PickJsonFileToOpenAsync(CancellationToken.None)
            : DefaultExportPath();

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await ImportDatabaseFromAsync(path);
    }

    public async Task ImportDatabaseFromAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                StatusBannerText = $"Файл не знайдено ({path}).";
                return;
            }

            string json = await File.ReadAllTextAsync(path, CancellationToken.None);
            List<VacancyDto>? imported = JsonSerializer.Deserialize(json, ClientJsonContext.Default.ListVacancyDto);
            if (imported is null || imported.Count == 0)
            {
                StatusBannerText = "Файл імпорту порожній.";
                return;
            }

            if (_localStore is not null)
            {
                await _localStore.SaveVacanciesAsync(imported, isSynced: false, CancellationToken.None);
                IReadOnlyList<VacancyDto> merged = await _localStore.GetAllAsync(CancellationToken.None);
                SetDatabaseVacancies(merged.ToList());
                await RefreshLocalStatsAsync(CancellationToken.None);
            }
            else
            {
                List<VacancyDto> combined = _allVacancies.Concat(imported)
                    .GroupBy(v => v.Id)
                    .Select(g => g.Last())
                    .ToList();
                SetDatabaseVacancies(combined);
            }

            StatusBannerText = $"Імпортовано {imported.Count} вакансій";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Помилка імпорту бази: {ex.Message}";
        }
    }

    // ==================== ВНУТРІШНЯ ЛОГІКА ФІЛЬТРАЦІЇ ТА СОРТУВАННЯ ====================

    private void SetDatabaseVacancies(List<VacancyDto> vacancies)
    {
        _allVacancies.Clear();
        _allVacancies.AddRange(vacancies);
        _hasLoadedOnce = true;

        TotalInDatabase = _allVacancies.Count;
        DouCount = _allVacancies.Count(v => v.Source == JobSource.Dou);
        WorkUaCount = _allVacancies.Count(v => v.Source == JobSource.WorkUa);
        DjinniCount = _allVacancies.Count(v => v.Source == JobSource.Djinni);
        RobotaUaCount = _allVacancies.Count(v => v.Source == JobSource.RobotaUa);

        RefreshDynamicPickers();
        ApplyFiltersAndSort();
    }

    private void RefreshDynamicPickers()
    {
        _suppressAutoFilter = true;
        try
        {
            string prevCat = SelectedCategory;
            List<string> categories =
            [
                "Усі категорії",
                .. _allVacancies
                    .Select(v => v.Category?.Trim())
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            ];
            ReplaceCollection(AvailableCategories, categories);
            SelectedCategory = AvailableCategories.Contains(prevCat) ? prevCat : AvailableCategories[0];

            string prevLoc = SelectedLocation;
            List<string> locations =
            [
                "Усі міста",
                .. _allVacancies
                    .Select(ExtractCityToken)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase)
            ];
            ReplaceCollection(AvailableLocations, locations);
            SelectedLocation = AvailableLocations.Contains(prevLoc) ? prevLoc : AvailableLocations[0];
        }
        finally
        {
            _suppressAutoFilter = false;
        }
    }

    private async Task ApplyFiltersWithDistanceAsync()
    {
        await VacancyFilterEngine.PrecalculateDistancesAsync(
            _allVacancies.Where(MatchesAllFilters), _distanceService, _distanceCache, CancellationToken.None);
        ApplyFiltersAndSort();
    }

    private void ApplyFiltersAndSort()
    {
        List<VacancyDto> matched = _allVacancies.Where(MatchesAllFilters).ToList();
        List<VacancyDto> sorted = VacancyFilterEngine.SortVacancies(matched, SelectedSortOption, GetDistanceKm).ToList();

        List<VacancyItemViewModel> cards = sorted.Select(CreateCard).ToList();
        _vacancies.ReplaceAll(cards);

        _distancePrefetchCts?.Cancel();
        CancellationTokenSource prefetchCts = new();
        _distancePrefetchCts = prefetchCts;
        _ = VacancyFilterEngine.PrefetchDistancesAsync(cards, _distanceService, prefetchCts.Token);

        FilteredCount = Vacancies.Count;
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(IsFilterHighlightActive));
    }

    private VacancyItemViewModel CreateCard(VacancyDto dto)
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

    private bool MatchesAllFilters(VacancyDto v)
    {
        // 1. Мультивибір із 4 сайтів-джерел
        bool sourceMatches = v.Source switch
        {
            JobSource.Dou => IncludeDou,
            JobSource.WorkUa => IncludeWorkUa,
            JobSource.Djinni => IncludeDjinni,
            JobSource.RobotaUa => IncludeRobotaUa,
            _ => IncludeDou && IncludeWorkUa && IncludeDjinni && IncludeRobotaUa
        };
        if (!sourceMatches)
        {
            return false;
        }

        // 2. Широка канонічна категорія (уніфікована для всіх 4 сайтів)
        if (SelectedCategoryGroup != CategoryGroupOptions[0])
        {
            string group = !string.IsNullOrWhiteSpace(v.CategoryGroup)
                ? v.CategoryGroup
                : VacancyCategoryMap.Resolve(v.Category);
            if (!string.Equals(group, SelectedCategoryGroup, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 3. Точна категорія
        if (SelectedCategory != "Усі категорії" &&
            !string.Equals(v.Category?.Trim(), SelectedCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 4. Формат роботи (нормалізовані прапорці + текстовий фолбек)
        if (!MatchesWorkFormat(v, SelectedWorkFormat))
        {
            return false;
        }

        // 5. Досвід роботи (нормалізовані місяці + текстовий фолбек)
        if (!MatchesExperience(v, SelectedExperience))
        {
            return false;
        }

        // 6. Локація / місто (випадаючий список + вільний текст)
        if (SelectedLocation != "Усі міста" && !MatchesLocation(v, SelectedLocation))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(LocationFilter) && !MatchesLocation(v, LocationFilter.Trim()))
        {
            return false;
        }

        // 7. Компанія
        if (!string.IsNullOrWhiteSpace(CompanyFilter) &&
            !v.Company.Contains(CompanyFilter.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 8. Обов'язкові навички (через кому, всі мають бути в навичках/назві/описі)
        if (!string.IsNullOrWhiteSpace(SkillsFilter))
        {
            string[] requiredSkills = SkillsFilter.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (requiredSkills.Any(skill => !VacancyFilterEngine.ContainsWord(v, skill, titleOnly: false, includeLocationFields: true)))
            {
                return false;
            }
        }

        // 9. Зарплата з крос-валютною конвертацією (USD / UAH / EUR)
        if (!VacancyFilterEngine.MatchesSalary(v, OnlyWithSalary, MinSalaryFilter, MaxSalaryFilter, SelectedSalaryCurrency, IncludeNoSalary))
        {
            return false;
        }

        // 10. Період публікації
        if (!MatchesFreshness(v, SelectedFreshness, _time.GetUtcNow()))
        {
            return false;
        }

        // 11. Статус активності
        if (SelectedStatus == "Лише активні" && !v.IsActive)
        {
            return false;
        }

        if (SelectedStatus == "Лише архівні" && v.IsActive)
        {
            return false;
        }

        // 12. Обране та переглянуті
        if (OnlySaved && !v.IsSaved)
        {
            return false;
        }

        if (HideViewed && (v.IsViewed || _viewedIds.Contains(v.Id)))
        {
            return false;
        }

        // 13–14. Вайт-лист та Бан-лист слів
        if (!VacancyFilterEngine.MatchesWordLists(v, WhiteListWords, WhiteListMatchAll, BanListWords, TitleOnly, includeLocationFields: true))
        {
            return false;
        }

        // 15. Повнотекстовий пошук
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            !VacancyFilterEngine.ContainsAllWords(v, SearchText.Trim(), TitleOnly, includeLocationFields: true))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesWorkFormat(VacancyDto v, string selectedFormat)
    {
        if (selectedFormat == "Усі формати")
        {
            return true;
        }

        WorkFormat wf = v.WorkFormat != WorkFormat.None
            ? v.WorkFormat
            : VacancyNormalizer.ResolveWorkFormat(v.Location, v.Schedule);

        return selectedFormat switch
        {
            "Віддалено" => (wf & WorkFormat.Remote) != 0,
            "Гібрид" => (wf & WorkFormat.Hybrid) != 0,
            "Офіс" => (wf & WorkFormat.Office) != 0,
            "Не вказано" => wf == WorkFormat.None,
            _ => true
        };
    }

    private static bool MatchesExperience(VacancyDto v, string selectedExperience)
    {
        if (selectedExperience == "Будь-який досвід")
        {
            return true;
        }

        int? months = v.ExperienceMonths ?? VacancyExperienceParser.ParseMonths(v.Experience, v.Description);
        return selectedExperience switch
        {
            "Без досвіду (0)" => months == 0,
            "До 1 року" => months is >= 0 and <= 12,
            "1–3 роки" => months is >= 12 and <= 36,
            "3–5 років" => months is >= 36 and <= 60,
            "5+ років" => months is >= 60,
            "Не вказано" => months is null,
            _ => true
        };
    }

    private static bool MatchesLocation(VacancyDto v, string term) =>
        v.Location.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (v.FullAddress?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool MatchesFreshness(VacancyDto v, string selectedFreshness, DateTimeOffset now)
    {
        TimeSpan? window = selectedFreshness switch
        {
            "За 24 години" => TimeSpan.FromDays(1),
            "За 3 дні" => TimeSpan.FromDays(3),
            "За 7 днів" => TimeSpan.FromDays(7),
            "За 14 днів" => TimeSpan.FromDays(14),
            "За 30 днів" => TimeSpan.FromDays(30),
            _ => null
        };

        return window is null || v.PublishedAt >= now - window.Value;
    }

    private double? GetDistanceKm(VacancyDto v) =>
        _distanceCache.TryGetValue(v.Id, out double? km) ? km : null;

    private static string? ExtractCityToken(VacancyDto v)
    {
        if (string.IsNullOrWhiteSpace(v.Location))
        {
            return null;
        }

        string first = v.Location.Split([',', '/', '('], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(first)
            || first.Equals("Не вказано", StringComparison.OrdinalIgnoreCase)
            || first.Equals("Віддалено", StringComparison.OrdinalIgnoreCase)
            || first.Equals("Remote", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return first;
    }

    private static void ReplaceCollection(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        target.Clear();
        foreach (string value in values)
        {
            target.Add(value);
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _distancePrefetchCts?.Cancel();
        _distancePrefetchCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
