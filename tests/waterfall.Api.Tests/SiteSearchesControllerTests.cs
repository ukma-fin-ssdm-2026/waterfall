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

    [Fact]
    public void StartBatch_ReturnsAcceptedWithStatusAndLocation()
    {
        MultiSiteSearchStatusDto status = new()
        {
            Id = Guid.NewGuid(),
            State = SiteSearchStates.Running,
            PagesTotal = 3,
            Sites =
            [
                new SiteSearchItemStatusDto { Source = JobSource.Dou, PagesTotal = 1 },
                new SiteSearchItemStatusDto { Source = JobSource.Djinni, PagesTotal = 2 }
            ]
        };
        _search.NextBatchStart = new MultiSiteSearchStart(status, null);

        MultiSiteSearchRequest request = new()
        {
            Requests =
            [
                new SiteSearchRequest { Source = JobSource.Dou },
                new SiteSearchRequest { Source = JobSource.Djinni, Pages = 2 }
            ]
        };

        ActionResult<MultiSiteSearchStatusDto> result = _controller.StartBatch(request);

        AcceptedAtActionResult accepted = Assert.IsType<AcceptedAtActionResult>(result.Result);
        Assert.Same(status, accepted.Value);
        Assert.Equal(nameof(SiteSearchesController.GetBatch), accepted.ActionName);
        Assert.Equal(_userId, _search.StartedBy);
    }

    [Fact]
    public void StartBatch_WhenRejected_Returns429WithLimitCode()
    {
        _search.NextBatchStart = new MultiSiteSearchStart(null, "Попередній пошук ще триває");

        ActionResult<MultiSiteSearchStatusDto> result = _controller.StartBatch(new MultiSiteSearchRequest
        {
            Requests = [new SiteSearchRequest { Source = JobSource.Dou }]
        });

        ObjectResult rejected = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.StatusCode);
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(rejected.Value);
        Assert.Equal(SiteSearchesController.LimitErrorCode, error.Error.Code);
    }

    [Fact]
    public void StartBatch_WithoutUser_Returns401()
    {
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        ActionResult<MultiSiteSearchStatusDto> result = _controller.StartBatch(new MultiSiteSearchRequest
        {
            Requests = [new SiteSearchRequest { Source = JobSource.Djinni }]
        });

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetBatch_ReturnsStatus_Or404_Or401()
    {
        Guid known = Guid.NewGuid();
        _search.BatchStatuses[known] = new MultiSiteSearchStatusDto
        {
            Id = known,
            State = SiteSearchStates.Completed,
            Vacancies = []
        };

        ActionResult<MultiSiteSearchStatusDto> found = await _controller.GetBatch(known);
        ActionResult<MultiSiteSearchStatusDto> missing = await _controller.GetBatch(Guid.NewGuid());

        Assert.Equal(SiteSearchStates.Completed, Assert.IsType<MultiSiteSearchStatusDto>(Assert.IsType<OkObjectResult>(found.Result).Value).State);
        Assert.IsType<NotFoundObjectResult>(missing.Result);

        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        ActionResult<MultiSiteSearchStatusDto> unauth = await _controller.GetBatch(known);
        Assert.IsType<UnauthorizedObjectResult>(unauth.Result);
    }

    private sealed class FakeSiteSearch : ISiteSearchService
    {
        public SiteSearchStart NextStart { get; set; } = new(null, "не налаштовано");

        public MultiSiteSearchStart NextBatchStart { get; set; } = new(null, "не налаштовано");

        public Guid? StartedBy { get; private set; }

        public Dictionary<Guid, SiteSearchStatusDto> Statuses { get; } = [];

        public Dictionary<Guid, MultiSiteSearchStatusDto> BatchStatuses { get; } = [];

        public SiteSearchStart Start(SiteSearchRequest request, Guid userId)
        {
            StartedBy = userId;
            return NextStart;
        }

        public Task<SiteSearchStatusDto?> GetAsync(Guid id, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Statuses.GetValueOrDefault(id));

        public MultiSiteSearchStart StartBatch(MultiSiteSearchRequest request, Guid userId)
        {
            StartedBy = userId;
            return NextBatchStart;
        }

        public Task<MultiSiteSearchStatusDto?> GetBatchAsync(Guid id, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(BatchStatuses.GetValueOrDefault(id));
    }
}
