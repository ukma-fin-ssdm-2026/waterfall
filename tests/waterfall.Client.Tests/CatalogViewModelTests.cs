// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: тести EnsureLoaded/LoadMore/ShowTotal, скасованого застарілого завантаження і гілок debounce (Lab 06); перевірено: dotnet test, звіт покриття.

using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

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
    public async Task UnsaveFromAnotherTab_UpdatesMatchingCardInPlace()
    {
        Task load = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        var page = FakeGateway.Page(1, 2, hasNext: false, "A", "B");
        page = page with { Items = page.Items.Select(v => v with { IsSaved = true }).ToList() };
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(page);
        await load;

        _messenger.Send(new VacancySaveChangedMessage(_vm.Vacancies[1].Id, IsSaved: false));

        Assert.True(_vm.Vacancies[0].IsSaved);
        Assert.False(_vm.Vacancies[1].IsSaved);
        Assert.Single(_gateway.CatalogCalls);
    }

    [Fact]
    public async Task EnsureLoaded_LoadsFirstPageOnce()
    {
        Task load = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        FakeGateway.CatalogCall call = await _gateway.WaitForCatalogCallAsync(1);
        Assert.True(_vm.IsLoading);
        Assert.False(_vm.IsEmpty);

        call.Response.SetResult(FakeGateway.Page(1, 21, hasNext: true, "A", "B"));
        await load;

        Assert.Equal((null, 1), (call.Search, call.Page));
        Assert.Equal(["A", "B"], _vm.Vacancies.Select(v => v.Title));
        Assert.Equal(21, _vm.TotalCount);
        Assert.False(_vm.IsLoading);

        await _vm.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.Single(_gateway.CatalogCalls);
    }

    [Fact]
    public async Task Typing_SearchesOnlyAfterPause_AndRestartsPauseOnEachKeystroke()
    {
        _vm.SearchText = "q";
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _vm.SearchText = "qa ";
        _time.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Empty(_gateway.CatalogCalls);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        FakeGateway.CatalogCall call = await _gateway.WaitForCatalogCallAsync(1);

        Assert.Equal(("qa", 1), (call.Search, call.Page));
        Assert.Single(_gateway.CatalogCalls);
    }

    [Fact]
    public async Task LoadMore_AppendsNextPageOfSameQuery_UntilNoMorePages()
    {
        _vm.SearchText = "net";
        Task search = _vm.SearchCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 3, hasNext: true, "A", "B"));
        await search;

        Task more = _vm.LoadMoreCommand.ExecuteAsync(null);
        FakeGateway.CatalogCall second = await _gateway.WaitForCatalogCallAsync(2);
        Assert.True(_vm.IsLoadingMore);
        second.Response.SetResult(FakeGateway.Page(2, 3, hasNext: false, "C"));
        await more;

        Assert.Equal(("net", 2), (second.Search, second.Page));
        Assert.Equal(["A", "B", "C"], _vm.Vacancies.Select(v => v.Title));
        Assert.False(_vm.IsLoadingMore);

        await _vm.LoadMoreCommand.ExecuteAsync(null);
        Assert.Equal(2, _gateway.CatalogCalls.Count);
    }

    [Fact]
    public async Task NewSearch_DiscardsInFlightLoadMoreOfPreviousQuery()
    {
        Task first = _vm.SearchCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 40, hasNext: true, "old-1"));
        await first;

        Task more = _vm.LoadMoreCommand.ExecuteAsync(null);
        FakeGateway.CatalogCall staleMore = await _gateway.WaitForCatalogCallAsync(2);

        _vm.SearchText = "qa";
        Task search = _vm.SearchCommand.ExecuteAsync(null);
        FakeGateway.CatalogCall fresh = await _gateway.WaitForCatalogCallAsync(3);

        Assert.True(staleMore.Ct.IsCancellationRequested);
        fresh.Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "qa-1"));
        await Task.WhenAll(more, search);

        Assert.Equal(["qa-1"], _vm.Vacancies.Select(v => v.Title));
        Assert.False(_vm.IsLoadingMore);
        Assert.False(_vm.IsLoading);
    }

    [Fact]
    public async Task Failure_ShowsServerMessage_RetryClearsIt()
    {
        Task load = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetException(
            new ApiException(null, null, "Не вдалося з'єднатися з сервером."));
        await load;

        Assert.True(_vm.HasError);
        Assert.Equal("Не вдалося з'єднатися з сервером.", _vm.ErrorMessage);
        Assert.False(_vm.IsEmpty);
        Assert.False(_vm.IsLoading);

        Task retry = _vm.RetryCommand.ExecuteAsync(null);
        Assert.False(_vm.HasError);
        (await _gateway.WaitForCatalogCallAsync(2)).Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "A"));
        await retry;

        Assert.Single(_vm.Vacancies);
        Assert.False(_vm.HasError);
    }

    [Fact]
    public async Task NoResults_IsEmpty()
    {
        Task load = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 0, hasNext: false));
        await load;

        Assert.True(_vm.IsEmpty);
        Assert.Equal("Знайдено вакансій: 0", _vm.TotalText);
    }

    [Fact]
    public async Task EnsureLoaded_WhenAlreadyLoaded_DoesNotReload()
    {
        Task load = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "A"));
        await load;

        _vm.SearchText = string.Empty;
        await _vm.EnsureLoadedCommand.ExecuteAsync(null);

        Assert.Single(_gateway.CatalogCalls);
    }

    [Fact]
    public async Task EnsureLoaded_WhileLoading_DoesNotStartSecondLoad()
    {
        Task first = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        FakeGateway.CatalogCall call = await _gateway.WaitForCatalogCallAsync(1);

        await _vm.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.Single(_gateway.CatalogCalls);

        call.Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "A"));
        await first;
        Assert.Single(_vm.Vacancies);
    }

    [Fact]
    public async Task Search_SameQueryAfterLoad_IsNoOp()
    {
        Task load = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "A"));
        await load;

        await _vm.SearchCommand.ExecuteAsync(null);

        Assert.Single(_gateway.CatalogCalls);
    }

    [Fact]
    public async Task ShowTotal_HiddenBeforeLoad_AndOnError() 
    {
        Task load = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        Assert.False(_vm.ShowTotal);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "A"));
        await load;
        Assert.True(_vm.ShowTotal);

        Task failing = _vm.RetryCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(2)).Response.SetException(
            new ApiException(HttpStatusCode.ServiceUnavailable, null, "Сервер повернув помилку 503."));
        await failing;
        Assert.True(_vm.HasError);
        Assert.False(_vm.ShowTotal);

        Task recovery = _vm.RetryCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(3)).Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "B"));
        await recovery;
        Assert.True(_vm.ShowTotal);
    }

    [Fact]
    public async Task LoadMore_WhenServerRejects_ShowsError()
    {
        Task first = _vm.SearchCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 3, hasNext: true, "A"));
        await first;

        Task more = _vm.LoadMoreCommand.ExecuteAsync(null);
        await _gateway.WaitForCatalogCallAsync(2);
        _gateway.CatalogCalls[1].Response.SetException(new ApiException(HttpStatusCode.InternalServerError, null, "Сервер повернув помилку 500."));
        await more;

        Assert.Equal("Сервер повернув помилку 500.", _vm.ErrorMessage);
        Assert.False(_vm.IsLoadingMore);
        Assert.True(_vm.HasError);
        Assert.False(_vm.ShowTotal);
    }

    [Fact]
    public async Task Refresh_ResetsRefreshingFlag_AndReplacesList()
    {
        Task load = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "A"));
        await load;

        _vm.IsRefreshing = true;
        Task refresh = _vm.RefreshCommand.ExecuteAsync(null);
        Assert.False(_vm.IsLoading);
        (await _gateway.WaitForCatalogCallAsync(2)).Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "B"));
        await refresh;

        Assert.Equal(["B"], _vm.Vacancies.Select(v => v.Title));
        Assert.False(_vm.IsRefreshing);
    }

    [Fact]
    public async Task CardToggleError_IsShownInPageBanner()
    {
        Task load = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "A"));
        await load;
        _gateway.ToggleError = new ApiException(HttpStatusCode.InternalServerError, null, "Сервер повернув помилку 500.");

        await _vm.Vacancies[0].ToggleSaveCommand.ExecuteAsync(null);

        Assert.Equal("Сервер повернув помилку 500.", _vm.ErrorMessage);
        Assert.False(_vm.Vacancies[0].IsSaved);
    }

    [Fact]
    public async Task SupersededLoad_CancelledSilently_NewResultShown()
    {
        // Перше завантаження висить; повторне (Retry) його витісняє: скасування — не помилка,
        // банер не з'являється, показуються дані новішого запиту, прапорці скинуто.
        Task stale = _vm.EnsureLoadedCommand.ExecuteAsync(null);
        FakeGateway.CatalogCall first = await _gateway.WaitForCatalogCallAsync(1);
        Assert.True(_vm.IsLoading);

        Task fresh = _vm.RetryCommand.ExecuteAsync(null);
        FakeGateway.CatalogCall second = await _gateway.WaitForCatalogCallAsync(2);
        Assert.True(first.Ct.IsCancellationRequested);
        Assert.Null(_vm.ErrorMessage);

        second.Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "B"));
        await Task.WhenAll(stale, fresh);

        Assert.Equal(["B"], _vm.Vacancies.Select(v => v.Title));
        Assert.False(_vm.HasError);
        Assert.False(_vm.IsLoading);
        Assert.False(_vm.IsLoadingMore);
        Assert.True(_vm.ShowTotal);
    }

    [Fact]
    public async Task Debounce_BeforeFirstLoad_SameQueryStillLoads()
    {
        // Користувач друкує до першого завантаження, потім стирає: нормалізований запит уже
        // «той самий» (порожній), але HasLoaded == false, тож пауза все одно завершується завантаженням.
        _vm.SearchText = "q";
        _vm.SearchText = string.Empty;
        _time.Advance(TimeSpan.FromMilliseconds(400));
        FakeGateway.CatalogCall call = await _gateway.WaitForCatalogCallAsync(1);

        Assert.Equal((null, 1), (call.Search, call.Page));
    }

    [Fact]
    public async Task Debounce_AfterLoad_SameQueryDoesNotReload()
    {
        // Після завантаження користувач додає пробіл: нормалізований запит той самий —
        // пауза минає без повторного запиту (зайве мерехтіння списку й запит).
        // (SearchCommand скасовує debounce від набору, тож перший запит один.)
        _vm.SearchText = "net";
        Task load = _vm.SearchCommand.ExecuteAsync(null);
        (await _gateway.WaitForCatalogCallAsync(1)).Response.SetResult(FakeGateway.Page(1, 1, hasNext: false, "A"));
        await load;
        Assert.Single(_gateway.CatalogCalls);

        _vm.SearchText = "net ";
        _time.Advance(CatalogViewModel.SearchDebounce);
        await Task.Delay(50);

        Assert.Single(_gateway.CatalogCalls);
        Assert.Equal(["A"], _vm.Vacancies.Select(v => v.Title));
    }
}
