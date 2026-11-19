using waterfall.UI.Controls;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

public partial class AnalyticsPage : ContentPage
{
    private readonly AnalyticsViewModel _viewModel;
    private readonly GradeSalaryBarChartDrawable _gradeDrawable = new();
    private readonly HorizontalBarChartDrawable _techSalaryDrawable = new()
    {
        BarColor = Color.FromArgb("#2ED573"),
        EmptyMessage = "Недостатньо даних про зарплати за технологіями"
    };
    private readonly DonutChartDrawable _workFormatDrawable = new() { CenterSubtitle = "форматів" };
    private readonly HorizontalBarChartDrawable _citiesDrawable = new()
    {
        BarColor = Color.FromArgb("#4F8EF7"),
        EmptyMessage = "Локації не вказані"
    };
    private readonly DonutChartDrawable _sourcesDrawable = new() { CenterSubtitle = "вакансій" };
    private readonly HorizontalBarChartDrawable _companiesDrawable = new()
    {
        BarColor = Color.FromArgb("#A55EEA"),
        EmptyMessage = "Компанії не вказані"
    };

    public AnalyticsPage(AnalyticsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        GradeSalaryChart.Drawable = _gradeDrawable;
        TopPayingTechChart.Drawable = _techSalaryDrawable;
        WorkFormatDonutChart.Drawable = _workFormatDrawable;
        TopCitiesChart.Drawable = _citiesDrawable;
        SourcesDonutChart.Drawable = _sourcesDrawable;
        TopCompaniesChart.Drawable = _companiesDrawable;

        _viewModel.ChartsUpdated += OnChartsUpdated;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.CheckAccessAndReloadCommand.Execute(null);
    }

    private void OnChartsUpdated(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
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

            GradeSalaryChart.Invalidate();
            TopPayingTechChart.Invalidate();
            WorkFormatDonutChart.Invalidate();
            TopCitiesChart.Invalidate();
            SourcesDonutChart.Invalidate();
            TopCompaniesChart.Invalidate();
        });
    }
}
