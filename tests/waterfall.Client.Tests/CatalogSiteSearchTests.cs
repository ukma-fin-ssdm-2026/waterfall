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
    public async Task DjinniExtraFilters_GoToSiteAsUrlParams()
    {
        _vm.SelectedPlatform = "Djinni";
        _vm.DjinniSelectedEnglish = "Upper-Intermediate (english_level=upper)";
        _vm.DjinniSelectedCompanyType = "Продуктова (company_type=product)";
        _vm.DjinniSelectedDomain = "FinTech (domain=fintech)";
        _vm.DjinniSelectedRegion = "Країни ЄС (region=eu)";
        _vm.DjinniSelectedCountry = "Польща (country=POL)";

        await RunSearchAsync();

        Assert.Equal(
            new Dictionary<string, string> { ["english_level"] = "upper", ["company_type"] = "product", ["domain"] = "fintech", ["region"] = "eu", ["country"] = "POL" },
            Assert.Single(_gateway.SiteSearchRequests).Options);
    }

    [Fact]
    public async Task DjinniPanelDefaults_SendNoFilters()
    {
        _vm.SelectedPlatform = "Djinni";

        await RunSearchAsync();

        SiteSearchRequest request = Assert.Single(_gateway.SiteSearchRequests);
        Assert.Null(request.Categories);
        Assert.Equal((null, null, null, null, (string?)null), (request.Experience, request.City, request.Employment, request.SalaryFrom, request.Keywords));
        Assert.Null(request.Options);
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
    public async Task WorkUaPanel_BecomesPathCodesAndUrlFilters()
    {
        _vm.SelectedPlatform = "WorkUa";
        Choose(_vm.WorkUaCategoryChoices, "IT, комп'ютери, інтернет");
        _vm.WorkUaSelectedCity = "Чернівці";
        Choose(_vm.WorkUaEmploymentChoices, "Повна зайнятість");
        _vm.WorkUaSelectedExperience = "Без досвіду (experience=1)";
        _vm.WorkUaSelectedDays = "За 7 днів (days=123)";
        Choose(_vm.WorkUaLanguageChoices, "Англійська");
        _vm.WorkUaForStudents = true;
        _vm.WorkUaSalaryFrom = "25000";
        _vm.SiteKeywords = " Python ";
        _vm.WorkUaPagesToParse = "2";

        await RunSearchAsync();

        SiteSearchRequest request = Assert.Single(_gateway.SiteSearchRequests);
        Assert.Equal((JobSource.WorkUa, (List<string>?)null, "chernivtsi_cv", "Python", false, 2),
            (request.Source, request.Categories, request.City, request.Keywords, request.Remote, request.Pages));
        Assert.Equal(
            new Dictionary<string, string> { ["category"] = "1", ["employment"] = "74", ["experience"] = "1", ["days"] = "123", ["language"] = "1", ["student"] = "1", ["salaryfrom"] = "4" },
            request.Options);
    }

    [Fact]
    public async Task WorkUaMultiSelect_SendsSeveralCodesPerFilter_AndSelectAllClearWork()
    {
        _vm.SelectedPlatform = "WorkUa";
        Choose(_vm.WorkUaCategoryChoices, "IT, комп'ютери, інтернет", "Маркетинг, реклама, PR");
        Choose(_vm.WorkUaLanguageChoices, "Англійська", "Українська");
        _vm.SelectAllChoicesCommand.Execute(_vm.WorkUaEmploymentChoices);

        await RunSearchAsync();

        Dictionary<string, string> options = Assert.Single(_gateway.SiteSearchRequests).Options!;
        Assert.Equal(("1,9", "1,41", "74,75", "1"), (options["category"], options["language"], options["employment"], options["hybrid"]));

        _vm.ClearChoicesCommand.Execute(_vm.WorkUaCategoryChoices);
        Assert.DoesNotContain(_vm.WorkUaCategoryChoices, choice => choice.IsSelected);
        await _vm.ResetFiltersCommand.ExecuteAsync(null);
        Assert.DoesNotContain(_vm.WorkUaLanguageChoices.Concat(_vm.WorkUaEmploymentChoices), choice => choice.IsSelected);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "1")]
    public async Task WorkUaSalary_IncludeNoSalary_AddsNosalary(bool include, string? expected)
    {
        _vm.SelectedPlatform = "WorkUa";
        _vm.WorkUaSalaryFrom = "20000";
        _vm.WorkUaIncludeNoSalary = include;

        await RunSearchAsync();

        Assert.Equal(expected, Assert.Single(_gateway.SiteSearchRequests).Options!.GetValueOrDefault("nosalary"));
    }

    [Fact]
    public async Task RobotaUaIncludeNoSalary_KeepsVacanciesWithoutSalary()
    {
        _vm.SelectedPlatform = "RobotaUa";
        _vm.RobotaUaSelectedSalary = "Від 20 000 грн (salary=20000)";
        _vm.RobotaUaIncludeNoSalary = true;

        await RunSearchAsync();

        Assert.Equal(new Dictionary<string, string> { ["salary"] = "20000" }, Assert.Single(_gateway.SiteSearchRequests).Options);
    }

    [Theory]
    [InlineData("WorkUa", "3", 3)]
    [InlineData("Djinni", "70", 50)]
    [InlineData("DOU", "4", 1)]
    public async Task FromPage_IsSentForPagedSites_ButNotDou(string platform, string fromPage, int expected)
    {
        _vm.SelectedPlatform = platform;
        _vm.SiteFromPage = fromPage;

        await RunSearchAsync();

        Assert.Equal(expected, Assert.Single(_gateway.SiteSearchRequests).FromPage);
    }

    private static void Choose(IEnumerable<SelectableOption> choices, params string[] labels)
    {
        foreach (SelectableOption choice in choices.Where(choice => labels.Contains(choice.Label)))
        {
            choice.ToggleCommand.Execute(null);
        }
    }

    [Fact]
    public async Task WorkUaJobPages_OffByDefault_SwitchOnSendsFullSearch_AndHintWarns()
    {
        _vm.SelectedPlatform = "WorkUa";
        _vm.WorkUaPagesToParse = "2";
        Assert.False(_vm.WorkUaOpenJobPages);
        Assert.Contains("1–2 рядки", _vm.WorkUaJobPagesHint, StringComparison.Ordinal);

        await RunSearchAsync();
        Assert.True(_gateway.SiteSearchRequests[^1].ListOnly);

        _vm.WorkUaOpenJobPages = true;
        Assert.Contains("близько 2 хв", _vm.WorkUaJobPagesHint, StringComparison.Ordinal);
        await RunSearchAsync();
        Assert.False(_gateway.SiteSearchRequests[^1].ListOnly);

        await _vm.ResetFiltersCommand.ExecuteAsync(null);
        Assert.False(_vm.WorkUaOpenJobPages);
    }

    [Fact]
    public void RobotaUaTopButton_ShowsPagesAndTogglesItsPanel()
    {
        _vm.RobotaUaPagesToParse = "2";
        Assert.Equal("🔴 Robota.ua (2 стор.) ▼", _vm.RobotaUaButtonText);

        _vm.SelectRobotaUaFilterCommand.Execute(null);
        Assert.True(_vm.IsRobotaUaPanelVisible);
        Assert.EndsWith("▲", _vm.RobotaUaButtonText, StringComparison.Ordinal);

        _vm.SelectRobotaUaFilterCommand.Execute(null);
        Assert.False(_vm.IsRobotaUaPanelVisible);
    }

    [Fact]
    public async Task WorkUaRemoteCity_IsSentAsRemoteCityCode()
    {
        _vm.SelectedPlatform = "WorkUa";
        _vm.WorkUaSelectedCity = "Дистанційно";

        await RunSearchAsync();

        SiteSearchRequest request = Assert.Single(_gateway.SiteSearchRequests);
        Assert.Equal(("remote", (List<string>?)null, (Dictionary<string, string>?)null), (request.City, request.Categories, request.Options));
    }

    [Fact]
    public async Task RobotaUaPanel_BecomesCityCodeAndParams()
    {
        _vm.SelectedPlatform = "RobotaUa";
        _vm.RobotaUaSelectedCity = "Одеса";
        _vm.RobotaUaSelectedEmployment = "Віддалена робота (scheduleIds=3)";
        _vm.RobotaUaSelectedSalary = "Від 40 000 грн (salary=40000)";
        _vm.RobotaUaSelectedExperience = "Без досвіду (experienceType=true)";
        _vm.SiteKeywords = "QA";
        _vm.RobotaUaPagesToParse = "9";

        await RunSearchAsync();

        SiteSearchRequest request = Assert.Single(_gateway.SiteSearchRequests);
        Assert.Equal((JobSource.RobotaUa, "odessa", "QA", 5), (request.Source, request.City, request.Keywords, request.Pages));
        // Зарплата обрана — вакансії без неї сайт має приховати
        Assert.Equal(new Dictionary<string, string> { ["scheduleIds"] = "3", ["salary"] = "40000", ["salaryType"] = "false", ["experienceType"] = "true" }, request.Options);
    }

    [Fact]
    public async Task RobotaUaWithoutSalary_DoesNotHideVacanciesWithoutSalary()
    {
        _vm.SelectedPlatform = "RobotaUa";
        _vm.RobotaUaSelectedEmployment = "Віддалена робота (scheduleIds=3)";

        await RunSearchAsync();

        Assert.Equal(new Dictionary<string, string> { ["scheduleIds"] = "3" }, Assert.Single(_gateway.SiteSearchRequests).Options);
    }

    [Fact]
    public async Task SharedKeywords_WinOverPlatformField()
    {
        _vm.SelectedPlatform = "Djinni";
        _vm.DjinniAllKeywords = "Blazor";
        _vm.SiteKeywords = "Rust";

        await RunSearchAsync();

        Assert.Equal("Rust", Assert.Single(_gateway.SiteSearchRequests).Keywords);
    }

    [Fact]
    public async Task UnknownPlatform_IsNotSentToServer()
    {
        _vm.SelectedPlatform = "Other";

        await RunSearchAsync();

        Assert.Empty(_gateway.SiteSearchRequests);
        Assert.Contains("Оберіть сайт", _vm.StaleCacheText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("25000", false, "4")]  // від 25 000 → межа 20 000, щоб не загубити вакансій
    [InlineData("25000", true, "5")]   // до 25 000 → межа 30 000
    [InlineData("150000", true, "8")]  // понад найбільшу межу — «100 000 і більше»
    [InlineData("5000", false, null)]  // менше найменшої межі — без фільтра
    [InlineData("abc", false, null)]
    public void WorkUaSalary_BecomesSiteStepCode(string uah, bool roundUp, string? expected) =>
        Assert.Equal(expected, CatalogViewModel.WorkUaSalaryCode(uah, roundUp));

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
