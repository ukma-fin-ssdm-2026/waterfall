// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;

namespace Waterfall.Client.ViewModels;

public sealed record TechDemandItem(string Name, int Count, double BarWidthFraction, string DisplayShare);

public sealed record TimelinePoint(string Period, int VacanciesCount);

/// <summary>
/// Вкладка «Аналітика» (FR-003, FR-008, US-006, US-007): графіки, середня ЗП, попит на стек.
/// </summary>
public partial class AnalyticsViewModel : ObservableObject
{
    private readonly IAuthTokenStore _tokens;

    public AnalyticsViewModel(IAuthTokenStore tokens)
    {
        _tokens = tokens;
        LoadMockData();
    }

    public IReadOnlyList<string> Categories { get; } =
    [
        "Всі категорії",
        ".NET / C#",
        "Python",
        "JavaScript / TypeScript",
        "QA / Testing",
        "DevOps / Cloud"
    ];

    public IReadOnlyList<string> Periods { get; } =
    [
        "Останній місяць",
        "3 місяці",
        "6 місяців",
        "1 рік"
    ];

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "Всі категорії";

    [ObservableProperty]
    public partial string SelectedPeriod { get; set; } = "3 місяці";

    [ObservableProperty]
    public partial string AverageSalary { get; set; } = "$2 450";

    [ObservableProperty]
    public partial int TotalVacancies { get; set; } = 384;

    [ObservableProperty]
    public partial string DynamicsPercent { get; set; } = "+14.2% зростання";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInsufficientData))]
    public partial bool HasEnoughData { get; set; } = true;

    public bool IsInsufficientData => !HasEnoughData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessDeniedMessage), nameof(IsAccessDenied))]
    public partial bool IsAnalyst { get; set; } = true;

    public bool IsAccessDenied => !IsAnalyst;

    public string AccessDeniedMessage => "Розділ доступний виключно користувачам із роллю «Роботодавець / Аналітик ринку» (FR-008, US-006 AC-3).";

    public ObservableCollection<TechDemandItem> TopTechnologies { get; } = [];

    public ObservableCollection<TimelinePoint> Timeline { get; } = [];

    // # Mock
    [RelayCommand]
    public async Task CheckAccessAndReloadAsync()
    {
        string? token = await _tokens.GetTokenAsync();
        if (token is not null)
        {
            var session = AuthSession.FromToken(token);
            if (session is not null)
            {
                IsAnalyst = session.Role == UserRole.Analyst;
            }
        }

        ApplyFilters();
    }

    // # Mock
    [RelayCommand]
    private void ApplyFilters()
    {
        if (SelectedCategory == "QA / Testing" && SelectedPeriod == "Останній місяць")
        {
            // Демонстрація стану US-006 AC-2 (< 5 вакансій)
            HasEnoughData = false;
            return;
        }

        HasEnoughData = true;
        LoadMockData();
    }

    // # Mock
    [RelayCommand]
    private void ResetAnalyticsFilters()
    {
        SelectedCategory = Categories[0];
        SelectedPeriod = Periods[1];
        HasEnoughData = true;
        LoadMockData();
    }

    // # Mock
    private void LoadMockData()
    {
        TopTechnologies.Clear();
        TopTechnologies.Add(new TechDemandItem("Python", 145, 1.0, "38%"));
        TopTechnologies.Add(new TechDemandItem("C# / .NET", 112, 0.77, "29%"));
        TopTechnologies.Add(new TechDemandItem("JavaScript", 88, 0.61, "23%"));
        TopTechnologies.Add(new TechDemandItem("Docker", 65, 0.45, "17%"));
        TopTechnologies.Add(new TechDemandItem("PostgreSQL", 54, 0.37, "14%"));

        Timeline.Clear();
        Timeline.Add(new TimelinePoint("Серпень 2026", 98));
        Timeline.Add(new TimelinePoint("Вересень 2026", 124));
        Timeline.Add(new TimelinePoint("Жовтень 2026", 162));

        if (SelectedCategory == "Python")
        {
            AverageSalary = "$2 600";
            TotalVacancies = 145;
            DynamicsPercent = "+18% за 3 міс.";
        }
        else if (SelectedCategory.Contains(".NET"))
        {
            AverageSalary = "$2 850";
            TotalVacancies = 112;
            DynamicsPercent = "+9% за 3 міс.";
        }
        else
        {
            AverageSalary = "$2 450";
            TotalVacancies = 384;
            DynamicsPercent = "+14.2% за 3 міс.";
        }
    }
}
