// AI: Тести обробки помилок та стійкості (Lab 9, Домашнє завдання 2)
// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Controllers;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Geo;
using Waterfall.Services.Models.SavedVacancies;

namespace Waterfall.Reliability.Tests;

/// <summary>
/// Тести обробки помилок: перевіряють, що система не падає при непередбачених винятках,
/// коректно логує інциденти та повертає зрозумілу відповідь клієнту або безпечне значення (fail-fast / fail-safe).
/// </summary>
public class ErrorHandlingTests
{
    [Theory]
    [InlineData("{ invalid json string }")]
    [InlineData("[ \"C#\", unclosed string ]")]
    [InlineData("{\"key\": 123}")]
    public void ParseSkills_WhenJsonCorrupted_DoesNotCrash_ReturnsEmptyFallbackAndLogsWarning(string corruptedJson)
    {
        // Arrange
        TestLogger logger = new();

        // Act - не повинно викидати неперехоплений JsonException (FIND-07)
        IReadOnlyList<string> skills = VacancyService.ParseSkills(corruptedJson, logger);

        // Assert - безпечне резервне значення (Safe Fallback)
        Assert.NotNull(skills);
        Assert.Empty(skills);

        // Перевіряємо, що помилка не проковтнута мовчки, а зафіксована у журналі як попередження
        Assert.True(logger.WarningLogged, "Помилка десеріалізації пошкодженого JSON повинна фіксуватися в лозі як попередження.");
    }

    [Fact]
    public async Task SavedVacancyService_SaveAsync_WhenRequestIsNull_ThrowsArgumentNullException_FailFast()
    {
        // Arrange
        FakeSavedVacancyRepository repo = new();
        FakeCacheService cache = new();
        SavedVacancyService service = new(repo, cache, NullLogger<SavedVacancyService>.Instance);

        // Act & Assert - швидка відмова (Fail-Fast, FIND-06)
        ArgumentNullException ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SaveAsync(null!, Guid.NewGuid()));

