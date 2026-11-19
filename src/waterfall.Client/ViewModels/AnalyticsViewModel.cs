// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Analytics;
using Waterfall.Client.Models.Auth;

namespace Waterfall.Client.ViewModels;

public sealed record GradeSalaryItem(
    string Grade,
    decimal AverageSalary,
    string FormattedAverage,
    string RangeText,
    int VacanciesCount);

public sealed record TechSalaryItem(
    string Name,
    decimal AverageSalary,
    string FormattedSalary,
    int VacanciesCount);

public sealed record AnalyticsShareItem(
    string Name,
    int Count,
    double SharePercent,
    string DisplayLabel);

/// <summary>
/// Вкладка «Аналітика» (FR-003, FR-008): 5 аналітичних розрізів за реальними вакансіями БД,
/// миттєве перемальовування графіків при зміні фільтрів та перевірка ролі Аналітика.
/// </summary>
public partial class AnalyticsViewModel : ObservableObject
{
    public const string AllCategoriesOption = "Всі категорії";

    private readonly IAuthTokenStore _tokens;
    private readonly IHttpGatewayService _gateway;
    private CancellationTokenSource? _loadCts;
    private bool _suppressFilterReload;

    public AnalyticsViewModel(
        IAuthTokenStore tokens,
        IHttpGatewayService gateway,
        IMessenger? messenger = null)
    {
        _tokens = tokens;
        _gateway = gateway;

        if (messenger is not null)
        {
            messenger.Register<AnalyticsViewModel, UserAuthStateChangedMessage>(this, static (vm, message) =>
            {
                _ = vm.CheckAccessAndReloadAsync();
            });
        }
    }

    public event EventHandler? ChartsUpdated;

    public ObservableCollection<string> Categories { get; } = [AllCategoriesOption];

    public IReadOnlyList<string> Periods { get; } =
    [
        "Останній місяць",
        "3 місяці",
        "6 місяців",
        "1 рік"
    ];

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = AllCategoriesOption;

    [ObservableProperty]
    public partial string SelectedPeriod { get; set; } = "3 місяці";

    [ObservableProperty]
    public partial int TotalVacancies { get; set; }

    [ObservableProperty]
    public partial string SalaryTransparencyText { get; set; } = "—";

