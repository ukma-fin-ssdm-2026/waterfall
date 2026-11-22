// AI: модульні тести контролера вакансій (VacanciesController, HTTP-статуси 200/201/204/400/404, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Microsoft.AspNetCore.Mvc;
using Waterfall.Api.Controllers;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Tests;

/// <summary>
/// Модульні тести для VacanciesController згідно з шаблоном AAA.
/// Перевіряє HTTP-статуси (200, 201, 204, 400, 404), валідацію вхідних даних та структуру ApiErrorResponse.
/// </summary>
public sealed class VacanciesControllerTests
{
    private readonly FakeVacancyService _vacancyService = new();
    private readonly FakeLogger<VacanciesController> _logger = new();
    private readonly VacanciesController _controller;

    public VacanciesControllerTests()
    {
        _controller = new VacanciesController(_vacancyService, _logger);
        ControllerTestContext.Setup(_controller);
    }

    private static VacancyDto CreateSampleDto(string title = ".NET Developer")
    {
        return new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = title,
            Company = "CoolCompany",
            Description = "Great project",
            Category = ".NET",
            Location = "Kyiv",
            Currency = "USD",
            Source = JobSource.Djinni,
            SourceUrl = "https://djinni.co/jobs/1",
            PublishedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithPagedResultAndPassesCurrentUserId()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        ControllerTestContext.Setup(_controller, currentUserId: userId);

        VacancyDto sample = CreateSampleDto();
        _vacancyService.Vacancies.Add(sample);
        VacancyFilterParameters filter = new() { Page = 1, PageSize = 10 };

        // Act
        ActionResult<PagedResult<VacancyDto>> result = await _controller.GetAll(filter, CancellationToken.None);

        // Assert
        OkObjectResult okResult = Assert.IsType<OkObjectResult>(result.Result);
        PagedResult<VacancyDto> paged = Assert.IsType<PagedResult<VacancyDto>>(okResult.Value);
        Assert.Single(paged.Items);
        Assert.Equal(sample.Id, paged.Items[0].Id);

        // Verify CurrentUserId was passed down to service
        Assert.Single(_vacancyService.GetAllCalls);
        Assert.Equal(userId, _vacancyService.GetAllCalls[0].UserId);
    }

    [Fact]
    public async Task GetById_WhenFound_ReturnsOkWithVacancyDto()
    {
        // Arrange
        VacancyDto sample = CreateSampleDto("Senior Architect");
        _vacancyService.Vacancies.Add(sample);

        // Act
        ActionResult<VacancyDto> result = await _controller.GetById(sample.Id, CancellationToken.None);

        // Assert
        OkObjectResult okResult = Assert.IsType<OkObjectResult>(result.Result);
        VacancyDto dto = Assert.IsType<VacancyDto>(okResult.Value);
        Assert.Equal(sample.Id, dto.Id);
        Assert.Equal("Senior Architect", dto.Title);
    }

    [Fact]
    public async Task GetById_WhenNotFound_ReturnsNotFoundWithStandardizedApiErrorResponse()
    {
        // Arrange
        Guid missingId = Guid.NewGuid();

        // Act
        ActionResult<VacancyDto> result = await _controller.GetById(missingId, CancellationToken.None);

        // Assert
        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("NOT_FOUND", error.Error.Code);
        Assert.Contains(missingId.ToString(), error.Error.Message);
        Assert.False(string.IsNullOrWhiteSpace(error.Error.RequestId));
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreatedAtActionWithLocationAndNewDto()
    {
        // Arrange
        CreateVacancyRequest request = new()
        {
            Title = "DevOps Lead",
            Company = "CloudTeam",
            Description = "Manage infrastructure",
            Category = "DevOps",
            Location = "Remote",
            Currency = "USD",
            Source = JobSource.Dou,
            SourceUrl = "https://jobs.dou.ua/1"
        };

        // Act
        ActionResult<VacancyDto> result = await _controller.Create(request, CancellationToken.None);

        // Assert
        CreatedAtActionResult createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(VacanciesController.GetById), createdResult.ActionName);
        VacancyDto createdDto = Assert.IsType<VacancyDto>(createdResult.Value);
        Assert.Equal("DevOps Lead", createdDto.Title);
        Assert.Equal(createdDto.Id, createdResult.RouteValues!["id"]);
    }

    [Fact]
    public async Task Create_InvalidModelState_ReturnsBadRequestWithApiErrorResponse()
    {
        // Arrange
        _controller.ModelState.AddModelError("Title", "Поле 'Title' є обов'язковим.");
        CreateVacancyRequest request = new();

        // Act
        ActionResult<VacancyDto> result = await _controller.Create(request, CancellationToken.None);

        // Assert
        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("VALIDATION_ERROR", error.Error.Code);
        Assert.Empty(_vacancyService.CreateCalls); // Service must not be called
    }

    [Fact]
    public async Task Update_ValidRequest_ReturnsOkWithUpdatedDto()
    {
        // Arrange
        VacancyDto existing = CreateSampleDto("Old Title");
        _vacancyService.Vacancies.Add(existing);

        UpdateVacancyRequest request = new() { Title = "New Title" };

        // Act
        ActionResult<VacancyDto> result = await _controller.Update(existing.Id, request, CancellationToken.None);

        // Assert
        OkObjectResult okResult = Assert.IsType<OkObjectResult>(result.Result);
        VacancyDto updated = Assert.IsType<VacancyDto>(okResult.Value);
        Assert.Equal("New Title", updated.Title);
    }

