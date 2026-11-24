// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Services;

/// <summary>
/// REL-002: активні фільтри каталогу записуються в локальне сховище в момент зміни
/// й відновлюються при наступному запуску, зокрема після аварійного завершення.
/// Зберігаються фільтри, що звужують уже знайдений список (рядок пошуку, сортування,
/// платформа, формат, зарплата, компанія, прапорці, вайт-лист і бан-слова), і форми пошуку на сайтах
/// (<see cref="CatalogSiteFilterFields"/>: категорії, міста, досвід, галочки сфер Work.ua тощо).
/// Вибір самих сайтів зберігає ParseSitePreferences в UI.
/// </summary>
public sealed class CatalogFilterPersistence : IDisposable
{
    /// <summary>Довші рядки обрізаються: це захист від зіпсованого або підміненого файлу налаштувань.</summary>
    public const int MaxTextLength = 200;

    /// <summary>Скільки слів вайт-листа чи бан-листа відновлюється щонайбільше.</summary>
    public const int MaxWordsPerList = 100;

    private static readonly HashSet<string> TrackedProperties =
    [
        .. CatalogSiteFilterFields.All.Select(field => field.Name),
        nameof(CatalogViewModel.SearchText),
        nameof(CatalogViewModel.SelectedSortOption),
        nameof(CatalogViewModel.SelectedSourceFilter),
        nameof(CatalogViewModel.SelectedFormat),
        nameof(CatalogViewModel.SelectedSalaryCurrency),
        nameof(CatalogViewModel.UnifiedCompanyFilter),
        nameof(CatalogViewModel.MinSalaryFilter),
        nameof(CatalogViewModel.MaxSalaryFilter),
        nameof(CatalogViewModel.UnifiedTitleOnly),
        nameof(CatalogViewModel.UnifiedOnlyWithSalary),
        nameof(CatalogViewModel.UnifiedOnlyActive),
        nameof(CatalogViewModel.UnifiedHideViewed),
        nameof(CatalogViewModel.UnifiedIncludeNoSalary),
        nameof(CatalogViewModel.UnifiedIsRemote),
        nameof(CatalogViewModel.WhiteListMatchAll)
    ];

    private readonly CatalogViewModel _viewModel;
    private readonly IFilterStateStorage _storage;
    private bool _isRestoring;
    private bool _isDisposed;

    private CatalogFilterPersistence(CatalogViewModel viewModel, IFilterStateStorage storage)
    {
        _viewModel = viewModel;
        _storage = storage;
    }

    /// <summary>Відновлює збережені фільтри у <paramref name="viewModel"/> і далі записує кожну їхню зміну.</summary>
    public static CatalogFilterPersistence Attach(CatalogViewModel viewModel, IFilterStateStorage storage)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(storage);

        CatalogFilterPersistence persistence = new(viewModel, storage);
        persistence.Restore();
        viewModel.PropertyChanged += persistence.OnPropertyChanged;
        viewModel.WhiteListWords.CollectionChanged += persistence.OnWordsChanged;
        viewModel.BanListWords.CollectionChanged += persistence.OnWordsChanged;
        foreach (SelectableOption choice in CatalogSiteFilterFields.ChoiceGroups(viewModel).SelectMany(group => group))
        {
            choice.PropertyChanged += persistence.OnChoiceChanged;
        }

