using waterfall.UI.Controls;
using waterfall.UI.Services;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class AnalyticsPage : ContentPage, IHostedPage
{
    private readonly AnalyticsViewModel _viewModel;
    private readonly ThemeService _themeService;
    private readonly GradeSalaryBarChartDrawable _gradeDrawable = new();
    private readonly HorizontalBarChartDrawable _techSalaryDrawable = new()
    {
        BarColor = Color.FromArgb("#6FB59B"),
        EmptyMessage = "Недостатньо даних про зарплати за технологіями"
    };
    private readonly DonutChartDrawable _workFormatDrawable = new() { CenterSubtitle = "форматів" };
    private readonly HorizontalBarChartDrawable _citiesDrawable = new()
    {
        BarColor = Color.FromArgb("#D4745E"),
        EmptyMessage = "Локації не вказані"
    };
    private readonly DonutChartDrawable _sourcesDrawable = new() { CenterSubtitle = "вакансій" };
    private readonly HorizontalBarChartDrawable _companiesDrawable = new()
    {
        BarColor = Color.FromArgb("#8E8A9E"),
        EmptyMessage = "Компанії не вказані"
    };

    public AnalyticsPage(AnalyticsViewModel viewModel, ThemeService themeService)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _themeService = themeService;

        GradeSalaryChart.Drawable = _gradeDrawable;
        TopPayingTechChart.Drawable = _techSalaryDrawable;
        WorkFormatDonutChart.Drawable = _workFormatDrawable;
        TopCitiesChart.Drawable = _citiesDrawable;
        SourcesDonutChart.Drawable = _sourcesDrawable;
        TopCompaniesChart.Drawable = _companiesDrawable;

        _viewModel.ChartsUpdated += OnChartsUpdated;
        _themeService.ThemeChanged += OnThemeChanged;
    }

    internal void HideStandalonePhoneHeader() => PhoneHeaderBar.IsVisible = false;

    public void OnHostActivated() => OnAppearing();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.HasLoaded)
        {
            SyncChartsFromViewModel();
        }

        _viewModel.EnsureLoadedCommand.Execute(null);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(InvalidateCharts);
    }

    private void InvalidateCharts()
    {
        GradeSalaryChart.Invalidate();
        TopPayingTechChart.Invalidate();
        WorkFormatDonutChart.Invalidate();
        TopCitiesChart.Invalidate();
        SourcesDonutChart.Invalidate();
        TopCompaniesChart.Invalidate();
    }

    private void OnChartsUpdated(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(SyncChartsFromViewModel);
    }

    private void SyncChartsFromViewModel()
    {
        _gradeDrawable.Items = _viewModel.SalariesByGrade.ToList();
        _techSalaryDrawable.Entries = _viewModel.TopPayingTechnologies
            .Select(t => new HorizontalBarEntry(t.Name, (double)t.AverageSalary, t.FormattedSalary))
            .ToList();
        _workFormatDrawable.Items = _viewModel.WorkFormats.ToList();
        _citiesDrawable.Entries = _viewModel.TopCities
            .Select(c => new HorizontalBarEntry(c.Name, c.Count, c.DisplayLabel))
            .ToList();
        _sourcesDrawable.Items = _viewModel.Sources.ToList();
        _companiesDrawable.Entries = _viewModel.TopCompanies
            .Select(c => new HorizontalBarEntry(c.Name, c.Count, c.DisplayLabel))
            .ToList();

        InvalidateCharts();
    }
}
