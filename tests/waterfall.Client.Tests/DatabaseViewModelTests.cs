// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;
using Waterfall.Client.ViewModels;
using Waterfall.Repository.Entities;

namespace Waterfall.Client.Tests;

public sealed class DatabaseViewModelTests : IDisposable
{
    private readonly string _tempDbPath = Path.Combine(Path.GetTempPath(), $"waterfall-dbvm-{Guid.NewGuid():N}.db");
    private readonly FakeGateway _gateway = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly EfLocalVacancyStore _localStore;

    public DatabaseViewModelTests()
    {
        _localStore = new EfLocalVacancyStore(_tempDbPath);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
        }
        catch
        {
        }
    }

    private List<VacancyDto> SampleFourSiteVacancies()
    {
        DateTimeOffset now = _time.GetUtcNow();
        return
        [
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = "dou-1",
                Title = "Senior .NET Developer",
                Company = "TechUA",
                Description = "ASP.NET Core, Docker, SQL",
                Category = ".NET",
                CategoryGroup = "Software Development",
                Location = "Київ",
                WorkFormat = WorkFormat.Remote,
                Experience = "3+ роки",
                ExperienceMonths = 36,
                SalaryMin = 3500,
                SalaryMax = 4500,
                Currency = "USD",
                Source = JobSource.Dou,
                SourceUrl = "https://jobs.dou.ua/1",
                Skills = ["C#", ".NET", "Docker"],
                PublishedAt = now.AddHours(-5),
                FirstSeenAt = now.AddHours(-4),
                LastSeenAt = now.AddHours(-1),
                IsActive = true,
                IsSaved = true
            },
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = "work-1",
                Title = "Junior QA Engineer",
                Company = "QualityLab",
                Description = "Manual testing, Jira, SQL",
                Category = "QA Manual",
                CategoryGroup = "QA",
                Location = "Львів",
                WorkFormat = WorkFormat.Office,
                Experience = "Без досвіду",
                ExperienceMonths = 0,
                SalaryMin = 30000,
                SalaryMax = 41500,
                Currency = "UAH",
                Source = JobSource.WorkUa,
                SourceUrl = "https://www.work.ua/jobs/1/",
                Skills = ["QA", "Jira"],
                PublishedAt = now.AddDays(-2),
                FirstSeenAt = now.AddDays(-2),
                LastSeenAt = now.AddDays(-1),
                IsActive = true,
                IsViewed = true
            },
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = "dj-1",
                Title = "ML Engineer (LLM)",
                Company = "AI Startup",
                Description = "Python, PyTorch, LLM",
                Category = "AI/ML",
                CategoryGroup = "AI / ML",
                Location = "Віддалено",
                WorkFormat = WorkFormat.Remote,
                Experience = "5+ років",
                ExperienceMonths = 60,
                SalaryMin = 5000,
                SalaryMax = 6500,
                Currency = "USD",
                Source = JobSource.Djinni,
                SourceUrl = "https://djinni.co/jobs/1/",
                Skills = ["Python", "PyTorch"],
                PublishedAt = now.AddDays(-10),
                FirstSeenAt = now.AddDays(-9),
                LastSeenAt = now.AddDays(-8),
                IsActive = false
            },
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = "rob-1",
                Title = "Фінансовий аналітик",
                Company = "FinBank",
                Description = "Excel, фінансове моделювання, бухгалтерія",
                Category = "Фінанси",
                CategoryGroup = "Finance",
                Location = "Одеса",
                WorkFormat = WorkFormat.Hybrid,
                Experience = "1–3 роки",
                ExperienceMonths = 18,
                SalaryMin = 60000,
                SalaryMax = 83000,
                Currency = "UAH",
                Source = JobSource.RobotaUa,
                SourceUrl = "https://robota.ua/company1/vacancy1",
                Skills = ["Excel", "Finance"],
                PublishedAt = now.AddDays(-20),
                FirstSeenAt = now.AddDays(-19),
                LastSeenAt = now.AddDays(-18),
                IsActive = true
            }
        ];
    }

    private void SetupPagedServer(List<VacancyDto> items, int pageSize = 2)
    {
        _gateway.VacanciesQueryHandler = (opts, _) =>
        {
            List<VacancyDto> slice = items.Skip((opts.Page - 1) * pageSize).Take(pageSize).ToList();
            bool hasNext = opts.Page * pageSize < items.Count;
            return Task.FromResult(new PagedResult<VacancyDto>
            {
                Items = slice,
                Page = opts.Page,
                PageSize = pageSize,
                TotalCount = items.Count,
                HasNextPage = hasNext
            });
        };
    }

    [Fact]
    public async Task EnsureLoaded_FetchesAllPagesAcrossFourSites_AndUpdatesCountsAndPickers()
    {
        List<VacancyDto> items = SampleFourSiteVacancies();
        SetupPagedServer(items, pageSize: 2);
        DatabaseViewModel vm = new(_gateway, _messenger, _time, localStore: _localStore);

        await vm.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.Equal(4, vm.TotalInDatabase);
        Assert.Equal(4, vm.FilteredCount);
        Assert.Equal((1, 1, 1, 1), (vm.DouCount, vm.WorkUaCount, vm.DjinniCount, vm.RobotaUaCount));
        Assert.False(vm.IsOfflineMode);
        Assert.Contains(".NET", vm.AvailableCategories);
        Assert.Contains("Київ", vm.AvailableLocations);
        Assert.Equal(4, (await _localStore.GetAllAsync()).Count);
    }

    [Fact]
    public async Task CrossSiteFiltering_SupportsFourSiteToggles_Categories_Format_Experience_Salary_Freshness_Words()
    {
        List<VacancyDto> items = SampleFourSiteVacancies();
        SetupPagedServer(items, pageSize: 10);
        DatabaseViewModel vm = new(_gateway, _messenger, _time);
        await vm.EnsureLoadedCommand.ExecuteAsync(null);

        // 1. Мультивибір джерел (DOU + Djinni)
        vm.IncludeWorkUa = false;
        vm.IncludeRobotaUa = false;
        Assert.Equal(2, vm.FilteredCount);

        vm.SelectAllSourcesCommand.Execute(null);
        Assert.Equal(4, vm.FilteredCount);

        // 2. Широка канонічна категорія
        vm.SelectedCategoryGroup = "Finance";
        Assert.Equal(["Фінансовий аналітик"], vm.Vacancies.Select(v => v.Title));
        vm.ResetFiltersCommand.Execute(null);

        // 3. Формат роботи
        vm.SelectedWorkFormat = "Віддалено";
        Assert.Equal(2, vm.FilteredCount);
        vm.ResetFiltersCommand.Execute(null);

        // 4. Досвід роботи (0 міс)
        vm.SelectedExperience = "Без досвіду (0)";
        Assert.Equal(["Junior QA Engineer"], vm.Vacancies.Select(v => v.Title));
        vm.ResetFiltersCommand.Execute(null);

        // 5. Крос-валютна зарплата (від $1500 має пропустити 83 000 грн ≈ $2000 з Robota.ua!)
        vm.SelectedSalaryCurrency = "$ USD";
        vm.MinSalaryFilter = "1500";
        vm.ApplyFiltersCommand.Execute(null);
        Assert.Equal(3, vm.FilteredCount);
        Assert.Contains(vm.Vacancies, v => v.Title == "Фінансовий аналітик");
        vm.ResetFiltersCommand.Execute(null);

        // 6. Свіжість публікації (За 3 дні)
        vm.SelectedFreshness = "За 3 дні";
        Assert.Equal(2, vm.FilteredCount);
        vm.ResetFiltersCommand.Execute(null);

        // 7. Статус (Лише архівні)
        vm.SelectedStatus = "Лише архівні";
        Assert.Equal(["ML Engineer (LLM)"], vm.Vacancies.Select(v => v.Title));
        vm.ResetFiltersCommand.Execute(null);

        // 8. Обрані + Без переглянутих
        vm.OnlySaved = true;
        Assert.Equal(["Senior .NET Developer"], vm.Vacancies.Select(v => v.Title));
        vm.OnlySaved = false;
        vm.HideViewed = true;
        Assert.Equal(3, vm.FilteredCount);
        vm.HideViewed = false;

        // 9. Вайт-лист (Усі слова) + Бан-лист
        vm.NewWhiteListWord = "SQL";
        vm.AddWhiteListWordCommand.Execute(null);
        Assert.Equal(2, vm.FilteredCount);
        vm.NewBanListWord = "Jira";
        vm.AddBanListWordCommand.Execute(null);
        Assert.Equal(["Senior .NET Developer"], vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task Sorting_OrdersBySalaryExperienceTitleCompanyAndCategory()
    {
        SetupPagedServer(SampleFourSiteVacancies(), pageSize: 10);
        DatabaseViewModel vm = new(_gateway, _messenger, _time);
        await vm.EnsureLoadedCommand.ExecuteAsync(null);

        vm.SelectedSortOption = "За зарплатою (від найвищої ↓)";
        Assert.Equal("ML Engineer (LLM)", vm.Vacancies[0].Title);
        Assert.Equal("Junior QA Engineer", vm.Vacancies[^1].Title);

        vm.SelectedSortOption = "За досвідом (від меншого ↑)";
        Assert.Equal("Junior QA Engineer", vm.Vacancies[0].Title);

        vm.SelectedSortOption = "За категорією (А → Я)";
        Assert.Equal("AI / ML", vm.Vacancies[0].CategoryGroup);
    }

    [Fact]
    public async Task Load_WhenServerRefusesClientVacancies_ShowsRefreshOnlyAndKeepsChangesOnDevice()
    {
        VacancyDto localOffline = SampleFourSiteVacancies()[0] with { Title = "Offline Local Vacancy" };
        await _localStore.SaveVacanciesAsync([localOffline], isSynced: false);

        _gateway.VacanciesAcceptedByServer = false;
        SetupPagedServer(SampleFourSiteVacancies());
        VacancySyncService syncService = new(_gateway, _localStore, _messenger);
        DatabaseViewModel vm = new(_gateway, _messenger, _time, localStore: _localStore, syncService: syncService);

        await vm.EnsureLoadedCommand.ExecuteAsync(null);

        // Відмову сервера видно вже після завантаження: кнопка обіцяє лише оновлення каталогу
        Assert.False(vm.CanUploadLocalChanges);
        Assert.False(vm.HasUnsyncedChanges);
        Assert.Equal("Оновити каталог", vm.SyncButtonText);
        Assert.Contains("Лише на пристрої: 1", vm.SyncBadgeText);
        Assert.Equal(1, vm.UnsyncedCount);
        Assert.Single(await _localStore.GetUnsyncedAsync());

        // Ручна синхронізація не вигадує «надіслано» і не позначає локальні зміни синхронізованими
        await vm.SyncDatabaseCommand.ExecuteAsync(null);

        Assert.DoesNotContain("надіслано", vm.StatusBannerText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Каталог оновлено", vm.StatusBannerText);
        Assert.Single(await _localStore.GetUnsyncedAsync());
    }

    [Fact]
    public async Task OfflineFallback_LoadsLocalDatabase_AndManualSyncExchangesData()
    {
        // Попередньо наповнюємо локальну БД офлайн-вакансією
        VacancyDto localOffline = SampleFourSiteVacancies()[0] with { Title = "Offline Local Vacancy" };
        await _localStore.SaveVacanciesAsync([localOffline], isSynced: false);

        _gateway.VacanciesQueryHandler = (_, _) => Task.FromException<PagedResult<VacancyDto>>(new ApiException(null, null, "No network"));
        _gateway.VacancySyncHandler = _ => throw new ApiException(null, null, "No network");
        VacancySyncService syncService = new(_gateway, _localStore, _messenger);
        DatabaseViewModel vm = new(_gateway, _messenger, _time, localStore: _localStore, syncService: syncService);

        await vm.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.True(vm.IsOfflineMode);
        Assert.Single(vm.Vacancies);
        Assert.Equal("Offline Local Vacancy", vm.Vacancies[0].Title);
        Assert.Equal(1, vm.UnsyncedCount);

        // Сервер повертається онлайн і має іншу вакансію
        VacancyDto serverNew = SampleFourSiteVacancies()[2] with { Title = "Server Collected Vacancy" };
        _gateway.VacancySyncHandler = req => new VacancySyncResponseDto
        {
            ReceivedFromClient = req.ClientVacancies.Count,
            CreatedOnServer = req.ClientVacancies.Count,
            TotalServerVacancies = 2,
            ServerTime = _time.GetUtcNow(),
            ServerVacancies =
            [
                new VacancySyncItemDto
                {
                    Id = serverNew.Id,
                    ExternalId = serverNew.ExternalId,
                    Title = serverNew.Title,
                    Company = serverNew.Company,
                    Description = serverNew.Description,
                    Category = serverNew.Category,
                    Location = serverNew.Location,
                    Currency = serverNew.Currency,
                    Source = serverNew.Source,
                    SourceUrl = serverNew.SourceUrl
                }
            ]
        };
        SetupPagedServer([localOffline, serverNew], pageSize: 10);

        await vm.SyncDatabaseCommand.ExecuteAsync(null);

        Assert.False(vm.IsOfflineMode);
        Assert.Equal(0, vm.UnsyncedCount);
        Assert.Equal(2, vm.TotalInDatabase);
        Assert.Contains("Синхронізація завершена", vm.StatusBannerText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportImportJson_SearchDebounce_AndAdditionalFilters_WorkEndToEnd()
    {
        List<VacancyDto> items = SampleFourSiteVacancies();
        SetupPagedServer(items, pageSize: 10);
        DatabaseViewModel vm = new(_gateway, _messenger, _time, localStore: _localStore);
        await vm.EnsureLoadedCommand.ExecuteAsync(null);

        // Пошук із debounce + режим «лише в заголовку»
        vm.SearchText = "Docker";
        _time.Advance(TimeSpan.FromMilliseconds(350));
        await Task.Yield();
        Assert.Equal(["Senior .NET Developer"], vm.Vacancies.Select(v => v.Title));

        vm.ToggleTitleOnlyCommand.Execute(null);
        Assert.Equal(0, vm.FilteredCount);
        vm.ToggleTitleOnlyCommand.Execute(null);
        vm.SearchText = string.Empty;
        _time.Advance(TimeSpan.FromMilliseconds(350));
        await Task.Yield();
        Assert.Equal(4, vm.FilteredCount);

        // Фільтри за компанією, локацією та стеком
        vm.CompanyFilter = "FinBank";
        vm.ApplyFiltersCommand.Execute(null);
        Assert.Equal(["Фінансовий аналітик"], vm.Vacancies.Select(v => v.Title));
        vm.ResetFiltersCommand.Execute(null);

        vm.LocationFilter = "Львів";
        vm.ApplyFiltersCommand.Execute(null);
        Assert.Equal(["Junior QA Engineer"], vm.Vacancies.Select(v => v.Title));
        vm.ResetFiltersCommand.Execute(null);

        vm.SkillsFilter = "PyTorch, LLM";
        vm.ApplyFiltersCommand.Execute(null);
        Assert.Equal(["ML Engineer (LLM)"], vm.Vacancies.Select(v => v.Title));
        vm.ResetFiltersCommand.Execute(null);

        // Експорт відфільтрованого списку у JSON та імпорт назад у локальну БД
        string exportPath = Path.Combine(Path.GetTempPath(), $"waterfall-export-{Guid.NewGuid():N}.json");
        try
        {
            await vm.ExportDatabaseToAsync(exportPath);
            Assert.True(File.Exists(exportPath));
            Assert.Contains("Експортовано 4", vm.StatusBannerText, StringComparison.Ordinal);

            await vm.ImportDatabaseFromAsync(exportPath);
            Assert.Contains("Імпортовано 4", vm.StatusBannerText, StringComparison.Ordinal);
            Assert.Equal(4, vm.UnsyncedCount);
        }
        finally
        {
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }
        }
    }

    [Fact]
    public async Task TogglesWordListsMessagesDistanceSortAndDispose_WorkAsExpected()
    {
        List<VacancyDto> items = SampleFourSiteVacancies();
        items[0] = items[0] with { Currency = "EUR", SalaryMin = 2000m, SalaryMax = 3000m, FullAddress = "Київ, Хрещатик 1" };
        items[1] = items[1] with { Location = "Віддалено", FullAddress = null };
        SetupPagedServer(items, pageSize: 10);

        StubDistanceService distanceService = new() { UserAddress = "Київ, Політехнічна 1" };
        using DatabaseViewModel vm = new(_gateway, _messenger, _time, distanceService: distanceService, localStore: _localStore);
        await vm.EnsureLoadedCommand.ExecuteAsync(null);

        // Toggle-команди панелі й джерел
        vm.ToggleFilterPanelCommand.Execute(null);
        Assert.True(vm.IsFilterPanelVisible);

        vm.ToggleDouFilterCommand.Execute(null);
        Assert.False(vm.IncludeDou);
        vm.ToggleWorkUaFilterCommand.Execute(null);
        vm.ToggleDjinniFilterCommand.Execute(null);
        vm.ToggleRobotaUaFilterCommand.Execute(null);
        Assert.Equal(0, vm.FilteredCount);

        vm.SelectAllSourcesCommand.Execute(null);
        Assert.Equal(4, vm.FilteredCount);

        // Вайт-лист і бан-лист команди
        vm.NewWhiteListWord = "Senior, ML";
        vm.AddWhiteListWordCommand.Execute(null);
        Assert.True(vm.HasWhiteListWords);
        vm.ToggleWhiteListMatchAllCommand.Execute(null);
        Assert.Equal(0, vm.FilteredCount);
        vm.RemoveWhiteListWordCommand.Execute("ML");
        Assert.Equal(1, vm.FilteredCount);
        vm.ClearWhiteListWordsCommand.Execute(null);
        Assert.False(vm.HasWhiteListWords);

        vm.NewBanListWord = "QA";
        vm.AddBanListWordCommand.Execute(null);
        Assert.Equal(3, vm.FilteredCount);
        vm.RemoveBanListWordCommand.Execute("QA");
        Assert.Equal(4, vm.FilteredCount);
        vm.NewBanListWord = "QA";
        vm.AddBanListWordCommand.Execute(null);
        vm.ClearBanListWordsCommand.Execute(null);
        Assert.False(vm.HasBanListWords);

        // Зарплатні тогли (EUR -> USD/UAH) + верхня межа + без ЗП
        vm.SelectedSalaryCurrency = "€ EUR";
        vm.MaxSalaryFilter = "2500";
        vm.ToggleIncludeNoSalaryCommand.Execute(null);
        vm.ApplyFiltersCommand.Execute(null);
        Assert.True(vm.FilteredCount >= 1);
        vm.ToggleOnlyWithSalaryCommand.Execute(null);
        vm.ResetFiltersCommand.Execute(null);

        // Повідомлення про збереження/перегляд і перемикачі
        _messenger.Send(new VacancySaveChangedMessage(items[0].Id, true));
        vm.ToggleOnlySavedCommand.Execute(null);
        Assert.Single(vm.Vacancies);
        vm.ToggleOnlySavedCommand.Execute(null);

        _messenger.Send(new VacancyViewedMessage(items[1].Id));
        vm.ToggleHideViewedCommand.Execute(null);
        Assert.DoesNotContain(vm.Vacancies, v => v.Id == items[1].Id);
        vm.ToggleHideViewedCommand.Execute(null);

        // Сортування (усі гілки)
        foreach (string sortOption in vm.SortOptions)
        {
            vm.SelectedSortOption = sortOption;
        }

        _messenger.Send(new UserAddressChangedMessage("Київ, Солом'янка"));
        await Task.Yield();

        // Refresh + Error dismiss
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.ErrorMessage = "Тест";
        vm.DismissErrorCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void VacancyFilterEngine_CoversAllSortingAndCurrencyConversions()
    {
        VacancyDto eur = SampleFourSiteVacancies()[0] with { Currency = "EUR", SalaryMin = 1000m, SalaryMax = 2000m };
        VacancyDto usd = SampleFourSiteVacancies()[1] with { Currency = "USD", SalaryMin = 1000m, SalaryMax = 1500m };
        VacancyDto uah = SampleFourSiteVacancies()[2] with { Currency = "UAH", SalaryMin = 20000m, SalaryMax = 30000m };
        VacancyDto none = SampleFourSiteVacancies()[3] with { SalaryMin = null, SalaryMax = null };

        Assert.True(VacancyFilterEngine.ConvertSalary(eur, "$ USD") > 0m);
        Assert.True(VacancyFilterEngine.ConvertSalary(usd, "€ EUR") > 0m);
        Assert.True(VacancyFilterEngine.ConvertSalary(uah, "₴ ГРН") > 0m);
        Assert.Equal(0m, VacancyFilterEngine.ConvertSalary(none, "$ USD"));
        Assert.False(VacancyFilterEngine.MatchesSalary(none, false, "100", "", "$ USD", includeNoSalary: false));
        Assert.True(VacancyFilterEngine.MatchesSalary(none, false, "100", "", "$ USD", includeNoSalary: true));
        Assert.False(VacancyFilterEngine.MatchesSalary(usd, false, "", "500", "$ USD", includeNoSalary: false));

        List<VacancyDto> all = [eur, usd, uah, none];
        string[] sorts =
        [
            "За датою (спочатку старі)",
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
            "За категорією (А → Я)",
            "За замовчуванням"
        ];

        foreach (string s in sorts)
        {
            List<VacancyDto> sorted = VacancyFilterEngine.SortVacancies(all, s, v => v.Id == eur.Id ? 5.0 : null).ToList();
            Assert.Equal(4, sorted.Count);
        }

        Assert.False(VacancyFilterEngine.TryAddWords("   ", []));
    }

    [Fact]
    public void SyncButtonText_LastSyncedMenuText_SortAndFilterFlags_ReflectState()
    {
        DatabaseViewModel vm = new(_gateway, _messenger, _time);

        Assert.Equal("Синхронізувати", vm.SyncButtonText);
        Assert.Equal("Ще не синхронізовано", vm.LastSyncedMenuText);
        Assert.False(vm.IsCustomSortActive);
        Assert.False(vm.HasActiveFilters);
        Assert.False(vm.IsFilterHighlightActive);

        vm.UnsyncedCount = 3;
        Assert.Equal("Синхронізувати (3)", vm.SyncButtonText);

        DateTimeOffset syncedAt = new(2026, 11, 21, 14, 5, 0, TimeSpan.Zero);
        vm.LastSyncedAt = syncedAt;
        Assert.Equal($"Оновлено о {syncedAt.ToLocalTime():HH:mm}", vm.LastSyncedMenuText);

        vm.SelectedSortOption = vm.SortOptions[1];
        Assert.True(vm.IsCustomSortActive);

        vm.OnlyWithSalary = true;
        Assert.True(vm.HasActiveFilters);
        Assert.True(vm.IsFilterHighlightActive);

        vm.OnlyWithSalary = false;
        vm.ToggleDouFilterCommand.Execute(null);
        Assert.True(vm.HasActiveFilters);

        vm.ResetFilters();
        Assert.False(vm.HasActiveFilters);
        Assert.False(vm.IsCustomSortActive);
    }

    [Fact]
    public async Task SyncDatabase_WhenServerReturns401_DoesNotShowErrorBannerOrSwitchOffline()
    {
        VacancyDto localOffline = SampleFourSiteVacancies()[0] with { Title = "Guest Local Vacancy" };
        await _localStore.SaveVacanciesAsync([localOffline], isSynced: false, CancellationToken.None);

        _gateway.VacancySyncError = new ApiException(
            System.Net.HttpStatusCode.Unauthorized,
            null,
            "Потрібно увійти в обліковий запис.");
        VacancySyncService syncService = new(_gateway, _localStore, _messenger);
        DatabaseViewModel vm = new(_gateway, _messenger, _time, localStore: _localStore, syncService: syncService);

        await vm.SyncDatabaseCommand.ExecuteAsync(null);

        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.HasStatusBanner);
        Assert.Null(vm.StatusBannerText);
        Assert.False(vm.IsOfflineMode);
        Assert.Equal(1, vm.UnsyncedCount);
    }

    [Fact]
    public async Task EnsureLoaded_WhenGuestHasUnsyncedVacancies_SkipsSyncAndLoadsServerCatalog()
    {
        VacancyDto localOffline = SampleFourSiteVacancies()[0] with { Title = "Guest Local Vacancy" };
        await _localStore.SaveVacanciesAsync([localOffline], isSynced: false, CancellationToken.None);

        SetupPagedServer(SampleFourSiteVacancies(), pageSize: 10);
        InMemoryTokenStore guestTokenStore = new();
        VacancySyncService syncService = new(_gateway, _localStore, _messenger, guestTokenStore);
        DatabaseViewModel vm = new(
            _gateway,
            _messenger,
            _time,
            localStore: _localStore,
            syncService: syncService,
            tokenStore: guestTokenStore);

        await vm.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.Empty(_gateway.VacancySyncRequests);
        Assert.False(vm.HasError);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(4, vm.TotalInDatabase);
        Assert.Equal(1, vm.UnsyncedCount);
    }

    private sealed class StubDistanceService : IDistanceCalculatorService
    {
        public string? UserAddress { get; set; }

        public Task<double?> CalculateDistanceKmAsync(string targetAddress, CancellationToken ct = default) =>
            Task.FromResult<double?>(targetAddress.Contains("Київ", StringComparison.OrdinalIgnoreCase) ? 3.5 : 400.0);
    }
}
