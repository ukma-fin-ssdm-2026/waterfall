// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Data.Sqlite;
using Waterfall.Client.Models.SiteSearch;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

public sealed class VacancySyncTests : IDisposable
{
    private readonly string _tempDbPath = Path.Combine(Path.GetTempPath(), $"waterfall-sync-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task BidirectionalSync_ExchangesOfflineClientAndServerVacancies_AndPersistsDeltaTimestamp()
    {
        EfLocalVacancyStore store = new(_tempDbPath);
        FakeGateway gateway = new();
        StrongReferenceMessenger messenger = new();
        VacancySyncService syncService = new(gateway, store, messenger);

        // 1. Клієнт зібрав 2 вакансії офлайн (IsSynced = false)
        VacancyDto client1 = new()
        {
            Id = Guid.NewGuid(),
            ExternalId = "c-1",
            Title = "Client Offline Python",
            Company = "LocalDev",
            Description = "Collected offline",
            Category = "Python",
            Location = "Київ",
            Currency = "USD",
            Source = JobSource.Dou,
            SourceUrl = "https://jobs.dou.ua/vacancies/c1/"
        };
        VacancyDto client2 = new()
        {
            Id = Guid.NewGuid(),
            ExternalId = "c-2",
            Title = "Client Offline QA",
            Company = "LocalQA",
            Description = "Collected offline 2",
            Category = "QA",
            Location = "Львів",
            Currency = "UAH",
            Source = JobSource.WorkUa,
            SourceUrl = "https://www.work.ua/jobs/c2/"
        };
        await store.SaveVacanciesAsync([client1, client2], isSynced: false);
        Assert.Equal(2, (await store.GetUnsyncedAsync()).Count);

        // 2. Сервер зібрав власну вакансію окремо
        DateTimeOffset serverNow = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        Guid serverOnlyId = Guid.NewGuid();
        gateway.VacancySyncHandler = req => new VacancySyncResponseDto
        {
            ReceivedFromClient = req.ClientVacancies.Count,
            CreatedOnServer = req.ClientVacancies.Count,
            UpdatedOnServer = 0,
            TotalServerVacancies = 3,
            ServerTime = serverNow,
            ServerVacancies =
            [
                new VacancySyncItemDto
                {
                    Id = serverOnlyId,
                    ExternalId = "s-1",
                    Title = "Server Collected DevOps",
                    Company = "CloudServer",
                    Description = "Collected on server",
                    Category = "DevOps",
                    Location = "Віддалено",
                    Currency = "USD",
                    Source = JobSource.Djinni,
                    SourceUrl = "https://djinni.co/jobs/s1/",
                    LastSeenAt = serverNow
                }
            ]
        };

        VacancySyncResult? receivedEvent = null;
        messenger.Register<VacanciesSyncedMessage>(this, (_, msg) => receivedEvent = msg.Result);

        // 3. Перша синхронізація: обмін в обидва боки
        VacancySyncResult first = await syncService.SyncWithServerAsync();

        Assert.True(first.Succeeded);
        Assert.Equal(2, first.UploadedToServer);
        Assert.Equal(1, first.DownloadedFromServer);
        Assert.Equal(1, first.AddedLocally);
        Assert.Equal(3, first.TotalLocalVacancies);
        Assert.NotNull(receivedEvent);

        // Усі локальні записи тепер синхронізовано, а LastSyncedAt збережено
        Assert.Empty(await store.GetUnsyncedAsync());
        Assert.Equal(serverNow, await store.GetLastSyncedAtAsync());
        Assert.Equal(3, (await store.GetAllAsync()).Count);

        // 4. Повторна синхронізація передає LastSyncedAt для дельта-режиму і 0 клієнтських записів
        await syncService.SyncWithServerAsync();
        VacancySyncRequestDto secondRequest = gateway.VacancySyncRequests[^1];
        Assert.Empty(secondRequest.ClientVacancies);
        Assert.Equal(serverNow, secondRequest.LastSyncedAt);
        Assert.False(secondRequest.FullSync);
    }

    [Fact]
    public async Task Sync_WhenServerRefusesClientVacancies_LeavesLocalChangesUnsyncedAndReportsRefusal()
    {
        EfLocalVacancyStore store = new(_tempDbPath);
        FakeGateway gateway = new() { VacanciesAcceptedByServer = false };
        VacancySyncService syncService = new(gateway, store);

        await store.SaveVacanciesAsync(
        [
            new VacancyDto { Id = Guid.NewGuid(), Title = "Offline Python", Company = "Local", Description = "d", Category = "Python", Location = "Київ", Currency = "USD", Source = JobSource.Dou, SourceUrl = "https://jobs.dou.ua/vacancies/o1/" },
            new VacancyDto { Id = Guid.NewGuid(), Title = "Offline QA", Company = "Local", Description = "d", Category = "QA", Location = "Львів", Currency = "UAH", Source = JobSource.WorkUa, SourceUrl = "https://www.work.ua/jobs/o2/" }
        ], isSynced: false);

        VacancySyncResult result = await syncService.SyncWithServerAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.ClientVacanciesAccepted);
        Assert.False(syncService.ClientVacanciesAccepted);
        Assert.Equal(0, result.UploadedToServer);

        // Відмова сервера не робить локальні зміни «синхронізованими»: інакше вони б загубилися назавжди
        Assert.Equal(2, (await store.GetUnsyncedAsync()).Count);
        Assert.Equal(2, (await store.GetStatsAsync()).UnsyncedCount);

        // Вивантаження половини не скасовує завантаження: мітка часу оновлена
        Assert.NotNull(await store.GetLastSyncedAtAsync());
    }