        return persistence;
    }

    public CatalogViewModel ViewModel => _viewModel;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _viewModel.PropertyChanged -= OnPropertyChanged;
        _viewModel.WhiteListWords.CollectionChanged -= OnWordsChanged;
        _viewModel.BanListWords.CollectionChanged -= OnWordsChanged;
        foreach (SelectableOption choice in CatalogSiteFilterFields.ChoiceGroups(_viewModel).SelectMany(group => group))
        {
            choice.PropertyChanged -= OnChoiceChanged;
        }
    }

    /// <summary>Знімок поточних фільтрів ViewModel.</summary>
    public static CatalogFilterState Capture(CatalogViewModel vm) => new()
    {
        SearchText = vm.SearchText,
        SortOption = vm.SelectedSortOption,
        SourceFilter = vm.SelectedSourceFilter,
        Format = vm.SelectedFormat,
        SalaryCurrency = vm.SelectedSalaryCurrency,
        CompanyFilter = vm.UnifiedCompanyFilter,
        MinSalary = vm.MinSalaryFilter,
        MaxSalary = vm.MaxSalaryFilter,
        TitleOnly = vm.UnifiedTitleOnly,
        OnlyWithSalary = vm.UnifiedOnlyWithSalary,
        OnlyActive = vm.UnifiedOnlyActive,
        HideViewed = vm.UnifiedHideViewed,
        IncludeNoSalary = vm.UnifiedIncludeNoSalary,
        IsRemote = vm.UnifiedIsRemote,
        WhiteListMatchAll = vm.WhiteListMatchAll,
        WhiteListWords = [.. vm.WhiteListWords],
        BanListWords = [.. vm.BanListWords],
        SiteFilters = CatalogSiteFilterFields.All.ToDictionary(field => field.Name, field => field.Get(vm), StringComparer.Ordinal),
        SiteChoices =
        [
            .. CatalogSiteFilterFields.ChoiceGroups(vm)
                .SelectMany(group => group)
                .Where(choice => choice.IsSelected)
                .Select(CatalogSiteFilterFields.ChoiceId)
        ]
    };

    private void Restore()
    {
        CatalogFilterState? state = TryLoad();
        if (state is null)
        {
            return;
        }

        _isRestoring = true;
        try
        {
            CatalogViewModel vm = _viewModel;

            // Варіанти списків можуть змінитися між версіями застосунку: невідомий варіант лишає значення за замовчуванням
            vm.SelectedSortOption = PickOption(state.SortOption, vm.SortOptions, vm.SelectedSortOption);
            vm.SelectedSourceFilter = PickOption(state.SourceFilter, vm.SourceFilterOptions, vm.SelectedSourceFilter);
            vm.SelectedFormat = PickOption(state.Format, vm.FormatOptions, vm.SelectedFormat);
            vm.SelectedSalaryCurrency = PickOption(state.SalaryCurrency, vm.SalaryCurrencyOptions, vm.SelectedSalaryCurrency);

            vm.UnifiedCompanyFilter = Clip(state.CompanyFilter);
            vm.MinSalaryFilter = Clip(state.MinSalary);
            vm.MaxSalaryFilter = Clip(state.MaxSalary);

            vm.UnifiedTitleOnly = state.TitleOnly;
            vm.UnifiedOnlyWithSalary = state.OnlyWithSalary;
            vm.UnifiedOnlyActive = state.OnlyActive;
            vm.UnifiedHideViewed = state.HideViewed;
            vm.UnifiedIncludeNoSalary = state.IncludeNoSalary;
            vm.UnifiedIsRemote = state.IsRemote;
            vm.WhiteListMatchAll = state.WhiteListMatchAll;

            vm.RestoreSavedFilters(Clip(state.SearchText), CleanWords(state.WhiteListWords), CleanWords(state.BanListWords));
            RestoreSiteFilters(vm, state);
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private static void RestoreSiteFilters(CatalogViewModel vm, CatalogFilterState state)
    {
        if (state.SiteFilters is not null)
        {
            foreach (CatalogSiteFilterFields.Field field in CatalogSiteFilterFields.All)
            {
                // Поле, якого немає в записі (старіший запис), лишається зі значенням за замовчуванням
                if (state.SiteFilters.TryGetValue(field.Name, out string? value) && value is not null)
                {
                    field.Restore(vm, value);
                }
            }
        }

        HashSet<string> selected = new(state.SiteChoices ?? [], StringComparer.Ordinal);
        foreach (SelectableOption choice in CatalogSiteFilterFields.ChoiceGroups(vm).SelectMany(group => group))
        {
            choice.IsSelected = selected.Contains(CatalogSiteFilterFields.ChoiceId(choice));
        }
    }

    private CatalogFilterState? TryLoad()
    {
        string? json;
        try
        {
            json = _storage.Load();
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return Deserialize(json);
    }

    /// <summary>Рядок для сховища (source-generated JSON, працює й у trimmed-збірках).</summary>
    public static string Serialize(CatalogFilterState state) =>
        JsonSerializer.Serialize(state, ClientJsonContext.Default.CatalogFilterState);

    /// <summary>Знімок із рядка сховища або <c>null</c>, якщо запис зіпсований чи іншої версії формату.</summary>
    public static CatalogFilterState? Deserialize(string json)
    {
        try
        {
            CatalogFilterState? state = JsonSerializer.Deserialize(json, ClientJsonContext.Default.CatalogFilterState);
            return state?.Version == CatalogFilterState.CurrentVersion ? state : null;
        }
        catch (JsonException)
        {
            // Зіпсований запис не повинен ламати запуск: каталог стартує з фільтрами за замовчуванням
            return null;
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && TrackedProperties.Contains(e.PropertyName))
        {
            SaveNow();
        }
    }

    private void OnWordsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SaveNow();

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectableOption.IsSelected))
        {
            SaveNow();
        }
    }

    private void SaveNow()
    {
        if (_isRestoring || _isDisposed)
        {
            return;
        }

        try
        {
            _storage.Save(Serialize(Capture(_viewModel)));
        }
        catch
        {
            // Недоступне сховище (непакований хост, повний диск) не повинне ламати роботу каталогу
        }
    }

    private static string PickOption(string? saved, IReadOnlyList<string> options, string fallback) =>
        saved is not null && options.Contains(saved) ? saved : fallback;

    internal static string Clip(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= MaxTextLength ? value : value[..MaxTextLength];
    }

    private static List<string> CleanWords(IEnumerable<string>? words) =>
        words is null
            ? []
            : words
                .Where(word => !string.IsNullOrWhiteSpace(word) && word.Length <= MaxTextLength)
                .Select(word => word.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxWordsPerList)
                .ToList();
}