        Assert.Equal("request", ex.ParamName);
    }

    [Fact]
    public async Task SavedVacancyService_SaveAsync_WhenIdsAreEmpty_ThrowsInformativeArgumentException_FailFast()
    {
        // Arrange
        FakeSavedVacancyRepository repo = new();
        FakeCacheService cache = new();
        SavedVacancyService service = new(repo, cache, NullLogger<SavedVacancyService>.Instance);

        SaveVacancyRequest emptyVacancyRequest = new() { VacancyId = Guid.Empty };
        SaveVacancyRequest validRequest = new() { VacancyId = Guid.NewGuid() };

        // Act & Assert
        ArgumentException exVacancy = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync(emptyVacancyRequest, Guid.NewGuid()));
        Assert.Contains("Ідентифікатор вакансії не може бути порожнім", exVacancy.Message);

        ArgumentException exUser = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync(validRequest, Guid.Empty));
        Assert.Contains("Ідентифікатор користувача не може бути порожнім", exUser.Message);
    }

    [Fact]
    public async Task GeoController_SyncCoordinates_WhenBodyIsNull_ReturnsBadRequest_InsteadOfInternal500Crash()
    {
        // Arrange
        FakeGeoLocationService geoService = new();
        GeoController controller = new(geoService)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        // Act
        ActionResult<GeoSyncResponseDto> result = await controller.SyncCoordinates(null, CancellationToken.None);

        // Assert - система повертає контрольовану відповідь 400 Bad Request, а не аварійний 500 NRE (FIND-05)
        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task GeoLocationService_GetCoordinatesAsync_WhenAddressIsBlank_ReturnsNullGracefully()
    {
        // Arrange
        FakeGeoLocationCacheRepository cache = new();
        GeoLocationService service = new(cache);

        // Act
        (double Latitude, double Longitude)? resultNull = await service.GetCoordinatesAsync(null!);
        (double Latitude, double Longitude)? resultEmpty = await service.GetCoordinatesAsync("   ");

        // Assert
        Assert.Null(resultNull);
        Assert.Null(resultEmpty);
    }

    private sealed class TestLogger : ILogger
    {
        public bool WarningLogged { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                WarningLogged = true;
            }
        }
    }

    private sealed class FakeSavedVacancyRepository : ISavedVacancyRepository
    {
        public Task<IReadOnlyList<SavedVacancyDB>> GetAllAsync(Guid userId, bool onlyActive = true, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SavedVacancyDB>>(new List<SavedVacancyDB>());
        public Task<SavedVacancyDB?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<SavedVacancyDB?>(null);
        public Task<SavedVacancyDB> SaveAsync(SavedVacancyDB entity, CancellationToken ct = default) =>
            Task.FromResult(entity);
        public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(true);
        public Task<bool> ExistsForVacancyAsync(Guid vacancyId, Guid? userId, CancellationToken ct = default) =>
            Task.FromResult(false);
        public Task<HashSet<Guid>> GetSavedVacancyIdsAsync(IEnumerable<Guid> vacancyIds, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(new HashSet<Guid>());
    }

    private sealed class FakeCacheService : IVacancyCacheService
    {
        public DateTimeOffset? LastCatalogRefreshedAt => null;
        public Task<Waterfall.Services.Models.Common.PagedResult<Waterfall.Services.Models.Vacancies.VacancyDto>?> GetPagedVacanciesAsync(Waterfall.Services.Models.Vacancies.VacancyFilterParameters filter, CancellationToken ct = default) =>
            Task.FromResult<Waterfall.Services.Models.Common.PagedResult<Waterfall.Services.Models.Vacancies.VacancyDto>?>(null);
        public Task SetPagedVacanciesAsync(Waterfall.Services.Models.Vacancies.VacancyFilterParameters filter, Waterfall.Services.Models.Common.PagedResult<Waterfall.Services.Models.Vacancies.VacancyDto> result, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<Waterfall.Services.Models.Vacancies.VacancyDto?> GetVacancyByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<Waterfall.Services.Models.Vacancies.VacancyDto?>(null);
        public Task SetVacancyByIdAsync(Guid id, Waterfall.Services.Models.Vacancies.VacancyDto dto, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task InvalidateCatalogAsync(CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task InvalidateVacancyAsync(Guid vacancyId, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Waterfall.Services.Models.Common.PagedResult<Waterfall.Services.Models.Vacancies.VacancyDto>? GetStaleCatalog() => null;
        public (long Hits, long Misses) GetMetrics() => (0, 0);
    }

    private sealed class FakeGeoLocationService : IGeoLocationService
    {
        public Task<(double Latitude, double Longitude)?> GetCoordinatesAsync(string address, CancellationToken ct = default) => Task.FromResult<(double Latitude, double Longitude)?>(null);
        public Task<GeoSyncResponseDto> SyncCacheAsync(GeoSyncRequestDto request, CancellationToken ct = default) => Task.FromResult(new GeoSyncResponseDto());
    }

    private sealed class FakeGeoLocationCacheRepository : IGeoLocationCacheRepository
    {
        public Task<IReadOnlyList<GeoLocationCacheDB>> GetAllCachedAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GeoLocationCacheDB>>(new List<GeoLocationCacheDB>());
        public Task<GeoLocationCacheDB?> GetByAddressAsync(string address, CancellationToken ct = default) => Task.FromResult<GeoLocationCacheDB?>(null);
        public Task<GeoLocationCacheDB> SetCoordinatesAsync(string address, double latitude, double longitude, CancellationToken ct = default) => Task.FromResult(new GeoLocationCacheDB());
        public Task UpsertRangeAsync(IEnumerable<GeoLocationCacheDB> items, CancellationToken ct = default) => Task.CompletedTask;
    }
}