    [Fact]
    public async Task Sync_AfterRefusal_StopsUploadingLocalChangesButStillDownloadsServerVacancies()
    {
        EfLocalVacancyStore store = new(_tempDbPath);
        FakeGateway gateway = new() { VacanciesAcceptedByServer = false };
        VacancySyncService syncService = new(gateway, store);
        DateTimeOffset serverNow = new(2026, 12, 1, 10, 30, 0, TimeSpan.Zero);

        await store.SaveVacanciesAsync(
        [
            new VacancyDto { Id = Guid.NewGuid(), Title = "Offline DevOps", Company = "Local", Description = "d", Category = "DevOps", Location = "Київ", Currency = "USD", Source = JobSource.Djinni, SourceUrl = "https://djinni.co/jobs/o3/" }
        ], isSynced: false);

        // Перша синхронізація: дізнаємось про відмову сервера
        await syncService.SyncWithServerAsync();
        Assert.False(syncService.ClientVacanciesAccepted);
        Assert.Single(gateway.VacancySyncRequests[^1].ClientVacancies);

        // Друга синхронізація: тіло запиту вже без клієнтських вакансій, але серверні все одно забираємо
        gateway.VacancySyncHandler = req => new VacancySyncResponseDto
        {
            ClientVacanciesAccepted = false,
            ServerTime = serverNow,
            TotalServerVacancies = 1,
            ServerVacancies =
            [
                new VacancySyncItemDto
                {
                    Id = Guid.NewGuid(),
                    ExternalId = "s-refused",
                    Title = "Server Collected Analyst",
                    Company = "CloudServer",
                    Description = "Collected on server",
                    Category = "Analytics",
                    Location = "Віддалено",
                    Currency = "USD",
                    Source = JobSource.Djinni,
                    SourceUrl = "https://djinni.co/jobs/s-refused/",
                    LastSeenAt = serverNow
                }
            ]
        };

        VacancySyncResult second = await syncService.SyncWithServerAsync();

        Assert.Empty(gateway.VacancySyncRequests[^1].ClientVacancies);
        Assert.Equal(1, second.DownloadedFromServer);
        Assert.Equal(1, second.AddedLocally);
        Assert.Equal(2, (await store.GetAllAsync()).Count);
        Assert.Single(await store.GetUnsyncedAsync());
    }

    [Fact]
    public async Task Sync_WhenServerOmitsAcceptanceField_KeepsPreviousBehaviourAndMarksLocalChangesSynced()
    {
        EfLocalVacancyStore store = new(_tempDbPath);
        FakeGateway gateway = new()
        {
            // Старіший сервер без поля clientVacanciesAccepted
            VacancySyncHandler = req => new VacancySyncResponseDto
            {
                ReceivedFromClient = req.ClientVacancies.Count,
                ServerTime = DateTimeOffset.UtcNow
            }
        };
        VacancySyncService syncService = new(gateway, store);

        await store.SaveVacanciesAsync(
        [
            new VacancyDto { Id = Guid.NewGuid(), Title = "Offline Legacy", Company = "Local", Description = "d", Category = "QA", Location = "Київ", Currency = "UAH", Source = JobSource.Dou, SourceUrl = "https://jobs.dou.ua/vacancies/legacy/" }
        ], isSynced: false);

        VacancySyncResult result = await syncService.SyncWithServerAsync();

        Assert.True(result.ClientVacanciesAccepted);
        Assert.Empty(await store.GetUnsyncedAsync());
    }

