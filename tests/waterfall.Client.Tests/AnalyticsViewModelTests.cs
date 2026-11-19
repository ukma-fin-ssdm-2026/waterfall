// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using System.Text;
using System.Text.Json;
using Waterfall.Client.Models.Analytics;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

public class AnalyticsViewModelTests
{
    private static string Jwt(UserRole role)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["email"] = "user@test.com",
            ["role"] = role.ToString(),
            ["exp"] = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds()
        });
        return $"{B64("""{"alg":"HS256","typ":"JWT"}""")}.{B64(payload)}.sig";
    }

    [Fact]
    public async Task CheckAccessAndReload_JobSeeker_BlocksSectionWithoutApiCall()
    {
        InMemoryTokenStore tokens = new();
        await tokens.SetTokenAsync(Jwt(UserRole.JobSeeker));
        FakeGateway gateway = new();
        AnalyticsViewModel vm = new(tokens, gateway);

        await vm.CheckAccessAndReloadCommand.ExecuteAsync(null);

        Assert.False(vm.IsAnalyst);
        Assert.True(vm.IsAccessDenied);
        Assert.Empty(gateway.AnalyticsCalls);
    }

    [Fact]
    public async Task CheckAccessAndReload_NoToken_BlocksSection()
    {
        InMemoryTokenStore tokens = new();
        FakeGateway gateway = new();
        AnalyticsViewModel vm = new(tokens, gateway);

        await vm.CheckAccessAndReloadCommand.ExecuteAsync(null);

        Assert.False(vm.IsAnalyst);
        Assert.True(vm.IsAccessDenied);
    }

    [Fact]
    public async Task CheckAccessAndReload_Analyst_LoadsAllFiveAnalyticsAndRaisesChartsUpdated()
    {
        InMemoryTokenStore tokens = new();
        await tokens.SetTokenAsync(Jwt(UserRole.Analyst));
        FakeGateway gateway = new();
        AnalyticsViewModel vm = new(tokens, gateway);
        int chartsUpdatedCount = 0;
        vm.ChartsUpdated += (_, _) => chartsUpdatedCount++;

        Task loadTask = vm.CheckAccessAndReloadCommand.ExecuteAsync(null);
        FakeGateway.AnalyticsCall call = await gateway.WaitForAnalyticsCallAsync(1);
        call.Response.SetResult(new MarketAnalyticsDto
        {
            TotalVacancies = 10,
            HasEnoughData = true,
            PrimaryCurrency = "USD",
            SalaryTransparencyPercent = 80,
            EntryLevelPercent = 30,
            EntryLevelCount = 3,
            SalariesByGrade = [new GradeSalaryDto("Middle (1–3 р.)", 3000m, 2500m, 3500m, 5, "USD")],
            TopPayingTechnologies = [new TechSalaryDto("Go", 4500m, 4, "USD")],
            WorkFormats = [new CategoryShareDto("Віддалено", 7, 70)],
            TopCities = [new CategoryShareDto("Київ", 6, 60)],
            Sources = [new CategoryShareDto("Djinni", 5, 50)],
            TopCompanies = [new CategoryShareDto("Genesis", 3, 30)],
            AvailableCategories = ["Backend", "QA"]
        });
        await loadTask;

        Assert.True(vm.IsAnalyst);
        Assert.False(vm.IsAccessDenied);
        Assert.True(vm.HasEnoughData);
        Assert.False(vm.IsInsufficientData);
        Assert.Equal(10, vm.TotalVacancies);
        Assert.Equal("80%", vm.SalaryTransparencyText);
        Assert.Equal("30% (3)", vm.EntryLevelText);
        Assert.Single(vm.SalariesByGrade);
        Assert.Equal("$3 000", vm.SalariesByGrade[0].FormattedAverage);
        Assert.Single(vm.TopPayingTechnologies);
        Assert.Single(vm.WorkFormats);
        Assert.Single(vm.TopCities);
        Assert.Single(vm.Sources);
        Assert.Single(vm.TopCompanies);
        Assert.Contains("Backend", vm.Categories);
        Assert.Equal(1, chartsUpdatedCount);
    }

    [Fact]
    public async Task ChangingFilters_ImmediatelyReloadsAnalytics()
    {
        InMemoryTokenStore tokens = new();
        await tokens.SetTokenAsync(Jwt(UserRole.Analyst));
        FakeGateway gateway = new();
        AnalyticsViewModel vm = new(tokens, gateway);

        Task initTask = vm.CheckAccessAndReloadCommand.ExecuteAsync(null);
        FakeGateway.AnalyticsCall call1 = await gateway.WaitForAnalyticsCallAsync(1);
        call1.Response.SetResult(new MarketAnalyticsDto
        {
            TotalVacancies = 10,
            HasEnoughData = true,
            AvailableCategories = ["Backend"]
        });
        await initTask;

        vm.SelectedCategory = "Backend";
        FakeGateway.AnalyticsCall call2 = await gateway.WaitForAnalyticsCallAsync(2);
        Assert.Equal("Backend", call2.Category);
        call2.Response.SetResult(new MarketAnalyticsDto { TotalVacancies = 2, HasEnoughData = false });
        await Task.Delay(50);

        Assert.True(vm.IsInsufficientData);
        Assert.False(vm.HasEnoughData);
    }

    [Fact]
    public async Task ResetAnalyticsFilters_RestoresDefaultsAndReloads()
    {
        InMemoryTokenStore tokens = new();
        await tokens.SetTokenAsync(Jwt(UserRole.Analyst));
        FakeGateway gateway = new();
        AnalyticsViewModel vm = new(tokens, gateway);

        Task initTask = vm.CheckAccessAndReloadCommand.ExecuteAsync(null);
        FakeGateway.AnalyticsCall call1 = await gateway.WaitForAnalyticsCallAsync(1);
        call1.Response.SetResult(new MarketAnalyticsDto { TotalVacancies = 2, HasEnoughData = false, AvailableCategories = ["Backend"] });
        await initTask;

        vm.SelectedPeriod = "1 рік";
        FakeGateway.AnalyticsCall call2 = await gateway.WaitForAnalyticsCallAsync(2);
        Assert.Equal(12, call2.Months);
        call2.Response.SetResult(new MarketAnalyticsDto { TotalVacancies = 2, HasEnoughData = false });
        await Task.Delay(50);

        Task resetTask = vm.ResetAnalyticsFiltersCommand.ExecuteAsync(null);
        FakeGateway.AnalyticsCall call3 = await gateway.WaitForAnalyticsCallAsync(3);
        Assert.Null(call3.Category);
        Assert.Equal(3, call3.Months);
        call3.Response.SetResult(new MarketAnalyticsDto { TotalVacancies = 8, HasEnoughData = true });
        await resetTask;

        Assert.Equal(AnalyticsViewModel.AllCategoriesOption, vm.SelectedCategory);
        Assert.Equal("3 місяці", vm.SelectedPeriod);
        Assert.True(vm.HasEnoughData);
    }

    [Fact]
    public async Task ForbiddenResponse_BlocksAnalyticsSection()
    {
        InMemoryTokenStore tokens = new();
        await tokens.SetTokenAsync(Jwt(UserRole.Analyst));
        FakeGateway gateway = new();
        AnalyticsViewModel vm = new(tokens, gateway);

        Task loadTask = vm.CheckAccessAndReloadCommand.ExecuteAsync(null);
        FakeGateway.AnalyticsCall call = await gateway.WaitForAnalyticsCallAsync(1);
        call.Response.SetException(new ApiException(HttpStatusCode.Forbidden, null, "Forbidden"));
        await loadTask;

        Assert.False(vm.IsAnalyst);
        Assert.True(vm.IsAccessDenied);
    }
}