    [Fact]
    public async Task Update_WhenModelStateInvalid_Returns400AndServiceIsNotCalled()
    {
        // Arrange
        VacancyDto existing = CreateSampleDto("Old Title");
        _vacancyService.Vacancies.Add(existing);
        _controller.ModelState.AddModelError("Title", "Поле 'Title' є обов'язковим.");

        // Act
        ActionResult<VacancyDto> result = await _controller.Update(existing.Id, new UpdateVacancyRequest(), CancellationToken.None);

        // Assert
        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Equal("VALIDATION_ERROR", error.Error.Code);
        Assert.Empty(_vacancyService.UpdateCalls); // Service must not be called
    }

    [Fact]
    public async Task Update_WhenNotFound_Returns404WithApiErrorResponse()
    {
        // Arrange
        Guid missingId = Guid.NewGuid();

        // Act
        ActionResult<VacancyDto> result = await _controller.Update(missingId, new UpdateVacancyRequest { Title = "New Title" }, CancellationToken.None);

        // Assert
        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("NOT_FOUND", error.Error.Code);
        Assert.Contains(missingId.ToString(), error.Error.Message);
        Assert.False(string.IsNullOrWhiteSpace(error.Error.RequestId));
    }

    [Fact]
    public async Task Delete_ExistingVacancy_ReturnsNoContent()
    {
        // Arrange
        VacancyDto existing = CreateSampleDto();
        _vacancyService.Vacancies.Add(existing);

        // Act
        IActionResult result = await _controller.Delete(existing.Id, CancellationToken.None);

        // Assert
        Assert.IsType<NoContentResult>(result);
        Assert.Contains(existing.Id, _vacancyService.DeleteCalls);
    }

    [Fact]
    public async Task Delete_WhenNotFound_Returns404WithApiErrorResponse()
    {
        // Arrange
        Guid missingId = Guid.NewGuid();

        // Act
        IActionResult result = await _controller.Delete(missingId, CancellationToken.None);

        // Assert
        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("NOT_FOUND", error.Error.Code);
        Assert.Contains(missingId.ToString(), error.Error.Message);
        Assert.Contains(missingId, _vacancyService.DeleteCalls);
    }

    [Fact]
    public async Task GetRecommendations_AuthorizedUser_ReturnsOkWithRecommendations()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        FakeRecommendationService recommendationService = new()
        {
            Result = new RecommendationsResultDto
            {
                Items = [CreateSampleDto("Python Dev")],
                SavedCount = 2,
                BasisKeywords = ["Python"]
            }
        };
        VacanciesController controller = new(_vacancyService, _logger, recommendationService);
        ControllerTestContext.Setup(controller, currentUserId: userId);

        // Act
        ActionResult<RecommendationsResultDto> result = await controller.GetRecommendations(limit: 15, CancellationToken.None);

        // Assert
        OkObjectResult okResult = Assert.IsType<OkObjectResult>(result.Result);
        RecommendationsResultDto payload = Assert.IsType<RecommendationsResultDto>(okResult.Value);
        Assert.Single(payload.Items);
        Assert.Equal(2, payload.SavedCount);
        Assert.Equal((userId, 15), recommendationService.LastCall);
    }

    [Fact]
    public async Task GetRecommendations_WithoutUser_ReturnsUnauthorized()
    {
        // Arrange
        FakeRecommendationService recommendationService = new();
        VacanciesController controller = new(_vacancyService, _logger, recommendationService);
        ControllerTestContext.Setup(controller, currentUserId: null);

        // Act
        ActionResult<RecommendationsResultDto> result = await controller.GetRecommendations(ct: CancellationToken.None);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Sync_ValidRequest_ReturnsOkWithSyncResponse()
    {
        VacancySyncRequestDto req = new()
        {
            ClientVacancies = [new VacancySyncItemDto { Title = "Offline Dev", Company = "LocalCo", Source = JobSource.Dou }],
            FullSync = true
        };
        _vacancyService.NextSyncResponse = new VacancySyncResponseDto
        {
            ReceivedFromClient = 1,
            CreatedOnServer = 1,
            TotalServerVacancies = 5
        };

        ActionResult<VacancySyncResponseDto> result = await _controller.Sync(req, CancellationToken.None);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
        VacancySyncResponseDto payload = Assert.IsType<VacancySyncResponseDto>(ok.Value);
        Assert.Equal((1, 1, 5), (payload.ReceivedFromClient, payload.CreatedOnServer, payload.TotalServerVacancies));
        Assert.Single(_vacancyService.SyncCalls);
    }

    [Fact]
    public async Task Sync_NullRequest_Returns400()
    {
        ActionResult<VacancySyncResponseDto> result = await _controller.Sync(null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private sealed class FakeRecommendationService : IRecommendationService
    {
        public RecommendationsResultDto Result { get; set; } = new();
        public (Guid UserId, int Limit)? LastCall { get; private set; }

        public Task<RecommendationsResultDto> GetRecommendationsAsync(Guid userId, int limit = 20, CancellationToken ct = default)
        {
            LastCall = (userId, limit);
            return Task.FromResult(Result);
        }
    }
}