    [Fact]
    public async Task LocalStore_UpgradesLegacySqliteSchemaWithoutSyncColumns()
    {
        // Створюємо стару таблицю Vacancies без IsSynced та LocalUpdatedAt
        await using (SqliteConnection conn = new($"Data Source={_tempDbPath}"))
        {
            await conn.OpenAsync();
            await using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Vacancies (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Company TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    CategoryGroup TEXT NULL,
                    Location TEXT NOT NULL,
                    FullAddress TEXT NULL,
                    Schedule TEXT NULL,
                    Experience TEXT NULL,
                    ExperienceMonths INTEGER NULL,
                    WorkFormat INTEGER NOT NULL DEFAULT 0,
                    SalaryMin TEXT NULL,
                    SalaryMax TEXT NULL,
                    Currency TEXT NOT NULL,
                    Source TEXT NOT NULL,
                    SourceUrl TEXT NOT NULL,
                    SkillsJson TEXT NOT NULL,
                    SearchText TEXT NOT NULL,
                    PublishedAt INTEGER NOT NULL,
                    FirstSeenAt INTEGER NOT NULL,
                    LastSeenAt INTEGER NOT NULL,
                    ExternalId TEXT NULL,
                    IsActive INTEGER NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        EfLocalVacancyStore store = new(_tempDbPath);
        await store.SaveVacanciesAsync(
        [
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                Title = "Upgraded Job",
                Company = "TestCo",
                Description = "Desc",
                Category = ".NET",
                Location = "Київ",
                Source = JobSource.Dou,
                SourceUrl = "https://jobs.dou.ua/1"
            }
        ], isSynced: false);

        LocalVacancyStats stats = await store.GetStatsAsync();
        Assert.Equal((1, 1), (stats.TotalCount, stats.UnsyncedCount));
    }

    [Fact]
    public async Task OfflineSiteCollector_ParsesDouRssAndDjinniJsonLd()
    {
        string douXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
                <item>
                    <title>Senior Python Developer в TechCorp, Київ</title>
                    <link>https://jobs.dou.ua/companies/techcorp/vacancies/123456/</link>
                    <description>&lt;p&gt;Досвід 3 роки, Django&lt;/p&gt;</description>
                    <pubDate>Thu, 19 Nov 2026 10:00:00 GMT</pubDate>
                </item>
            </channel></rss>
            """;

        string djinniHtml = """
            <html><head><script type="application/ld+json">
            {"@type":"JobPosting","title":"QA Automation","url":"https://djinni.co/jobs/999-qa/","identifier":{"value":"999"},
             "hiringOrganization":{"name":"DjinniCo"},"description":"Playwright &amp; C#","occupationalCategory":"QA Automation",
             "jobLocationType":"TELECOMMUTE","datePosted":"2026-11-19"}
            </script></head></html>
            """;

        using HttpClient http = new(new StubHttpHandler(url =>
            url.Contains("jobs.dou.ua") ? douXml : djinniHtml));

        OfflineSiteCollector collector = new(http);
        Assert.True(collector.Supports(JobSource.Dou));
        Assert.True(collector.Supports(JobSource.Djinni));
        Assert.False(collector.Supports(JobSource.WorkUa));

        IReadOnlyList<VacancyDto> found = await collector.CollectAsync(
        [
            new SiteSearchRequest { Source = JobSource.Dou, Categories = ["Python"] },
            new SiteSearchRequest { Source = JobSource.Djinni, Categories = ["QA Automation"], Pages = 1 }
        ]);

        Assert.Equal(2, found.Count);
        VacancyDto dou = found.Single(v => v.Source == JobSource.Dou);
        Assert.Equal(("Senior Python Developer", "TechCorp", "Київ", "123456", "Python"), (dou.Title, dou.Company, dou.Location, dou.ExternalId, dou.Category));

        VacancyDto djinni = found.Single(v => v.Source == JobSource.Djinni);
        Assert.Equal(("QA Automation", "DjinniCo", "Віддалено", "999", "QA"), (djinni.Title, djinni.Company, djinni.Location, djinni.ExternalId, djinni.CategoryGroup));
    }

    [Fact]
    public async Task SystemSettingsViewModel_SyncDatabasesNow_UpdatesStatsAndStatus()
    {
        EfLocalVacancyStore store = new(_tempDbPath);
        await store.SaveVacanciesAsync([new VacancyDto { Id = Guid.NewGuid(), Title = "Local", Source = JobSource.Dou }], isSynced: false);

        FakeGateway gateway = new()
        {
            VacancySyncHandler = req => new VacancySyncResponseDto
            {
                ReceivedFromClient = req.ClientVacancies.Count,
                CreatedOnServer = req.ClientVacancies.Count,
                TotalServerVacancies = 1,
                ServerTime = DateTimeOffset.UtcNow
            }
        };
        VacancySyncService syncService = new(gateway, store);
        SystemSettingsViewModel vm = new(gateway: null, tokens: null, localVacancyStore: store, vacancySyncService: syncService);

        await vm.RefreshSyncStatsAsync();
        Assert.Equal((1, 1), (vm.LocalVacanciesCount, vm.UnsyncedVacanciesCount));

        await vm.SyncDatabasesNowCommand.ExecuteAsync(null);

        Assert.True(vm.IsSuccessStatus);
        Assert.Equal(0, vm.UnsyncedVacanciesCount);
        Assert.Contains("Синхронізовано", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_WithMoreThan500UnsyncedVacancies_BatchesInto500ItemRequestsAndMarksAllSynced()
    {
        EfLocalVacancyStore store = new(_tempDbPath);
        FakeGateway gateway = new();
        VacancySyncService syncService = new(gateway, store);
        List<VacancyDto> unsynced = Enumerable.Range(1, 501)
            .Select(i => new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = $"offline-{i}",
                Title = $"Offline Job {i}",
                Company = "LocalCo",
                Description = "Offline batch test",
                Category = ".NET",
                Location = "Київ",
                Currency = "USD",
                Source = JobSource.Dou,
                SourceUrl = $"https://jobs.dou.ua/vacancies/offline-{i}/"
            })
            .ToList();
        await store.SaveVacanciesAsync(unsynced, isSynced: false, CancellationToken.None);

        VacancySyncResult result = await syncService.SyncWithServerAsync(fullSync: false, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(501, result.UploadedToServer);
        Assert.Equal(501, result.TotalLocalVacancies);
        Assert.Equal(2, gateway.VacancySyncRequests.Count);
        Assert.Equal(500, gateway.VacancySyncRequests[0].ClientVacancies.Count);
        Assert.Single(gateway.VacancySyncRequests[1].ClientVacancies);
        Assert.Empty(await store.GetUnsyncedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Sync_WhenServerReturns401_ReturnsUnauthorizedGracefullyWithoutMarkingSynced()
    {
        EfLocalVacancyStore store = new(_tempDbPath);
        await store.SaveVacanciesAsync(
        [
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = "guest-1",
                Title = "Guest Offline Job",
                Company = "LocalCo",
                Source = JobSource.Dou,
                SourceUrl = "https://jobs.dou.ua/vacancies/guest-1/"
            }
        ], isSynced: false, CancellationToken.None);

        FakeGateway gateway = new()
        {
            VacancySyncError = new ApiException(
                HttpStatusCode.Unauthorized,
                null,
                "Потрібно увійти в обліковий запис.")
        };
        VacancySyncService syncService = new(gateway, store);

        VacancySyncResult result = await syncService.SyncWithServerAsync(fullSync: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.IsUnauthorized);
        Assert.Equal(1, result.TotalLocalVacancies);
        Assert.Null(syncService.ClientVacanciesAccepted);
        Assert.Single(await store.GetUnsyncedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Sync_WhenTokenStoreIsEmpty_SkipsServerCallAndReturnsUnauthorized()
    {
        EfLocalVacancyStore store = new(_tempDbPath);
        await store.SaveVacanciesAsync(
        [
            new VacancyDto
            {
                Id = Guid.NewGuid(),
                ExternalId = "guest-2",
                Title = "Guest Local Job",
                Company = "LocalCo",
                Source = JobSource.Djinni,
                SourceUrl = "https://djinni.co/jobs/guest-2/"
            }
        ], isSynced: false, CancellationToken.None);

        FakeGateway gateway = new();
        InMemoryTokenStore tokenStore = new();
        VacancySyncService syncService = new(gateway, store, tokenStore: tokenStore);

        VacancySyncResult result = await syncService.SyncWithServerAsync(fullSync: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.IsUnauthorized);
        Assert.Empty(gateway.VacancySyncRequests);
        Assert.Single(await store.GetUnsyncedAsync(CancellationToken.None));
    }

    private sealed class StubHttpHandler(Func<string, string> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responder(request.RequestUri!.AbsoluteUri))
            });
    }
}
