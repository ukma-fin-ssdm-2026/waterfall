// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: тести EnsureLoaded/LoadMore/ShowTotal, скасованого застарілого завантаження і гілок debounce (Lab 06); перевірено: dotnet test, звіт покриття.
// AI: переписано під каталог без базового списку з БД — вакансії лише з «Знайти», рядок пошуку й фільтри локальні (рішення команди 10.11.2026, зі згоди Дениса); перевірено: dotnet test, самоперевірка автора (§8)

using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using Waterfall.Client.Models.SiteSearch;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

/// <summary>
/// Каталог без базового списку: до «Знайти» він порожній, а знайдені на сайті вакансії рядок пошуку,
/// сортування й фільтри уточнюють локально. Жодна дія каталогу не читає спільну БД (GetVacanciesAsync).
/// </summary>
public class CatalogViewModelTests
{
    private readonly FakeGateway _gateway = new();
    private readonly FakeTimeProvider _time = new();
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly CatalogViewModel _vm;

    public CatalogViewModelTests()
    {
        _vm = new CatalogViewModel(_gateway, _messenger, _time);
    }

    [Fact]
    public async Task OpeningTab_DoesNotLoadCatalog_AndShowsHint()
    {
        await _vm.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.Empty(_gateway.CatalogCalls);
        Assert.Empty(_vm.Vacancies);
        Assert.True(_vm.IsBeforeFirstSearch);
        Assert.False(_vm.IsEmpty);
        Assert.False(_vm.ShowTotal);
    }

