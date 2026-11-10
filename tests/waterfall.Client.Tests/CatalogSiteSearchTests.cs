// AI: тести пошуку на сайтах із каталогу: фільтри панелі → запит, шкала прогресу, результат, помилки (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using Waterfall.Client.Models.SiteSearch;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

public class CatalogSiteSearchTests
{
    private readonly FakeGateway _gateway = new();
    private readonly FakeTimeProvider _time = new();
    private readonly CatalogViewModel _vm;

    public CatalogSiteSearchTests()
    {
        _vm = new CatalogViewModel(_gateway, new StrongReferenceMessenger(), _time);
        _gateway.SiteSearchStatuses.Enqueue(Completed());
    }

    [Fact]
    public async Task DjinniPanel_BecomesRequestInSiteCodes()
    {
        _vm.SelectedPlatform = "Djinni";
        _vm.DjinniSelectedCategory = "C# / .NET";
        _vm.DjinniSelectedExperience = "2 роки (2y)";
        _vm.DjinniSelectedCity = "Київ (kyiv)";
        _vm.DjinniSelectedEmployment = "Повна віддалена (employment=remote)";
        _vm.DjinniSelectedSalary = "від $2000";
        _vm.DjinniAllKeywords = " Blazor ";
        _vm.DjinniPagesToParse = "9";

        await RunSearchAsync();

        SiteSearchRequest request = Assert.Single(_gateway.SiteSearchRequests);
        Assert.Equal(JobSource.Djinni, request.Source);
        Assert.Equal([".NET"], request.Categories!);
        Assert.Equal(("2y", "kyiv", "remote", 2000, "Blazor", 5), (request.Experience, request.City, request.Employment, request.SalaryFrom, request.Keywords, request.Pages));
    }

    [Fact]
    public async Task DjinniPanelDefaults_SendNoFilters()
    {
        _vm.SelectedPlatform = "Djinni";

        await RunSearchAsync();

        SiteSearchRequest request = Assert.Single(_gateway.SiteSearchRequests);
        Assert.Null(request.Categories);
        Assert.Equal((null, null, null, null, (string?)null), (request.Experience, request.City, request.Employment, request.SalaryFrom, request.Keywords));
    }

    [Theory]
    [InlineData("1…3 роки (exp=1-3)", "1-3")]
    [InlineData("Без досвіду (First Job)", "0-1")]
    [InlineData("Будь-який досвід", null)]
    public async Task DouPanel_BecomesRssFilters(string experienceOption, string? expected)
    {
        _vm.SelectedPlatform = "DOU";
        _vm.DouSelectedCategory = "Python";
        _vm.DouSelectedExperience = experienceOption;
        _vm.DouSelectedCity = "Львів";
        _vm.DouIsRemote = true;
        _vm.DouSearchQuery = "Django";

        await RunSearchAsync();

        SiteSearchRequest request = Assert.Single(_gateway.SiteSearchRequests);
        Assert.Equal((JobSource.Dou, "Python", expected, "Львів", true, "Django", 1),
            (request.Source, request.Categories!.Single(), request.Experience, request.City, request.Remote, request.Keywords, request.Pages));
    }

