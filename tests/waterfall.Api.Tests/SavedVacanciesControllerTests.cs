// AI: модульні тести контролера закладок (SavedVacanciesController, HTTP 409 Conflict, мульти-тенантність, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using Microsoft.AspNetCore.Mvc;
using Waterfall.Api.Controllers;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Models.SavedVacancies;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Tests;

/// <summary>
/// Модульні тести для SavedVacanciesController (керування «лайками» / збереженими вакансіями).
/// Перевіряє HTTP 200, 201, 204, 400, 404 та 409 Conflict при повторному збереженні.
/// </summary>
public sealed class SavedVacanciesControllerTests
{
    private readonly FakeSavedVacancyService _savedVacancyService = new();
    private readonly FakeVacancyService _vacancyService = new();
    private readonly FakeLogger<SavedVacanciesController> _logger = new();
    private readonly SavedVacanciesController _controller;
    private readonly Guid _currentUserId = Guid.NewGuid();

    public SavedVacanciesControllerTests()
    {
        _controller = new SavedVacanciesController(_savedVacancyService, _vacancyService, _logger);
        ControllerTestContext.Setup(_controller, currentUserId: _currentUserId);
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithUserSavedVacancies()
    {
        // Arrange
        SavedVacancyDto saved = new()
        {
            Id = Guid.NewGuid(),
            VacancyId = Guid.NewGuid(),
            Notes = "Check on Friday",
            SavedAt = DateTimeOffset.UtcNow
        };
        _savedVacancyService.SavedVacancies.Add(saved);

        // Act
        ActionResult<List<SavedVacancyDto>> result = await _controller.GetAll(onlyActive: true);

        // Assert
        OkObjectResult okResult = Assert.IsType<OkObjectResult>(result.Result);
        IReadOnlyList<SavedVacancyDto> items = Assert.IsAssignableFrom<IReadOnlyList<SavedVacancyDto>>(okResult.Value);
        Assert.Single(items);
        Assert.Equal(saved.Id, items[0].Id);
        Assert.Equal(_currentUserId, _savedVacancyService.GetAllCalls[0].UserId);
    }

    [Fact]
    public async Task GetById_WhenFound_ReturnsOkWithDto()
    {
        // Arrange
        Guid savedId = Guid.NewGuid();
        SavedVacancyDto saved = new()
        {
            Id = savedId,
            VacancyId = Guid.NewGuid(),
            Notes = "Prepare resume"
        };
        _savedVacancyService.SavedVacancies.Add(saved);

        // Act
        ActionResult<SavedVacancyDto> result = await _controller.GetById(savedId, CancellationToken.None);

        // Assert
        OkObjectResult okResult = Assert.IsType<OkObjectResult>(result.Result);
        SavedVacancyDto dto = Assert.IsType<SavedVacancyDto>(okResult.Value);
        Assert.Equal(savedId, dto.Id);
        Assert.Equal("Prepare resume", dto.Notes);
    }

    [Fact]
    public async Task Save_WhenVacancyDoesNotExist_ReturnsNotFound()
    {
        // Arrange
        Guid nonExistentVacId = Guid.NewGuid();
        SaveVacancyRequest request = new() { VacancyId = nonExistentVacId };

        // Act
        ActionResult<SavedVacancyDto> result = await _controller.Save(request, CancellationToken.None);

        // Assert
        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("NOT_FOUND", error.Error.Code);
        Assert.Contains(nonExistentVacId.ToString(), error.Error.Message);
    }

    [Fact]
    public async Task Save_WhenVacancyAlreadySaved_ReturnsConflictHttp409()
    {
        // Arrange
        Guid vacancyId = Guid.NewGuid();
        VacancyDto existingVacancy = new()
        {
            Id = vacancyId,
            Title = "Already Saved Job",
            Company = "Company",
            Description = "Desc",
            Category = "IT",
            Location = "Kyiv",
            Currency = "USD",
            PublishedAt = DateTimeOffset.UtcNow,
            IsSaved = true // Already saved!
        };
        _vacancyService.Vacancies.Add(existingVacancy);

        SaveVacancyRequest request = new() { VacancyId = vacancyId };

        // Act
        ActionResult<SavedVacancyDto> result = await _controller.Save(request, CancellationToken.None);

        // Assert - HTTP 409 Conflict
        ConflictObjectResult conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        ApiErrorResponse error = Assert.IsType<ApiErrorResponse>(conflict.Value);
        Assert.Equal("CONFLICT", error.Error.Code);
        Assert.Contains(vacancyId.ToString(), error.Error.Message);
        Assert.Empty(_savedVacancyService.SaveCalls); // Save service must not be invoked
    }

    [Fact]
    public async Task Save_ValidAndNotSaved_ReturnsCreatedAtActionWithSavedDto()
    {
        // Arrange
        Guid vacancyId = Guid.NewGuid();
        VacancyDto existingVacancy = new()
        {
            Id = vacancyId,
            Title = "Fresh Job",
            Company = "Company",
            Description = "Desc",
            Category = "IT",
            Location = "Kyiv",
            Currency = "USD",
            PublishedAt = DateTimeOffset.UtcNow,
            IsSaved = false
        };
        _vacancyService.Vacancies.Add(existingVacancy);

        SaveVacancyRequest request = new()
        {
            VacancyId = vacancyId,
            Notes = "Review before applying"
        };

        // Act
        ActionResult<SavedVacancyDto> result = await _controller.Save(request, CancellationToken.None);

        // Assert
        CreatedAtActionResult createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(SavedVacanciesController.GetById), createdResult.ActionName);
        SavedVacancyDto saved = Assert.IsType<SavedVacancyDto>(createdResult.Value);
        Assert.Equal(vacancyId, saved.VacancyId);
        Assert.Equal("Review before applying", saved.Notes);
    }

    [Fact]
    public async Task Delete_ExistingSavedVacancy_ReturnsNoContent()
    {
        // Arrange
        Guid savedId = Guid.NewGuid();
        _savedVacancyService.SavedVacancies.Add(new SavedVacancyDto { Id = savedId, VacancyId = Guid.NewGuid() });

        // Act
        IActionResult result = await _controller.Delete(savedId, CancellationToken.None);

        // Assert
        Assert.IsType<NoContentResult>(result);
        Assert.Contains(_savedVacancyService.DeleteCalls, d => d.Id == savedId && d.UserId == _currentUserId);
    }
}
