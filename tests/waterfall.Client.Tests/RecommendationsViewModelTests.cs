// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

public class RecommendationsViewModelTests
{
    private static VacancyDto CreateVacancy(Guid id, string title, bool isSaved = false) => new()
    {
        Id = id,
        Title = title,
        Company = "Test Corp",
        Location = "Київ",
        Category = ".NET",
        Schedule = "Full-time",
        Experience = "1 year",
        Source = JobSource.Djinni,
        SourceUrl = "https://djinni.co/jobs/1",
        PublishedAt = DateTimeOffset.UtcNow,
        IsSaved = isSaved
    };

    [Fact]
    public async Task LoadRecommendations_LoadsUnsavedVacancies_AndSetsLoaded()
    {
        FakeGateway gateway = new();
        WeakReferenceMessenger messenger = new();
        RecommendationsViewModel vm = new(gateway, messenger);

        Task loadTask = vm.LoadRecommendationsCommand.ExecuteAsync(null);

        FakeGateway.CatalogCall call = await gateway.WaitForCatalogCallAsync(1);
        Guid id1 = Guid.NewGuid();
        Guid id2 = Guid.NewGuid();
        call.Response.SetResult(new PagedResult<VacancyDto>
        {
            Items = [CreateVacancy(id1, "Vacancy 1", isSaved: false), CreateVacancy(id2, "Vacancy 2", isSaved: true)],
            Page = 1,
            PageSize = 10,
            TotalCount = 2
        });

        await loadTask;

        Assert.True(vm.HasLoaded);
        Assert.False(vm.IsLoading);
        Assert.Single(vm.Recommendations);
        Assert.Equal(id1, vm.Recommendations[0].Id);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task LoadRecommendations_HandlesApiException()
    {
        FakeGateway gateway = new();
        WeakReferenceMessenger messenger = new();
        RecommendationsViewModel vm = new(gateway, messenger);

        Task loadTask = vm.LoadRecommendationsCommand.ExecuteAsync(null);

        FakeGateway.CatalogCall call = await gateway.WaitForCatalogCallAsync(1);
        call.Response.SetException(new ApiException(System.Net.HttpStatusCode.InternalServerError, null, "Server error"));

        await loadTask;

        Assert.True(vm.HasError);
        Assert.Equal("Server error", vm.ErrorMessage);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task VacancySaveChangedMessage_UpdatesItemSavedState()
    {
        FakeGateway gateway = new();
        StrongReferenceMessenger messenger = new();
        RecommendationsViewModel vm = new(gateway, messenger);

        Task loadTask = vm.LoadRecommendationsCommand.ExecuteAsync(null);
        FakeGateway.CatalogCall call = await gateway.WaitForCatalogCallAsync(1);
        Guid id = Guid.NewGuid();
        call.Response.SetResult(new PagedResult<VacancyDto>
        {
            Items = [CreateVacancy(id, "Vacancy 1", isSaved: false)],
            Page = 1,
            PageSize = 10,
            TotalCount = 1
        });
        await loadTask;

        Assert.False(vm.Recommendations[0].IsSaved);

        messenger.Send(new VacancySaveChangedMessage(id, true));

        Assert.True(vm.Recommendations[0].IsSaved);
    }

    [Fact]
    public async Task Refresh_ReloadsRecommendations()
    {
        FakeGateway gateway = new();
        WeakReferenceMessenger messenger = new();
        RecommendationsViewModel vm = new(gateway, messenger);

        Task refreshTask = vm.RefreshCommand.ExecuteAsync(null);
        FakeGateway.CatalogCall call = await gateway.WaitForCatalogCallAsync(1);
        call.Response.SetResult(new PagedResult<VacancyDto>
        {
            Items = [],
            Page = 1,
            PageSize = 10,
            TotalCount = 0
        });
        await refreshTask;

        Assert.True(vm.HasLoaded);
        Assert.True(vm.IsEmpty);
    }
}