    [Fact]
    public async Task WhileSiteIsRead_ProgressIsShown_ThenResultReplacesList()
    {
        _vm.SelectedPlatform = "Djinni";
        _gateway.SiteSearchStatuses.Clear();
        _gateway.SiteSearchStatuses.Enqueue(new SiteSearchStatusDto { State = SiteSearchStatusDto.Running, PagesDone = 1, PagesTotal = 3, Found = 15 });
        _gateway.SiteSearchStatuses.Enqueue(Completed(Vacancy("Python Developer"), Vacancy("Django Developer")));

        Task search = _vm.FetchFromSitesCommand.ExecuteAsync(null);

        Assert.True(_vm.IsSiteSearchRunning);
        Assert.False(_vm.FetchFromSitesCommand.CanExecute(null));
        Assert.Equal(1.0 / 3, _vm.SiteSearchProgress, 3);
        Assert.Contains("сторінка 1 з 3, знайдено 15", _vm.SiteSearchStatusText, StringComparison.Ordinal);

        await FinishAsync(search);

        Assert.False(_vm.IsSiteSearchRunning);
        Assert.Equal(["Python Developer", "Django Developer"], _vm.Vacancies.Select(v => v.Title));
        Assert.Equal(2, _vm.TotalCount);
        Assert.Contains("знайдено 2 вакансій", _vm.StaleCacheText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResultFromCache_IsFetchedAndMarked()
    {
        _vm.SelectedPlatform = "DOU";
        _gateway.SiteSearchStatuses.Clear();
        _gateway.SiteSearchStatuses.Enqueue(new SiteSearchStatusDto { State = SiteSearchStatusDto.Completed, FromCache = true });
        _gateway.SiteSearchStatuses.Enqueue(Completed(Vacancy("QA Engineer")) with { FromCache = true });

        await RunSearchAsync();

        Assert.Equal(1, _gateway.SiteSearchPolls); // вакансії кешованого результату — окремим запитом стану
        Assert.Single(_vm.Vacancies);
        Assert.Contains("протягом 3 годин", _vm.StaleCacheText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guest_IsAskedToSignIn()
    {
        _vm.SelectedPlatform = "Djinni";
        _gateway.SiteSearchError = new ApiException(HttpStatusCode.Unauthorized, null, "Потрібно увійти");

        await RunSearchAsync();

        Assert.Contains("Увійдіть", _vm.StaleCacheText, StringComparison.Ordinal);
        Assert.False(_vm.IsSiteSearchRunning);
    }

    [Fact]
    public async Task FailedSearch_ShowsReason_AndKeepsList()
    {
        _vm.SelectedPlatform = "Djinni";
        _gateway.SiteSearchStatuses.Clear();
        _gateway.SiteSearchStatuses.Enqueue(new SiteSearchStatusDto { State = SiteSearchStatusDto.Failed, Message = "Не вдалося прочитати сайт." });

        await RunSearchAsync();

        Assert.Equal("⚠️ Djinni: Не вдалося прочитати сайт.", _vm.StaleCacheText);
        Assert.Empty(_vm.Vacancies);
    }

    [Fact]
    public async Task WorkUa_IsNotSentToServer()
    {
        _vm.SelectedPlatform = "WorkUa";

        await RunSearchAsync();

        Assert.Empty(_gateway.SiteSearchRequests);
        Assert.Contains("Djinni і DOU", _vm.StaleCacheText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1 рік (1y)", "1y")]
    [InlineData("< 1 року (exp=0-1)", "0-1")]
    [InlineData("Усі міста", null)]
    public void OptionCode_IsTakenFromTrailingParentheses(string option, string? expected) =>
        Assert.Equal(expected, CatalogViewModel.CodeInParentheses(option));

    private async Task RunSearchAsync() => await FinishAsync(_vm.FetchFromSitesCommand.ExecuteAsync(null));

    // Опитування стану чекає на FakeTimeProvider: прокручуємо час, доки пошук не завершиться
    private async Task FinishAsync(Task search)
    {
        for (int i = 0; i < 100 && !search.IsCompleted; i++)
        {
            _time.Advance(CatalogViewModel.SiteSearchPollInterval);
            await Task.Delay(1);
        }

        await search.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static SiteSearchStatusDto Completed(params VacancyDto[] vacancies) =>
        new() { State = SiteSearchStatusDto.Completed, PagesDone = 1, PagesTotal = 1, Found = vacancies.Length, Vacancies = [.. vacancies] };

    private static VacancyDto Vacancy(string title) => new() { Id = Guid.NewGuid(), Title = title, Company = "Acme", Source = JobSource.Djinni };
}
