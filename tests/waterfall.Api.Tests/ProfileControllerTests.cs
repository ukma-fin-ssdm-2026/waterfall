// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Controllers;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Profile;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Tests;

public class ProfileControllerTests
{
    private readonly FakeProfileService _service = new();
    private readonly FakeUserStatsService _statsService = new();
    private readonly FakeVacancyService _vacancyService = new();
    private readonly ProfileController _controller;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _vacancyId = Guid.NewGuid();

    public ProfileControllerTests()
    {
        _controller = new ProfileController(
            _service,
            _statsService,
            _vacancyService,
            NullLogger<ProfileController>.Instance);
    }

    private void Authenticate()
    {
        ClaimsPrincipal user = new(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, _userId.ToString()),
            new Claim(ClaimTypes.Role, "JobSeeker")
        ], "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetAddress_WhenUnauthorized_Returns401()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        ActionResult<UserAddressDto> result = await _controller.GetAddress(CancellationToken.None);
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAddress_WhenAuthenticated_ReturnsStoredAddress()
    {
        Authenticate();
        _service.StoredAddress = "вул. Хрещатик, 22, Київ";

        ActionResult<UserAddressDto> result = await _controller.GetAddress(CancellationToken.None);
        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        UserAddressDto dto = Assert.IsType<UserAddressDto>(ok.Value);
        Assert.Equal("вул. Хрещатик, 22, Київ", dto.Address);
    }

    [Fact]
    public async Task SaveAddress_WhenAuthenticated_UpdatesAddressInDatabase()
    {
        Authenticate();
        UserAddressDto request = new() { Address = "Київ, вул. Польова, 15" };

        ActionResult<UserAddressDto> result = await _controller.SaveAddress(request, CancellationToken.None);
        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        UserAddressDto dto = Assert.IsType<UserAddressDto>(ok.Value);

        Assert.Equal("Київ, вул. Польова, 15", dto.Address);
        Assert.Equal("Київ, вул. Польова, 15", _service.StoredAddress);
        Assert.Equal(_userId, _service.LastUserId);
    }

    [Fact]
    public async Task GetStats_WhenAuthenticated_ReturnsAggregatedStatistics()
    {
        // Arrange
        Authenticate();
        _statsService.StatsToReturn = new UserStatsDto
        {
            SavedVacanciesCount = 3,
            TotalSearches = 5,
            TopTechnologies = [new StatCountDto { Name = "Python", Count = 4 }],
            RecentSearches = ["Python розробник"]
        };

        // Act
        ActionResult<UserStatsDto> result = await _controller.GetStats(CancellationToken.None);

        // Assert
        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        UserStatsDto dto = Assert.IsType<UserStatsDto>(ok.Value);
        Assert.Equal(3, dto.SavedVacanciesCount);
        Assert.Equal(5, dto.TotalSearches);
        Assert.Equal("Python", Assert.Single(dto.TopTechnologies).Name);
        Assert.Equal([_userId], _statsService.GetStatsCalls);
    }

    [Fact]
    public async Task GetStats_WhenUnauthorized_Returns401()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        ActionResult<UserStatsDto> result = await _controller.GetStats(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Empty(_statsService.GetStatsCalls);
    }