    [ObservableProperty]
    public partial string EntryLevelText { get; set; } = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInsufficientData))]
    public partial bool HasLoaded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInsufficientData))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInsufficientData))]
    public partial bool HasEnoughData { get; private set; }

    public bool IsInsufficientData => HasLoaded && !IsLoading && IsAnalyst && ErrorMessage is null && !HasEnoughData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsInsufficientData))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => ErrorMessage is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAccessDenied))]
    [NotifyPropertyChangedFor(nameof(IsInsufficientData))]
    public partial bool IsAnalyst { get; private set; }

    public bool IsAccessDenied => !IsAnalyst;

    public string AccessDeniedMessage => "Розділ доступний виключно користувачам із роллю «Роботодавець / Аналітик ринку» (FR-008, US-006 AC-3).";

    public ObservableCollection<GradeSalaryItem> SalariesByGrade { get; } = [];

    public ObservableCollection<TechSalaryItem> TopPayingTechnologies { get; } = [];

    public ObservableCollection<AnalyticsShareItem> WorkFormats { get; } = [];

    public ObservableCollection<AnalyticsShareItem> TopCities { get; } = [];

    public ObservableCollection<AnalyticsShareItem> Sources { get; } = [];

    public ObservableCollection<AnalyticsShareItem> TopCompanies { get; } = [];

    partial void OnSelectedCategoryChanged(string value)
    {
        if (!_suppressFilterReload && IsAnalyst)
        {
            _ = ApplyFiltersAsync();
        }
    }

    partial void OnSelectedPeriodChanged(string value)
    {
        if (!_suppressFilterReload && IsAnalyst)
        {
            _ = ApplyFiltersAsync();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task CheckAccessAndReloadAsync()
    {
        string? token = await _tokens.GetTokenAsync(CancellationToken.None);
        AuthSession? session = token is null ? null : AuthSession.FromToken(token);
        IsAnalyst = session is { Role: UserRole.Analyst };

        if (!IsAnalyst)
        {
            if (_loadCts is not null)
            {
                await _loadCts.CancelAsync();
            }

            ErrorMessage = null;
            HasEnoughData = false;
            HasLoaded = false;
            ClearCollections();
            ChartsUpdated?.Invoke(this, EventArgs.Empty);
            return;
        }

        await ApplyFiltersAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ApplyFiltersAsync()
    {
        if (!IsAnalyst)
        {
            return;
        }

        if (_loadCts is not null)
        {
            await _loadCts.CancelAsync();
        }

        CancellationTokenSource cts = new();
        _loadCts = cts;
        CancellationToken ct = cts.Token;

        ErrorMessage = null;
        IsLoading = true;
        try
        {
            string? categoryParam = string.IsNullOrWhiteSpace(SelectedCategory)
                || string.Equals(SelectedCategory, AllCategoriesOption, StringComparison.Ordinal)
                ? null
                : SelectedCategory;
            int months = ParsePeriodMonths(SelectedPeriod);

            MarketAnalyticsDto result = await _gateway.GetMarketAnalyticsAsync(categoryParam, months, ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            SyncCategories(result.AvailableCategories);

            TotalVacancies = result.TotalVacancies;
            HasEnoughData = result.HasEnoughData;

            ClearCollections();
            if (!result.HasEnoughData)
            {
                SalaryTransparencyText = "—";
                EntryLevelText = "—";
            }
            else
            {
                SalaryTransparencyText = $"{result.SalaryTransparencyPercent.ToString("0.#", CultureInfo.InvariantCulture)}%";
                EntryLevelText = $"{result.EntryLevelPercent.ToString("0.#", CultureInfo.InvariantCulture)}% ({result.EntryLevelCount})";

                foreach (GradeSalaryDto g in result.SalariesByGrade)
                {
                    SalariesByGrade.Add(new GradeSalaryItem(
                        g.Grade,
                        g.AverageSalary,
                        FormatMoney(g.AverageSalary, g.Currency),
                        $"{FormatMoney(g.MinSalary, g.Currency)} – {FormatMoney(g.MaxSalary, g.Currency)} ({g.VacanciesCount})",
                        g.VacanciesCount));
                }

                foreach (TechSalaryDto t in result.TopPayingTechnologies)
                {
                    TopPayingTechnologies.Add(new TechSalaryItem(
                        t.Name,
                        t.AverageSalary,
                        $"{FormatMoney(t.AverageSalary, t.Currency)} ({t.VacanciesCount})",
                        t.VacanciesCount));
                }

                PopulateShares(WorkFormats, result.WorkFormats);
                PopulateShares(TopCities, result.TopCities);
                PopulateShares(Sources, result.Sources);
                PopulateShares(TopCompanies, result.TopCompanies);
            }

            HasLoaded = true;
            ChartsUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException ex) when (!ct.IsCancellationRequested)
        {
            if (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                IsAnalyst = false;
                ClearCollections();
                ChartsUpdated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = ex.Message;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Замінено новішим запитом при зміні фільтрів.
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsInsufficientData));
            }
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ResetAnalyticsFiltersAsync()
    {
        _suppressFilterReload = true;
        try
        {
            SelectedCategory = AllCategoriesOption;
            SelectedPeriod = Periods[1];
        }
        finally
        {
            _suppressFilterReload = false;
        }

        await ApplyFiltersAsync();
    }

    private void ClearCollections()
    {
        SalariesByGrade.Clear();
        TopPayingTechnologies.Clear();
        WorkFormats.Clear();
        TopCities.Clear();
        Sources.Clear();
        TopCompanies.Clear();
    }

    private static void PopulateShares(ObservableCollection<AnalyticsShareItem> target, IReadOnlyList<CategoryShareDto> source)
    {
        foreach (CategoryShareDto item in source)
        {
            string pct = item.SharePercent.ToString("0.#", CultureInfo.InvariantCulture);
            target.Add(new AnalyticsShareItem(item.Name, item.Count, item.SharePercent, $"{item.Count} ({pct}%)"));
        }
    }

    private void SyncCategories(IReadOnlyList<string> available)
    {
        string previous = SelectedCategory;
        List<string> next = [AllCategoriesOption];
        foreach (string cat in available)
        {
            if (!string.IsNullOrWhiteSpace(cat) && !next.Contains(cat, StringComparer.OrdinalIgnoreCase))
            {
                next.Add(cat);
            }
        }

        if (next.SequenceEqual(Categories, StringComparer.Ordinal))
        {
            return;
        }

        _suppressFilterReload = true;
        try
        {
            Categories.Clear();
            foreach (string item in next)
            {
                Categories.Add(item);
            }

            SelectedCategory = next.Contains(previous, StringComparer.OrdinalIgnoreCase)
                ? previous
                : AllCategoriesOption;
        }
        finally
        {
            _suppressFilterReload = false;
        }
    }

    private static int ParsePeriodMonths(string? period) => period switch
    {
        "Останній місяць" => 1,
        "6 місяців" => 6,
        "1 рік" => 12,
        _ => 3
    };

    private static string FormatMoney(decimal amount, string? currency)
    {
        string symbol = currency?.ToUpperInvariant() switch
        {
            "USD" => "$",
            "EUR" => "€",
            "UAH" => "₴",
            _ => string.Empty
        };

        string formatted = amount.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');
        return string.IsNullOrEmpty(symbol)
            ? $"{formatted} {currency}".Trim()
            : $"{symbol}{formatted}";
    }
}
