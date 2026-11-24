// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: тести збереження й відновлення фільтрів каталогу (REL-002); перевірено: dotnet test, самоперевірка автора (§8)

using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.SiteSearch;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

/// <summary>
/// REL-002: активні фільтри каталогу записуються в сховище в момент зміни, а новий екземпляр
/// каталогу (наступний запуск після аварійного завершення) показує їх у тому самому стані.
/// </summary>
public class CatalogFilterPersistenceTests
{
    private readonly MemoryFilterStorage _storage = new();

    [Fact]
    public void FirstLaunch_NothingSaved_KeepsDefaults_AndWritesNothing()
    {
        CatalogViewModel vm = NewViewModel(out _, out _);

        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        Assert.Equal(vm.SortOptions[0], vm.SelectedSortOption);
        Assert.Empty(vm.WhiteListWords);
        Assert.Equal(0, _storage.SaveCount);
    }

    [Fact]
    public async Task EveryFilterChange_IsWrittenImmediately()
    {
        CatalogViewModel vm = NewViewModel(out _, out _);
        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        vm.UnifiedOnlyWithSalary = true;
        Assert.True(Saved().OnlyWithSalary);

        vm.NewBanListWord = "Gambling";
        await vm.AddBanListWordCommand.ExecuteAsync(null);
        Assert.Equal(["Gambling"], Saved().BanListWords);

        vm.SelectedSortOption = vm.SortOptions[2];
        Assert.Equal(vm.SortOptions[2], Saved().SortOption);
    }

    [Fact]
    public void UnrelatedProperties_AreNotWritten()
    {
        CatalogViewModel vm = NewViewModel(out _, out _);
        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        vm.IsLocalFilterPanelVisible = !vm.IsLocalFilterPanelVisible;
        vm.NewWhiteListWord = "ще не додане";

        Assert.Equal(0, _storage.SaveCount);
    }