    [Fact]
    public async Task RecordSearch_WhenRecorded_Returns202()
    {
        Authenticate();

        IActionResult result = await _controller.RecordSearch(
            new RecordSearchRequest { Query = "Python розробник" }, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal([(_userId, "Python розробник")], _statsService.RecordSearchCalls);
    }

    [Fact]
    public async Task RecordSearch_WhenSkipped_Returns204()
    {
        Authenticate();
        _statsService.SearchResult = UserStatsRecordResult.Skipped;

        IActionResult result = await _controller.RecordSearch(
            new RecordSearchRequest { Query = "Python розробник" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Single(_statsService.RecordSearchCalls);
    }

    [Fact]
    public async Task RecordSearch_WhenNonJobSeeker_Returns403()
    {
        Authenticate();
        _statsService.SearchResult = UserStatsRecordResult.Forbidden;

        IActionResult result = await _controller.RecordSearch(
            new RecordSearchRequest { Query = "Python розробник" }, CancellationToken.None);

        ObjectResult forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task RecordSearch_WhenUnauthorized_Returns401()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        IActionResult result = await _controller.RecordSearch(
            new RecordSearchRequest { Query = "Python розробник" }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(_statsService.RecordSearchCalls);
    }

    [Fact]
    public async Task RecordSearch_WhenModelInvalid_Returns400()
    {
        Authenticate();
        _controller.ModelState.AddModelError("Query", "Поле query є обов'язковим");

        IActionResult result = await _controller.RecordSearch(
            new RecordSearchRequest { Query = "Python розробник" }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_statsService.RecordSearchCalls);
    }

    [Fact]
    public async Task ClearSearchHistory_WhenAuthenticated_Returns204()
    {
        Authenticate();

        IActionResult result = await _controller.ClearSearchHistory(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal([_userId], _statsService.ClearSearchHistoryCalls);
    }

    [Fact]
    public async Task ClearSearchHistory_WhenUnauthorized_Returns401()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        IActionResult result = await _controller.ClearSearchHistory(CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(_statsService.ClearSearchHistoryCalls);
    }

    [Fact]
    public async Task RecordVacancyView_WhenRecorded_Returns202()
    {
        Authenticate();
        _vacancyService.Vacancies.Add(new VacancyDto { Id = _vacancyId, Title = "Senior .NET розробник" });

        IActionResult result = await _controller.RecordVacancyView(
            new RecordVacancyViewRequest { VacancyId = _vacancyId }, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal([(_userId, _vacancyId)], _statsService.RecordViewCalls);
    }

    [Fact]
    public async Task RecordVacancyView_WhenVacancyUnknown_Returns404()
    {
        Authenticate();

        IActionResult result = await _controller.RecordVacancyView(
            new RecordVacancyViewRequest { VacancyId = _vacancyId }, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(_statsService.RecordViewCalls);
    }

    [Fact]
    public async Task RecordVacancyView_WhenNonJobSeeker_Returns403()
    {
        Authenticate();
        _vacancyService.Vacancies.Add(new VacancyDto { Id = _vacancyId, Title = "Senior .NET розробник" });
        _statsService.ViewResult = UserStatsRecordResult.Forbidden;

        IActionResult result = await _controller.RecordVacancyView(
            new RecordVacancyViewRequest { VacancyId = _vacancyId }, CancellationToken.None);

        ObjectResult forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task RecordVacancyView_WhenUnauthorized_Returns401()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        IActionResult result = await _controller.RecordVacancyView(
            new RecordVacancyViewRequest { VacancyId = _vacancyId }, CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(_statsService.RecordViewCalls);
    }

    [Fact]
    public async Task RecordVacancyView_WhenModelInvalid_Returns400()
    {
        Authenticate();
        _vacancyService.Vacancies.Add(new VacancyDto { Id = _vacancyId, Title = "Senior .NET розробник" });
        _controller.ModelState.AddModelError("VacancyId", "Поле vacancyId є обов'язковим");

        IActionResult result = await _controller.RecordVacancyView(
            new RecordVacancyViewRequest { VacancyId = _vacancyId }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_statsService.RecordViewCalls);
    }

    private sealed class FakeProfileService : IUserProfileService
    {
        public string? StoredAddress { get; set; }
        public Guid? LastUserId { get; private set; }

        public Task<string?> GetUserAddressAsync(Guid userId, CancellationToken ct = default)
        {
            LastUserId = userId;
            return Task.FromResult(StoredAddress);
        }

        public Task SaveUserAddressAsync(Guid userId, string address, CancellationToken ct = default)
        {
            LastUserId = userId;
            StoredAddress = address;
            return Task.CompletedTask;
        }
    }
}