    [Fact]
    public async Task NoCatalogAction_ReadsTheSharedDatabase()
    {
        await _vm.EnsureLoadedCommand.ExecuteAsync(null);
        await SearchSiteAsync(_vm, "A");

        _vm.SearchText = "a";
        _vm.SearchCommand.Execute(null);
        _vm.IsRefreshing = true;
        await _vm.RefreshCommand.ExecuteAsync(null);
        await _vm.FilterExistingCommand.ExecuteAsync(null);
        await _vm.ResetFiltersCommand.ExecuteAsync(null);
        _time.Advance(CatalogViewModel.SearchDebounce);

        Assert.Empty(_gateway.CatalogCalls);
        Assert.False(_vm.IsRefreshing);
        Assert.Equal(["A"], _vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task SiteSearch_FillsList_AndHidesHint()
    {
        await SearchSiteAsync(_vm, "A", "B");

        Assert.Equal(["A", "B"], _vm.Vacancies.Select(v => v.Title));
        Assert.Equal(2, _vm.TotalCount);
        Assert.False(_vm.IsBeforeFirstSearch);
        Assert.True(_vm.ShowTotal);
    }

    [Fact]
    public async Task NoResults_IsEmpty()
    {
        await SearchSiteAsync(_vm, Array.Empty<VacancyDto>());

        Assert.True(_vm.IsEmpty);
        Assert.False(_vm.IsBeforeFirstSearch);
        Assert.Equal("Знайдено вакансій: 0", _vm.TotalText);
    }

    [Fact]
    public async Task UnsaveFromAnotherTab_UpdatesMatchingCardInPlace()
    {
        await SearchSiteAsync(_vm, Vacancies("A", "B").Select(v => v with { IsSaved = true }).ToArray());

        _messenger.Send(new VacancySaveChangedMessage(_vm.Vacancies[1].Id, IsSaved: false));

        Assert.True(_vm.Vacancies[0].IsSaved);
        Assert.False(_vm.Vacancies[1].IsSaved);
    }

    [Fact]
    public async Task Typing_FiltersFoundVacanciesOnlyAfterPause_AndRestartsPauseOnEachKeystroke()
    {
        await SearchSiteAsync(_vm, "QA Engineer", "Python Developer");

        _vm.SearchText = "q";
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _vm.SearchText = "qa ";
        _time.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Equal(2, _vm.Vacancies.Count);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await WaitUntilAsync(() => _vm.Vacancies.Count == 1);

        Assert.Equal(["QA Engineer"], _vm.Vacancies.Select(v => v.Title));
        Assert.Equal(["qa"], _gateway.RecordedSearches);
    }

    [Fact]
    public async Task SameQueryWithExtraSpace_IsRecordedOnce()
    {
        await SearchSiteAsync(_vm, "Senior .NET");

        _vm.SearchText = "net";
        _vm.SearchCommand.Execute(null);
        _vm.SearchText = "net ";
        _time.Advance(CatalogViewModel.SearchDebounce);
        await Task.Delay(50);

        Assert.Equal(["net"], _gateway.RecordedSearches);
        Assert.Equal(["Senior .NET"], _vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public void SearchText_WhenProvided_RecordsSearchInGateway()
    {
        _vm.SearchText = "Python backend";
        _vm.SearchCommand.Execute(null);

        Assert.Contains("Python backend", _gateway.RecordedSearches);
        Assert.Empty(_gateway.CatalogCalls);
    }

    [Fact]
    public async Task TextTypedBeforeSearch_FiltersTheResult()
    {
        _vm.SearchText = "python";
        _vm.SearchCommand.Execute(null);

        await SearchSiteAsync(_vm, "Python Developer", "Java Developer");

        Assert.Equal(["Python Developer"], _vm.Vacancies.Select(v => v.Title));
        Assert.Contains("за фільтрами списку показано 1", _vm.StaleCacheText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FilterBeforeSearch_AsksToSearchFirst_AndAppliesToTheResult()
    {
        _vm.NewBanListWord = "Gambling";
        await _vm.AddBanListWordCommand.ExecuteAsync(null);
        await _vm.FilterExistingCommand.ExecuteAsync(null);

        Assert.Contains("Спочатку спарсіть вакансії", _vm.StaleCacheText, StringComparison.Ordinal);

        await SearchSiteAsync(_vm, "Python Developer", "Gambling Engineer");

        Assert.Equal(["Python Developer"], _vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task ResetFilters_ShowsWholeResultAgain()
    {
        await SearchSiteAsync(_vm, "Senior .NET Developer", "Junior QA");
        _vm.NewWhiteListWord = ".NET";
        await _vm.AddWhiteListWordCommand.ExecuteAsync(null);
        Assert.Single(_vm.Vacancies);

        await _vm.ResetFiltersCommand.ExecuteAsync(null);

        Assert.Equal(2, _vm.Vacancies.Count);
        Assert.Empty(_vm.WhiteListWords);
        Assert.Empty(_gateway.CatalogCalls);
    }

    [Fact]
    public async Task ShowTotal_HiddenBeforeSearch_AndOnError()
    {
        Assert.False(_vm.ShowTotal);
        await SearchSiteAsync(_vm, "A");
        Assert.True(_vm.ShowTotal);

        _gateway.ToggleError = new ApiException(HttpStatusCode.ServiceUnavailable, null, "Сервер повернув помилку 503.");
        await _vm.Vacancies[0].ToggleSaveCommand.ExecuteAsync(null);
        Assert.True(_vm.HasError);
        Assert.False(_vm.ShowTotal);

        _vm.DismissErrorCommand.Execute(null);
        Assert.False(_vm.HasError);
        Assert.True(_vm.ShowTotal);
    }

    [Fact]
    public async Task CardToggleError_IsShownInPageBanner()
    {
        await SearchSiteAsync(_vm, "A");
        _gateway.ToggleError = new ApiException(HttpStatusCode.InternalServerError, null, "Сервер повернув помилку 500.");

        await _vm.Vacancies[0].ToggleSaveCommand.ExecuteAsync(null);

        Assert.Equal("Сервер повернув помилку 500.", _vm.ErrorMessage);
        Assert.False(_vm.Vacancies[0].IsSaved);
    }

    [Fact]
    public async Task Refresh_ResetsRefreshingFlag_AndKeepsFoundVacancies()
    {
        await SearchSiteAsync(_vm, "A");

        _vm.IsRefreshing = true;
        await _vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["A"], _vm.Vacancies.Select(v => v.Title));
        Assert.False(_vm.IsRefreshing);
        Assert.Single(_gateway.SiteSearchRequests); // сайт не перечитується
    }

    [Fact]
    public async Task WhiteListAndBanList_FilterFoundVacanciesInPlace()
    {
        await SearchSiteAsync(_vm, "Senior .NET Developer", "Middle Python Gambling Engineer", "Junior .NET QA");

        _vm.NewWhiteListWord = ".NET, Python";
        await _vm.AddWhiteListWordCommand.ExecuteAsync(null);
        Assert.Equal([".NET", "Python"], _vm.WhiteListWords);
        Assert.Equal(3, _vm.Vacancies.Count);

        _vm.NewBanListWord = "Gambling, Junior";
        await _vm.AddBanListWordCommand.ExecuteAsync(null);
        Assert.Equal(["Gambling", "Junior"], _vm.BanListWords);
        Assert.Equal(["Senior .NET Developer"], _vm.Vacancies.Select(v => v.Title));
        Assert.Equal(1, _vm.TotalCount);
    }

    [Fact]
    public async Task WhiteBanListAndSearch_LookIntoDescription_UnlessTitleOnly()
    {
        VacancyDto[] items = Vacancies("Embedded Engineer", "FPGA Engineer", "Firmware Engineer");
        await SearchSiteAsync(_vm,
            items[0] with { Description = "Пишемо на C++ під STM32" },
            items[1] with { Description = "Verilog, VHDL" },
            items[2] with { Description = "C++ і трохи PHP" });

        _vm.NewWhiteListWord = "c++";
        await _vm.AddWhiteListWordCommand.ExecuteAsync(null);
        Assert.Equal(["Embedded Engineer", "Firmware Engineer"], _vm.Vacancies.Select(v => v.Title));

        _vm.NewBanListWord = "PHP";
        await _vm.AddBanListWordCommand.ExecuteAsync(null);
        Assert.Equal(["Embedded Engineer"], _vm.Vacancies.Select(v => v.Title));

        // «Лише в назві» опис не дивиться: C++ у жодній назві немає
        _vm.UnifiedTitleOnly = true;
        await _vm.FilterExistingCommand.ExecuteAsync(null);
        Assert.Empty(_vm.Vacancies);

        _vm.UnifiedTitleOnly = false;
        await _vm.ResetFiltersCommand.ExecuteAsync(null);
        _vm.SearchText = "stm32";
        _vm.SearchCommand.Execute(null);
        Assert.Equal(["Embedded Engineer"], _vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task SourceFilter_CanKeepOnlyRobotaUa()
    {
        VacancyDto[] items = Vacancies("From Robota", "From Djinni");
        await SearchSiteAsync(_vm, items[0] with { Source = JobSource.RobotaUa }, items[1] with { Source = JobSource.Djinni });

        _vm.SelectedSourceFilter = "Robota.ua";
        await _vm.FilterExistingCommand.ExecuteAsync(null);

        Assert.Contains("Robota.ua", _vm.SourceFilterOptions);
        Assert.Equal(["From Robota"], _vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task WhiteListMatchAll_RequiresEveryWord()
    {
        await SearchSiteAsync(_vm, "Python Django Developer", "Python Developer", "Django Developer");
        _vm.NewWhiteListWord = "Python, Django";
        await _vm.AddWhiteListWordCommand.ExecuteAsync(null);
        Assert.Equal(3, _vm.Vacancies.Count);
        Assert.Equal("Будь-яке слово", _vm.WhiteListMatchText);

        _vm.ToggleWhiteListMatchAllCommand.Execute(null);
        await _vm.FilterExistingCommand.ExecuteAsync(null);

        Assert.Equal("Усі слова", _vm.WhiteListMatchText);
        Assert.Equal(["Python Django Developer"], _vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task HideViewed_HidesServerMarkedAndSessionViewedVacancies()
    {
        VacancyDto[] items = Vacancies("Seen before", "Opened now", "Fresh");
        await SearchSiteAsync(_vm, items[0] with { IsViewed = true }, items[1], items[2]);

        // Розгорнута картка — перегляд цього сеансу; список одразу не змінюється
        _vm.Vacancies.Single(v => v.Title == "Opened now").ToggleDetailsCommand.Execute(null);
        Assert.Equal(3, _vm.Vacancies.Count);

        _vm.ToggleUnifiedHideViewedCommand.Execute(null);
        await _vm.FilterExistingCommand.ExecuteAsync(null);

        Assert.Equal(["Fresh"], _vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task IncludeNoSalary_KeepsVacanciesWithoutSalary_UnderMinSalary()
    {
        VacancyDto[] items = Vacancies("Paid", "Unpaid", "Low");
        await SearchSiteAsync(_vm,
            items[0] with { SalaryMin = 3000, Currency = "USD" },
            items[1],
            items[2] with { SalaryMin = 500, Currency = "USD" });

        _vm.MinSalaryFilter = "1000";
        await _vm.FilterExistingCommand.ExecuteAsync(null);
        Assert.Equal(["Paid"], _vm.Vacancies.Select(v => v.Title));

        _vm.ToggleUnifiedIncludeNoSalaryCommand.Execute(null);
        await _vm.FilterExistingCommand.ExecuteAsync(null);
        Assert.Equal(["Paid", "Unpaid"], _vm.Vacancies.Select(v => v.Title).Order());
    }

    [Fact]
    public async Task SelectedSortOption_SortsFoundVacanciesByTitleAndSalary()
    {
        VacancyDto[] items = Vacancies("B Developer", "C Engineer", "A Architect");
        await SearchSiteAsync(_vm,
            items[0] with { SalaryMin = 2000, SalaryMax = 3000, Currency = "USD" },
            items[1] with { SalaryMin = 5000, SalaryMax = 6000, Currency = "USD" },
            items[2] with { SalaryMin = 1000, SalaryMax = 1500, Currency = "USD" });

        _vm.SelectedSortOption = "За назвою посади (А → Я)";
        Assert.Equal(["A Architect", "B Developer", "C Engineer"], _vm.Vacancies.Select(v => v.Title));

        _vm.SelectedSortOption = "За зарплатою (від найвищої ↓)";
        Assert.Equal(["C Engineer", "B Developer", "A Architect"], _vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task SelectedSortOption_SortsFoundVacanciesByDistance()
    {
        var stubDistance = new StubDistanceService("Київ, Хрещатик 1", new()
        {
            ["Київ, вул. Велика Васильківська 10"] = 1.2,
            ["Київ, пр. Перемоги 50"] = 8.5,
            ["Львів"] = 540.0
        });
        var vm = new CatalogViewModel(_gateway, _messenger, _time, distanceService: stubDistance);
        VacancyDto[] items = Vacancies("Near Vacancy", "Far Vacancy", "Remote Vacancy", "Medium Vacancy");
        await SearchSiteAsync(vm,
            items[0] with { Location = "Київ, вул. Велика Васильківська 10" },
            items[1] with { Location = "Львів" },
            items[2] with { Location = "Remote", Schedule = "Віддалено" },
            items[3] with { Location = "Київ, пр. Перемоги 50" });

        vm.SelectedSortOption = "За відстанню (спочатку ближчі)";
        Assert.Equal(["Near Vacancy", "Medium Vacancy", "Far Vacancy", "Remote Vacancy"], vm.Vacancies.Select(v => v.Title));

        vm.SelectedSortOption = "За відстанню (спочатку дальші)";
        Assert.Equal(["Far Vacancy", "Medium Vacancy", "Near Vacancy", "Remote Vacancy"], vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task SelectedSortOption_DistanceSort_ShowsWarningBanner_WhenNoUserAddress()
    {
        var stubDistance = new StubDistanceService(userAddress: null, new());
        var vm = new CatalogViewModel(_gateway, _messenger, _time, distanceService: stubDistance);
        await SearchSiteAsync(vm, "Job 1", "Job 2");

        vm.SelectedSortOption = "За відстанню (спочатку ближчі)";
        Assert.Contains("Вкажіть адресу в профілі", vm.StaleCacheText);
    }

    [Fact]
    public async Task UserAddressChangedMessage_RefreshesDistanceSort()
    {
        var stubDistance = new StubDistanceService(userAddress: null, new()
        {
            ["Київ, вул. А"] = 5.0,
            ["Київ, вул. Б"] = 2.0
        });
        var vm = new CatalogViewModel(_gateway, _messenger, _time, distanceService: stubDistance);
        VacancyDto[] items = Vacancies("Job A", "Job B");
        await SearchSiteAsync(vm, items[0] with { Location = "Київ, вул. А" }, items[1] with { Location = "Київ, вул. Б" });

        vm.SelectedSortOption = "За відстанню (спочатку ближчі)";
        Assert.Contains("Вкажіть адресу в профілі", vm.StaleCacheText);

        // Користувач встановлює адресу в профілі
        stubDistance.UserAddress = "Київ, Майдан Незалежності";
        _messenger.Send(new UserAddressChangedMessage("Київ, Майдан Незалежності"));

        Assert.Equal(["Job B", "Job A"], vm.Vacancies.Select(v => v.Title));
        Assert.DoesNotContain("Вкажіть адресу в профілі", vm.StaleCacheText);
    }

    [Fact]
    public void GuestBanner_VisibleByDefaultWhenLoggedOut()
    {
        Assert.False(_vm.IsUserLoggedIn);
        Assert.True(_vm.IsUserLoggedOut);
        Assert.False(_vm.IsGuestBannerDismissed);
        Assert.True(_vm.IsGuestBannerVisible);
    }

    [Fact]
    public void DismissGuestBanner_HidesBanner()
    {
        Assert.True(_vm.IsGuestBannerVisible);

        _vm.DismissGuestBannerCommand.Execute(null);

        Assert.True(_vm.IsGuestBannerDismissed);
        Assert.False(_vm.IsGuestBannerVisible);
    }

    [Fact]
    public void UserAuthStateChangedMessage_UpdatesLoginStateAndBanner()
    {
        _messenger.Send(new UserAuthStateChangedMessage(true, "user@waterfall.com"));

        Assert.True(_vm.IsUserLoggedIn);
        Assert.False(_vm.IsUserLoggedOut);
        Assert.False(_vm.IsGuestBannerVisible);

        _messenger.Send(new UserAuthStateChangedMessage(false, null));

        Assert.False(_vm.IsUserLoggedIn);
        Assert.True(_vm.IsUserLoggedOut);
        Assert.False(_vm.IsGuestBannerDismissed);
        Assert.True(_vm.IsGuestBannerVisible);
    }

    [Fact]
    public async Task NavigateToLogin_InvokesConfiguredAction()
    {
        bool invoked = false;
        _vm.NavigateToLoginAction = () =>
        {
            invoked = true;
            return Task.CompletedTask;
        };

        await _vm.NavigateToLoginCommand.ExecuteAsync(null);

        Assert.True(invoked);
    }

    [Fact]
    public async Task RefreshAuthStatus_WithTokenStore_UpdatesIsLoggedIn()
    {
        var tokens = new InMemoryTokenStore();
        var vm = new CatalogViewModel(_gateway, _messenger, _time, tokenStore: tokens);

        Assert.False(vm.IsUserLoggedIn);

        await vm.RefreshAuthStatusAsync();
        Assert.False(vm.IsUserLoggedIn);

        static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string payloadJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["email"] = "user@waterfall.com",
            ["role"] = "JobSeeker",
            ["exp"] = _time.GetUtcNow().AddHours(1).ToUnixTimeSeconds()
        });
        string validJwt = $"{B64("""{"alg":"HS256","typ":"JWT"}""")}.{B64(payloadJson)}.sig";

        await tokens.SetTokenAsync(validJwt);

        await vm.RefreshAuthStatusAsync();
        Assert.True(vm.IsUserLoggedIn);
        Assert.False(vm.IsGuestBannerVisible);
    }

    [Fact]
    public async Task IsCustomSortActive_And_HasActiveLocalFilters_ReflectState()
    {
        Assert.False(_vm.IsCustomSortActive);
        Assert.False(_vm.HasActiveLocalFilters);
        Assert.False(_vm.IsFilterHighlightActive);

        _vm.SelectedSortOption = _vm.SortOptions[1];
        Assert.True(_vm.IsCustomSortActive);

        _vm.ToggleUnifiedOnlyWithSalaryCommand.Execute(null);
        Assert.True(_vm.HasActiveLocalFilters);
        Assert.True(_vm.IsFilterHighlightActive);

        _vm.ToggleUnifiedOnlyWithSalaryCommand.Execute(null);
        _vm.NewWhiteListWord = "C#";
        await _vm.AddWhiteListWordCommand.ExecuteAsync(null);
        Assert.True(_vm.HasActiveLocalFilters);

        await _vm.ResetFiltersCommand.ExecuteAsync(null);
        Assert.False(_vm.IsCustomSortActive);
        Assert.False(_vm.HasActiveLocalFilters);
        Assert.False(_vm.IsFilterHighlightActive);
    }

    [Fact]
    public async Task SiteSearch_PaginatesResultsAt20PerPage_WithoutExtraNetworkCalls()
    {
        VacancyDto[] items = Enumerable.Range(1, 45)
            .Select(i => new VacancyDto { Id = Guid.NewGuid(), Title = $"Job {i}" })
            .ToArray();

        await SearchSiteAsync(_vm, items);
        int requestsAfterSearch = _gateway.SiteSearchRequests.Count;

        Assert.Equal(45, _vm.TotalCount);
        Assert.Equal(3, _vm.TotalPages);
        Assert.Equal(1, _vm.CurrentPage);
        Assert.True(_vm.IsPagerVisible);
        Assert.False(_vm.HasPreviousPage);
        Assert.True(_vm.HasNextPage);
        Assert.Equal("Сторінка 1 з 3", _vm.PageText);
        Assert.Equal(20, _vm.Vacancies.Count);
        Assert.Equal("Job 1", _vm.Vacancies[0].Title);

        _vm.NextPageCommand.Execute(null);
        Assert.Equal(2, _vm.CurrentPage);
        Assert.True(_vm.HasPreviousPage);
        Assert.True(_vm.HasNextPage);
        Assert.Equal("Сторінка 2 з 3", _vm.PageText);
        Assert.Equal(20, _vm.Vacancies.Count);
        Assert.Equal("Job 21", _vm.Vacancies[0].Title);

        _vm.NextPageCommand.Execute(null);
        Assert.Equal(3, _vm.CurrentPage);
        Assert.True(_vm.HasPreviousPage);
        Assert.False(_vm.HasNextPage);
        Assert.Equal("Сторінка 3 з 3", _vm.PageText);
        Assert.Equal(5, _vm.Vacancies.Count);
        Assert.Equal("Job 41", _vm.Vacancies[0].Title);

        _vm.PrevPageCommand.Execute(null);
        Assert.Equal(2, _vm.CurrentPage);
        Assert.Equal(requestsAfterSearch, _gateway.SiteSearchRequests.Count);
    }

    [Fact]
    public async Task SiteSearch_MultiPageWithoutFilters_DoesNotReportFilteredSubsetInBanner()
    {
        VacancyDto[] items = Enumerable.Range(1, 25)
            .Select(i => new VacancyDto { Id = Guid.NewGuid(), Title = $"Dev {i}" })
            .ToArray();

        await SearchSiteAsync(_vm, items);

        Assert.Equal(20, _vm.Vacancies.Count);
        Assert.Equal(25, _vm.TotalCount);
        Assert.DoesNotContain("за фільтрами списку показано", _vm.StaleCacheText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FilterChange_ResetsToFirstPage_AndHidesPagerWhenSinglePage()
    {
        VacancyDto[] items = Enumerable.Range(1, 25)
            .Select(i => new VacancyDto { Id = Guid.NewGuid(), Title = i <= 5 ? $"Rust Dev {i}" : $"Java Dev {i}" })
            .ToArray();

        await SearchSiteAsync(_vm, items);
        _vm.NextPageCommand.Execute(null);
        Assert.Equal(2, _vm.CurrentPage);

        _vm.NewWhiteListWord = "Rust";
        await _vm.AddWhiteListWordCommand.ExecuteAsync(null);

        Assert.Equal(1, _vm.CurrentPage);
        Assert.Equal(5, _vm.TotalCount);
        Assert.False(_vm.IsPagerVisible);
        Assert.False(_vm.HasNextPage);

        _vm.NewBanListWord = "Rust";
        await _vm.AddBanListWordCommand.ExecuteAsync(null);

        Assert.Equal(1, _vm.CurrentPage);
        Assert.Equal(0, _vm.TotalCount);
        Assert.True(_vm.IsEmpty);
        Assert.False(_vm.IsPagerVisible);
    }

    [Fact]
    public async Task ExportVacancies_WithMultiplePages_ExportsAllFilteredVacancies()
    {
        VacancyDto[] items = Enumerable.Range(1, 25)
            .Select(i => new VacancyDto { Id = Guid.NewGuid(), Title = $"Exported {i}" })
            .ToArray();
        await SearchSiteAsync(_vm, items);
        Assert.Equal(20, _vm.Vacancies.Count);

        string tempPath = Path.Combine(Path.GetTempPath(), $"waterfall-export-{Guid.NewGuid():N}.json");
        try
        {
            await _vm.ExportVacanciesToAsync(tempPath);
            string json = await File.ReadAllTextAsync(tempPath);
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal(25, doc.RootElement.GetArrayLength());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task SaveMessageForOffPageVacancy_PreservesStateWhenNavigatingToThatPage()
    {
        VacancyDto[] items = Enumerable.Range(1, 25)
            .Select(i => new VacancyDto { Id = Guid.NewGuid(), Title = $"Vacancy {i}" })
            .ToArray();
        await SearchSiteAsync(_vm, items);

        Guid pageTwoTargetId = items[21].Id;
        _messenger.Send(new VacancySaveChangedMessage(pageTwoTargetId, IsSaved: true));

        _vm.NextPageCommand.Execute(null);

        Assert.True(_vm.Vacancies[1].IsSaved);
    }

    private static VacancyDto[] Vacancies(params string[] titles) => [.. FakeGateway.Page(1, titles.Length, hasNext: false, titles).Items];

    private Task SearchSiteAsync(CatalogViewModel vm, params string[] titles) => SearchSiteAsync(vm, Vacancies(titles));

    /// <summary>«Знайти» на панелі DOU; сервер одразу відповідає завершеним пошуком із цими вакансіями.</summary>
    private async Task SearchSiteAsync(CatalogViewModel vm, params VacancyDto[] found)
    {
        _gateway.SiteSearchStatuses.Clear();
        _gateway.SiteSearchStatuses.Enqueue(new SiteSearchStatusDto
        {
            State = SiteSearchStatusDto.Completed,
            PagesDone = 1,
            PagesTotal = 1,
            Found = found.Length,
            Vacancies = [.. found]
        });

        await vm.FetchFromSitesCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class StubDistanceService : Waterfall.Client.Interfaces.IDistanceCalculatorService
    {
        private readonly Dictionary<string, double?> _distances;

        public StubDistanceService(string? userAddress, Dictionary<string, double?> distances)
        {
            UserAddress = userAddress;
            _distances = distances;
        }

        public string? UserAddress { get; set; }

        public Task<double?> CalculateDistanceKmAsync(string destinationAddress, CancellationToken ct = default)
        {
            if (_distances.TryGetValue(destinationAddress, out double? km))
            {
                return Task.FromResult(km);
            }
            return Task.FromResult<double?>(null);
        }
    }
}