    [Fact]
    public async Task NextLaunch_RestoresFilters_AndAppliesThemToTheNextSearch()
    {
        CatalogViewModel before = NewViewModel(out _, out FakeTimeProvider beforeTime);
        CatalogFilterPersistence first = CatalogFilterPersistence.Attach(before, _storage);
        before.SearchText = "python";
        beforeTime.Advance(CatalogViewModel.SearchDebounce);
        before.NewBanListWord = "Gambling";
        await before.AddBanListWordCommand.ExecuteAsync(null);
        before.SelectedSortOption = before.SortOptions[6];
        before.UnifiedCompanyFilter = "Acme";
        before.UnifiedCompanyFilter = string.Empty;
        before.WhiteListMatchAll = true;
        first.Dispose();

        CatalogViewModel after = NewViewModel(out FakeGateway gateway, out _);
        using CatalogFilterPersistence second = CatalogFilterPersistence.Attach(after, _storage);

        Assert.Equal("python", after.SearchText);
        Assert.Equal(["Gambling"], after.BanListWords);
        Assert.Equal(after.SortOptions[6], after.SelectedSortOption);
        Assert.True(after.WhiteListMatchAll);
        Assert.True(after.HasBanListWords);
        Assert.True(after.HasActiveLocalFilters);

        await SearchSiteAsync(after, gateway, "Python Developer", "Gambling Python Dev", "Java Developer");

        Assert.Equal(["Python Developer"], after.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task RestoredSearchText_IsNotRecordedInSearchHistoryAgain()
    {
        _storage.Value = Serialize(new CatalogFilterState { SearchText = "python" });
        CatalogViewModel vm = NewViewModel(out FakeGateway gateway, out FakeTimeProvider time);

        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);
        time.Advance(CatalogViewModel.SearchDebounce);
        await Task.Delay(50);

        Assert.Equal("python", vm.SearchText);
        Assert.Empty(gateway.RecordedSearches);
    }

    [Fact]
    public async Task ResetFilters_WritesDefaults()
    {
        _storage.Value = Serialize(new CatalogFilterState
        {
            OnlyActive = true,
            WhiteListWords = [".NET"],
            SourceFilter = "Djinni"
        });
        CatalogViewModel vm = NewViewModel(out _, out _);
        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        await vm.ResetFiltersCommand.ExecuteAsync(null);

        CatalogFilterState saved = Saved();
        Assert.False(saved.OnlyActive);
        Assert.Empty(saved.WhiteListWords);
        Assert.Equal(vm.SourceFilterOptions[0], saved.SourceFilter);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"version":99,"onlyActive":true}""")]
    [InlineData("null")]
    public void DamagedOrForeignRecord_IsIgnored(string stored)
    {
        _storage.Value = stored;
        CatalogViewModel vm = NewViewModel(out _, out _);

        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        Assert.False(vm.UnifiedOnlyActive);
        Assert.Equal(vm.SortOptions[0], vm.SelectedSortOption);
    }

    [Fact]
    public void UnknownOptions_FallBackToDefaults_AndLongValuesAreClipped()
    {
        _storage.Value = Serialize(new CatalogFilterState
        {
            SortOption = "Неіснуюче сортування",
            SourceFilter = "LinkedIn",
            Format = "Гібрид",
            CompanyFilter = new string('x', 5000),
            BanListWords = ["  spam ", "SPAM", "", new string('y', 5000)]
        });
        CatalogViewModel vm = NewViewModel(out _, out _);

        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        Assert.Equal(vm.SortOptions[0], vm.SelectedSortOption);
        Assert.Equal(vm.SourceFilterOptions[0], vm.SelectedSourceFilter);
        Assert.Equal("Гібрид", vm.SelectedFormat);
        Assert.Equal(CatalogFilterPersistence.MaxTextLength, vm.UnifiedCompanyFilter.Length);
        Assert.Equal(["spam"], vm.BanListWords);
    }

    [Fact]
    public void AfterDispose_ChangesAreNoLongerWritten()
    {
        CatalogViewModel vm = NewViewModel(out _, out _);
        CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        persistence.Dispose();
        vm.UnifiedHideViewed = true;

        Assert.Equal(0, _storage.SaveCount);
    }

    [Fact]
    public void BrokenStorage_DoesNotBreakTheCatalog()
    {
        ThrowingStorage storage = new();
        CatalogViewModel vm = NewViewModel(out _, out _);

        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, storage);
        vm.UnifiedOnlyWithSalary = true;

        Assert.True(vm.UnifiedOnlyWithSalary);
    }

    [Fact]
    public void SiteSearchForms_AreRestoredOnNextLaunch()
    {
        CatalogViewModel before = NewViewModel(out _, out _);
        CatalogFilterPersistence first = CatalogFilterPersistence.Attach(before, _storage);
        before.DouSelectedCategory = before.DouCategoryOptions[3];
        before.DouIsRemote = true;
        before.WorkUaPagesToParse = "5";
        before.WorkUaSelectedCity = before.WorkUaCityOptions[2];
        before.WorkUaCategoryChoices.Single(c => c.Label == "Транспорт, автобізнес").IsSelected = true;
        before.WorkUaLanguageChoices[0].IsSelected = true;
        before.DjinniSelectedEnglish = before.DjinniEnglishOptions[2];
        before.DjinniHasReservation = true;
        before.RobotaUaSearchQuery = "водій";
        first.Dispose();

        CatalogViewModel after = NewViewModel(out _, out _);
        using CatalogFilterPersistence second = CatalogFilterPersistence.Attach(after, _storage);

        Assert.Equal(after.DouCategoryOptions[3], after.DouSelectedCategory);
        Assert.True(after.DouIsRemote);
        Assert.Equal("5", after.WorkUaPagesToParse);
        Assert.Equal(after.WorkUaCityOptions[2], after.WorkUaSelectedCity);
        Assert.Equal(["Транспорт, автобізнес"], after.WorkUaCategoryChoices.Where(c => c.IsSelected).Select(c => c.Label));
        Assert.True(after.WorkUaLanguageChoices[0].IsSelected);
        Assert.Equal(after.DjinniEnglishOptions[2], after.DjinniSelectedEnglish);
        Assert.True(after.DjinniHasReservation);
        Assert.Equal("водій", after.RobotaUaSearchQuery);
    }

    [Fact]
    public void TickingWorkUaSphere_IsWrittenImmediately()
    {
        CatalogViewModel vm = NewViewModel(out _, out _);
        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        vm.WorkUaCategoryChoices.Single(c => c.Label == "Транспорт, автобізнес").IsSelected = true;

        Assert.Equal(["category=24"], Saved().SiteChoices);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("1000")]
    [InlineData("-3")]
    [InlineData("")]
    public void InvalidPageCount_KeepsDefault(string pages)
    {
        _storage.Value = Serialize(new CatalogFilterState
        {
            SiteFilters = new Dictionary<string, string> { [nameof(CatalogViewModel.DjinniPagesToParse)] = pages }
        });
        CatalogViewModel vm = NewViewModel(out _, out _);

        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        Assert.Equal("3", vm.DjinniPagesToParse);
    }

    [Fact]
    public void InvalidSiteValues_KeepDefaults_AndUnknownKeysAreIgnored()
    {
        _storage.Value = Serialize(new CatalogFilterState
        {
            SiteFilters = new Dictionary<string, string>
            {
                [nameof(CatalogViewModel.DouSelectedCity)] = "Атлантида",
                [nameof(CatalogViewModel.DouIsRemote)] = "можливо",
                [nameof(CatalogViewModel.SiteKeywords)] = new string('k', 5000),
                ["IsAdmin"] = "true"
            },
            SiteChoices = ["category=24", "category=99999", "garbage"]
        });
        CatalogViewModel vm = NewViewModel(out _, out _);

        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        Assert.Equal(vm.DouCityOptions[0], vm.DouSelectedCity);
        Assert.False(vm.DouIsRemote);
        Assert.Equal(CatalogFilterPersistence.MaxTextLength, vm.SiteKeywords.Length);
        Assert.Equal(["Транспорт, автобізнес"], vm.WorkUaCategoryChoices.Where(c => c.IsSelected).Select(c => c.Label));
    }

    [Fact]
    public void RecordWithoutSiteForms_KeepsSiteDefaults()
    {
        _storage.Value = """{"version":1,"banListWords":["Gambling"]}""";
        CatalogViewModel vm = NewViewModel(out _, out _);

        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);

        Assert.Equal(["Gambling"], vm.BanListWords);
        Assert.Equal("3", vm.WorkUaPagesToParse);
        Assert.Equal(vm.DjinniCategoryOptions[0], vm.DjinniSelectedCategory);
        Assert.DoesNotContain(vm.WorkUaCategoryChoices, c => c.IsSelected);
    }

    [Fact]
    public async Task ResetFilters_ClearsSavedSiteFormsToo()
    {
        CatalogViewModel vm = NewViewModel(out _, out _);
        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(vm, _storage);
        vm.DouSelectedCategory = vm.DouCategoryOptions[3];
        vm.WorkUaCategoryChoices[0].IsSelected = true;

        await vm.ResetFiltersCommand.ExecuteAsync(null);

        CatalogFilterState saved = Saved();
        Assert.Equal(vm.DouCategoryOptions[0], saved.SiteFilters![nameof(CatalogViewModel.DouSelectedCategory)]);
        Assert.Empty(saved.SiteChoices!);
    }

    /// <summary>
    /// Кожне поле таблиці отримує значення, відмінне від стандартного, і після відновлення читається тим самим:
    /// ловить поле, яке читає одну властивість, а записує іншу, і стандартне значення поза списком варіантів.
    /// </summary>
    [Fact]
    public void EverySiteField_RoundTripsANonDefaultValue()
    {
        CatalogViewModel defaults = NewViewModel(out _, out _);
        Dictionary<string, string> expected = [];
        foreach (CatalogSiteFilterFields.Field field in CatalogSiteFilterFields.All)
        {
            string current = field.Get(defaults);
            expected[field.Name] = field.Kind switch
            {
                CatalogSiteFilterFields.FieldKind.Flag => current == "true" ? "false" : "true",
                CatalogSiteFilterFields.FieldKind.Pages => current == "7" ? "8" : "7",
                CatalogSiteFilterFields.FieldKind.Option => field.Options!(defaults).Last(o => o != current),
                _ => "значення " + field.Name
            };

            if (field.Kind == CatalogSiteFilterFields.FieldKind.Option)
            {
                Assert.Contains(current, field.Options!(defaults));
            }
        }

        _storage.Value = Serialize(new CatalogFilterState { SiteFilters = expected });
        CatalogViewModel restored = NewViewModel(out _, out _);
        using CatalogFilterPersistence persistence = CatalogFilterPersistence.Attach(restored, _storage);

        Assert.Equal(expected, CatalogFilterPersistence.Capture(restored).SiteFilters);
        Assert.Equal(CatalogSiteFilterFields.All.Count, CatalogSiteFilterFields.All.Select(f => f.Name).Distinct().Count());
    }

    private CatalogFilterState Saved() => CatalogFilterPersistence.Deserialize(_storage.Value!)!;

    private static string Serialize(CatalogFilterState state) => CatalogFilterPersistence.Serialize(state);

    private static CatalogViewModel NewViewModel(out FakeGateway gateway, out FakeTimeProvider time)
    {
        gateway = new FakeGateway();
        time = new FakeTimeProvider();
        return new CatalogViewModel(gateway, new StrongReferenceMessenger(), time);
    }

    /// <summary>«Знайти» на панелі DOU; сервер одразу відповідає завершеним пошуком із цими вакансіями.</summary>
    private static async Task SearchSiteAsync(CatalogViewModel vm, FakeGateway gateway, params string[] titles)
    {
        VacancyDto[] found = [.. FakeGateway.Page(1, titles.Length, hasNext: false, titles).Items];
        gateway.SiteSearchStatuses.Clear();
        gateway.SiteSearchStatuses.Enqueue(new SiteSearchStatusDto
        {
            State = SiteSearchStatusDto.Completed,
            PagesDone = 1,
            PagesTotal = 1,
            Found = found.Length,
            Vacancies = [.. found]
        });

        await vm.FetchFromSitesCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class MemoryFilterStorage : IFilterStateStorage
    {
        public string? Value { get; set; }
        public int SaveCount { get; private set; }

        public string? Load() => Value;

        public void Save(string value)
        {
            Value = value;
            SaveCount++;
        }
    }

    private sealed class ThrowingStorage : IFilterStateStorage
    {
        public string? Load() => throw new IOException("сховище недоступне");

        public void Save(string value) => throw new IOException("сховище недоступне");
    }
}
