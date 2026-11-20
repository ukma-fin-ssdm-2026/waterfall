// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Waterfall.Api.Controllers;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Analytics;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Api.Tests;

public sealed class AnalyticsControllerTests
{
    [Fact]
    public async Task Get_AnalystRole_ReturnsOkWithAnalytics()
    {
        FakeAnalyticsService service = new()
        {
            Result = new MarketAnalyticsDto { TotalVacancies = 12, HasEnoughData = true }
        };
        AnalyticsController controller = new(service);
        SetupUser(controller, Guid.NewGuid(), UserRole.Analyst);

        ActionResult<MarketAnalyticsDto> result = await controller.Get(category: "Python", months: 6, CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        MarketAnalyticsDto payload = Assert.IsType<MarketAnalyticsDto>(ok.Value);
        Assert.Equal(12, payload.TotalVacancies);
        Assert.Equal(("Python", 6), service.LastCall);
    }

    [Fact]
    public async Task Get_JobSeekerRole_ReturnsForbidden()
    {
        FakeAnalyticsService service = new();
        AnalyticsController controller = new(service);
        SetupUser(controller, Guid.NewGuid(), UserRole.JobSeeker);

        ActionResult<MarketAnalyticsDto> result = await controller.Get();

        ObjectResult forbidden = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Get_Unauthenticated_ReturnsUnauthorized()
    {
        FakeAnalyticsService service = new();
        AnalyticsController controller = new(service);
        ControllerTestContext.Setup(controller, currentUserId: null);

        ActionResult<MarketAnalyticsDto> result = await controller.Get();

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    private static void SetupUser(ControllerBase controller, Guid userId, UserRole role)
    {
        ControllerTestContext.Setup(controller, currentUserId: userId);
        ClaimsIdentity identity = new(
            [
                new Claim("sub", userId.ToString()),
                new Claim(ClaimTypes.Role, role.ToString())
            ],
            "TestAuth");
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);
    }

    private sealed class FakeAnalyticsService : IAnalyticsService
    {
        public MarketAnalyticsDto Result { get; set; } = new();
        public (string? Category, int Months)? LastCall { get; private set; }

        public Task<MarketAnalyticsDto> GetMarketAnalyticsAsync(string? category = null, int months = 3, CancellationToken ct = default)
        {
            LastCall = (category, months);
            return Task.FromResult(Result);
        }
    }
}
