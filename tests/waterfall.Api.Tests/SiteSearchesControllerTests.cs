// AI: тести ендпоінтів пошуку на сайтах (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Waterfall.Api.Controllers;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Workers;
using Waterfall.Services.Models.SiteSearch;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Tests;

public class SiteSearchesControllerTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly FakeSiteSearch _search = new();
    private readonly SiteSearchesController _controller;

    public SiteSearchesControllerTests()
    {
        _controller = new SiteSearchesController(_search);
        ClaimsPrincipal user = new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _userId.ToString())], "Test"));
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } };
    }

    [Fact]
    public void Start_ReturnsAcceptedWithStatusAndLocation()
    {
        SiteSearchStatusDto status = new() { Id = Guid.NewGuid(), State = SiteSearchStates.Queued, PagesTotal = 2 };
        _search.NextStart = new SiteSearchStart(status, null);

        ActionResult<SiteSearchStatusDto> result = _controller.Start(new SiteSearchRequest { Source = JobSource.Djinni, Pages = 2 });

        AcceptedAtActionResult accepted = Assert.IsType<AcceptedAtActionResult>(result.Result);
        Assert.Same(status, accepted.Value);
        Assert.Equal(nameof(SiteSearchesController.Get), accepted.ActionName);
        Assert.Equal(_userId, _search.StartedBy);
    }

    [Fact]
    public void Start_WhenServiceRejects_Returns429WithReason()
    {
        _search.NextStart = new SiteSearchStart(null, "Попередній пошук ще триває");

        ActionResult<SiteSearchStatusDto> result = _controller.Start(new SiteSearchRequest { Source = JobSource.Dou });

        ObjectResult rejected = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.StatusCode);
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(rejected.Value);
        Assert.Equal(SiteSearchesController.LimitErrorCode, error.Error.Code);
        Assert.Contains("ще триває", error.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_WithoutUser_Returns401()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        ActionResult<SiteSearchStatusDto> result = _controller.Start(new SiteSearchRequest { Source = JobSource.Djinni });

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Null(_search.StartedBy);
    }

    [Fact]
    public async Task Get_ReturnsStatus_Or404()
    {
        Guid known = Guid.NewGuid();
        _search.Statuses[known] = new SiteSearchStatusDto { Id = known, State = SiteSearchStates.Completed, Vacancies = [] };

        ActionResult<SiteSearchStatusDto> found = await _controller.Get(known);
        ActionResult<SiteSearchStatusDto> missing = await _controller.Get(Guid.NewGuid());

        Assert.Equal(SiteSearchStates.Completed, Assert.IsType<SiteSearchStatusDto>(Assert.IsType<OkObjectResult>(found.Result).Value).State);
        Assert.IsType<NotFoundObjectResult>(missing.Result);
    }

    private sealed class FakeSiteSearch : ISiteSearchService
    {
        public SiteSearchStart NextStart { get; set; } = new(null, "не налаштовано");

        public Guid? StartedBy { get; private set; }

        public Dictionary<Guid, SiteSearchStatusDto> Statuses { get; } = [];

        public SiteSearchStart Start(SiteSearchRequest request, Guid userId)
        {
            StartedBy = userId;
            return NextStart;
        }

        public Task<SiteSearchStatusDto?> GetAsync(Guid id, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Statuses.GetValueOrDefault(id));
    }
}
