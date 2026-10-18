// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: тест скасованого застарілого завантаження (Lab 06); перевірено: dotnet test, звіт покриття.

using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Models.SavedVacancies;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

public class FavoritesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeGateway _gateway = new();
    private readonly StrongReferenceMessenger _messenger = new();
    private readonly FavoritesViewModel _vm;

    public FavoritesViewModelTests()
    {
        _vm = new FavoritesViewModel(_gateway, _messenger);
    }

    private static SavedVacancyDto Saved(string title, int minutesAfterT0, bool nestedIsSaved = false) => new()
    {
        Id = Guid.NewGuid(),
        SavedAt = T0.AddMinutes(minutesAfterT0),
        Vacancy = new VacancyDto { Id = Guid.NewGuid(), Title = title, IsSaved = nestedIsSaved }
    };

    [Fact]
    public async Task Load_ShowsNewestFirst_AllMarkedSaved_SkipsEntriesWithoutVacancy()
    {
        _gateway.Saved = () => [Saved("old", 0), Saved("new", 10), new SavedVacancyDto { Vacancy = null }];

        await _vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["new", "old"], _vm.Vacancies.Select(v => v.Title));
        Assert.All(_vm.Vacancies, v => Assert.True(v.IsSaved));
        Assert.Equal("Збережено вакансій: 2", _vm.TotalText);
        Assert.True(_vm.ShowTotal);
        Assert.False(_vm.IsEmpty);
    }

    [Fact]
    public async Task EveryLoad_RefetchesFromServer()
    {
        _gateway.Saved = () => [Saved("A", 0)];
        await _vm.LoadCommand.ExecuteAsync(null);

        _gateway.Saved = () => [Saved("A", 0), Saved("B", 5)];
        await _vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, _gateway.SavedCalls);
        Assert.Equal(["B", "A"], _vm.Vacancies.Select(v => v.Title));
    }

    [Fact]
    public async Task UnsavingCard_RemovesIt_LastOneLeavesEmptyState()
    {
        _gateway.Saved = () => [Saved("A", 0)];
        await _vm.LoadCommand.ExecuteAsync(null);

        await _vm.Vacancies[0].ToggleSaveCommand.ExecuteAsync(null);

        Assert.Empty(_vm.Vacancies);
        Assert.True(_vm.IsEmpty);
        Assert.False(_vm.ShowTotal);
    }

    [Fact]
    public async Task UnsaveFromCatalog_RemovesMatchingItem_SaveIsIgnoredUntilNextLoad()
    {
        _gateway.Saved = () => [Saved("A", 0), Saved("B", 5)];
        await _vm.LoadCommand.ExecuteAsync(null);
        Guid a = _vm.Vacancies.Single(v => v.Title == "A").Id;

        _messenger.Send(new VacancySaveChangedMessage(a, IsSaved: false));
        _messenger.Send(new VacancySaveChangedMessage(Guid.NewGuid(), IsSaved: true));

        Assert.Equal(["B"], _vm.Vacancies.Select(v => v.Title));
        Assert.Equal("Збережено вакансій: 1", _vm.TotalText);
    }

    [Fact]
    public async Task Failure_KeepsPreviousCards_ShowsBanner_RetryRecovers()
    {
        _gateway.Saved = () => [Saved("A", 0)];
        await _vm.LoadCommand.ExecuteAsync(null);

        _gateway.Saved = () => throw new ApiException(null, null, "Не вдалося з'єднатися з сервером.");
        await _vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Не вдалося з'єднатися з сервером.", _vm.ErrorMessage);
        Assert.Single(_vm.Vacancies);
        Assert.False(_vm.ShowTotal);
        Assert.False(_vm.IsLoading);

        _gateway.Saved = () => [];
        await _vm.LoadCommand.ExecuteAsync(null);

        Assert.False(_vm.HasError);
        Assert.True(_vm.IsEmpty);
    }

    [Fact]
    public async Task Refresh_ResetsRefreshingFlag()
    {
        _vm.IsRefreshing = true;

        await _vm.RefreshCommand.ExecuteAsync(null);

        Assert.False(_vm.IsRefreshing);
        Assert.True(_vm.HasLoaded);
    }

    [Fact]
    public async Task SupersededLoad_CancelledSilently_NewResultShown()
    {
        // Перше завантаження висить; повторне його витісняє: скасування — не помилка,
        // банер не з'являється, показуються дані новішого запиту, прапорець скинуто.
        _gateway.DeferSavedCalls = true;
        Task stale = _vm.LoadCommand.ExecuteAsync(null);
        await _gateway.WaitForSavedCallAsync(1);
        Assert.True(_vm.IsLoading);

        Task fresh = _vm.LoadCommand.ExecuteAsync(null);
        FakeGateway.SavedCall second = await _gateway.WaitForSavedCallAsync(2);
        Assert.True(_gateway.SavedPendingCalls[0].Ct.IsCancellationRequested);
        Assert.Null(_vm.ErrorMessage);

        second.Response.SetResult([Saved("B", 5)]);
        await Task.WhenAll(stale, fresh);

        Assert.Equal(["B"], _vm.Vacancies.Select(v => v.Title));
        Assert.False(_vm.HasError);
        Assert.False(_vm.IsLoading);
        Assert.True(_vm.ShowTotal);
    }
}
