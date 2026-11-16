// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using CommunityToolkit.Mvvm.Messaging;
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
    public async Task LoadRecommendations_LoadsUnsavedVacancies_AndFormatsBasisText()
    {
        FakeGateway gateway = new();
        WeakReferenceMessenger messenger = new();
        RecommendationsViewModel vm = new(gateway, messenger);

        Task loadTask = vm.LoadRecommendationsCommand.ExecuteAsync(null);

        FakeGateway.RecommendationCall call = await gateway.WaitForRecommendationCallAsync(1);
        Guid id1 = Guid.NewGuid();
        Guid id2 = Guid.NewGuid();
        call.Response.SetResult(new RecommendationsResultDto
        {
            Items = [CreateVacancy(id1, "Python Dev", isSaved: false), CreateVacancy(id2, "Saved Python", isSaved: true)],
            SavedCount = 3,
            BasisKeywords = ["Python", "Django"],
            BasisSearches = ["FastAPI"]
        });

        await loadTask;

        Assert.True(vm.HasLoaded);
        Assert.False(vm.IsLoading);
        Assert.Single(vm.Recommendations);
        Assert.Equal(id1, vm.Recommendations[0].Id);
        Assert.False(vm.IsEmpty);
        Assert.Contains("Python, Django", vm.RecommendedBasisText);
        Assert.Contains("FastAPI", vm.RecommendedBasisText);
    }

    [Fact]
    public async Task LoadRecommendations_WhenNoSavedVacancies_ShowsInitialPrompt()
    {
        FakeGateway gateway = new();
        WeakReferenceMessenger messenger = new();
        RecommendationsViewModel vm = new(gateway, messenger);

        Task loadTask = vm.LoadRecommendationsCommand.ExecuteAsync(null);

        FakeGateway.RecommendationCall call = await gateway.WaitForRecommendationCallAsync(1);
        call.Response.SetResult(new RecommendationsResultDto
        {
            Items = [],
            SavedCount = 0,
            BasisKeywords = [],
            BasisSearches = []
        });

        await loadTask;

        Assert.True(vm.HasLoaded);
        Assert.True(vm.IsEmpty);
        Assert.Equal(0, vm.SavedCount);
        Assert.Equal("Ще немає збережених вакансій", vm.EmptyTitle);
        Assert.Contains("зберігати", vm.EmptyDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadRecommendations_HandlesApiException()
    {
        FakeGateway gateway = new();
        WeakReferenceMessenger messenger = new();
        RecommendationsViewModel vm = new(gateway, messenger);

        Task loadTask = vm.LoadRecommendationsCommand.ExecuteAsync(null);

        FakeGateway.RecommendationCall call = await gateway.WaitForRecommendationCallAsync(1);
        call.Response.SetException(new ApiException(System.Net.HttpStatusCode.InternalServerError, null, "Server error"));

        await loadTask;

        Assert.True(vm.HasError);
        Assert.Equal("Server error", vm.ErrorMessage);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task VacancySaveChangedMessage_RemovesSavedItemAndDynamicallyReloads()
    {
        FakeGateway gateway = new();
        StrongReferenceMessenger messenger = new();
        RecommendationsViewModel vm = new(gateway, messenger);

        Task loadTask = vm.LoadRecommendationsCommand.ExecuteAsync(null);
        FakeGateway.RecommendationCall call1 = await gateway.WaitForRecommendationCallAsync(1);
        Guid savedId = Guid.NewGuid();
        Guid javaId = Guid.NewGuid();
        call1.Response.SetResult(new RecommendationsResultDto
        {
            Items = [CreateVacancy(savedId, "Python Vacancy", isSaved: false)],
            SavedCount = 1,
            BasisKeywords = ["Python"]
        });
        await loadTask;

        Assert.Single(vm.Recommendations);

        // Користувач зберігає вакансію -> вона одразу зникає зі списку і підтягуються оновлені рекомендації
        messenger.Send(new VacancySaveChangedMessage(savedId, true));

        Assert.Empty(vm.Recommendations);

        FakeGateway.RecommendationCall call2 = await gateway.WaitForRecommendationCallAsync(2);
        call2.Response.SetResult(new RecommendationsResultDto
        {
            Items = [CreateVacancy(javaId, "Java Vacancy", isSaved: false)],
            SavedCount = 2,
            BasisKeywords = ["Python", "Java"]
        });

        await Task.Delay(50);

        Assert.Single(vm.Recommendations);
        Assert.Equal(javaId, vm.Recommendations[0].Id);
        Assert.Contains("Java", vm.RecommendedBasisText);
    }

    [Fact]
    public async Task Refresh_ReloadsRecommendations()
    {
        FakeGateway gateway = new();
        WeakReferenceMessenger messenger = new();
        RecommendationsViewModel vm = new(gateway, messenger);

        Task refreshTask = vm.RefreshCommand.ExecuteAsync(null);
        FakeGateway.RecommendationCall call = await gateway.WaitForRecommendationCallAsync(1);
        call.Response.SetResult(new RecommendationsResultDto
        {
            Items = [],
            SavedCount = 2
        });
        await refreshTask;

        Assert.True(vm.HasLoaded);
        Assert.True(vm.IsEmpty);
        Assert.Equal("Нових рекомендацій поки немає", vm.EmptyTitle);
    }
}
