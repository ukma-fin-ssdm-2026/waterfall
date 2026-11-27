// AI: тестовий контекст та моки сервісів (ServiceMocks, ControllerTestContext, FakeVacancyService, FakeSavedVacancyService, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Profile;
using Waterfall.Services.Models.SavedVacancies;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Api.Tests;

/// <summary>
/// Моки сервісів (ServiceMocks) та тестовий контекст для ізольованого тестування ASP.NET Core контролерів.
/// </summary>
public sealed class FakeLogger<T> : ILogger<T>
{
    private readonly List<string> _logs = [];
    public IReadOnlyList<string> Logs => _logs;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _logs.Add($"[{logLevel}] {formatter(state, exception)}");
    }
}

public sealed class FakeVacancyService : IVacancyService
{
    public List<VacancyDto> Vacancies { get; } = [];
    public List<(VacancyFilterParameters Filter, Guid? UserId)> GetAllCalls { get; } = [];
    public List<(Guid Id, Guid? UserId)> GetByIdCalls { get; } = [];

    public Task<PagedResult<VacancyDto>> GetAllAsync(VacancyFilterParameters filter, CancellationToken ct = default, Guid? userId = null)
    {
        GetAllCalls.Add((filter, userId));
        IReadOnlyList<VacancyDto> items = Vacancies
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        PagedResult<VacancyDto> paged = new()
        {
            Items = items,
            TotalCount = Vacancies.Count,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
        return Task.FromResult(paged);
    }

    public Task<VacancyDto?> GetByIdAsync(Guid id, CancellationToken ct = default, Guid? userId = null)
    {
        GetByIdCalls.Add((id, userId));
        VacancyDto? match = Vacancies.FirstOrDefault(v => v.Id == id);
        return Task.FromResult(match);
    }

    public List<VacancySyncRequestDto> SyncCalls { get; } = [];
    public VacancySyncResponseDto? NextSyncResponse { get; set; }

    public Task<VacancySyncResponseDto> SyncVacanciesAsync(VacancySyncRequestDto request, CancellationToken ct = default)
    {
        SyncCalls.Add(request);
        return Task.FromResult(NextSyncResponse ?? new VacancySyncResponseDto
        {
            ReceivedFromClient = request.ClientVacancies.Count,
            TotalServerVacancies = Vacancies.Count
        });
    }
}

public sealed class FakeSavedVacancyService : ISavedVacancyService
{
    public List<SavedVacancyDto> SavedVacancies { get; } = [];
    public List<(Guid UserId, bool OnlyActive)> GetAllCalls { get; } = [];
    public List<(Guid Id, Guid UserId)> GetByIdCalls { get; } = [];
    public List<(SaveVacancyRequest Request, Guid UserId)> SaveCalls { get; } = [];
    public List<(Guid Id, Guid UserId)> DeleteCalls { get; } = [];

    public Task<IReadOnlyList<SavedVacancyDto>> GetAllAsync(Guid userId, bool onlyActive = true, CancellationToken ct = default)
    {
        GetAllCalls.Add((userId, onlyActive));
        IReadOnlyList<SavedVacancyDto> items = SavedVacancies.ToList();
        return Task.FromResult(items);
    }

    public Task<SavedVacancyDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        GetByIdCalls.Add((id, userId));
        SavedVacancyDto? match = SavedVacancies.FirstOrDefault(s => s.VacancyId == id || s.Id == id);
        return Task.FromResult(match);
    }

    public Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, Guid userId, CancellationToken ct = default)
    {
        SaveCalls.Add((request, userId));
        if (SavedVacancies.Any(s => s.VacancyId == request.VacancyId))
        {
            throw new DuplicateSavedVacancyException(request.VacancyId);
        }

        SavedVacancyDto saved = new()
        {
            Id = request.VacancyId,
            VacancyId = request.VacancyId,
            Notes = request.Notes,
            SavedAt = DateTimeOffset.UtcNow
        };
        SavedVacancies.Add(saved);
        return Task.FromResult(saved);
    }

    public Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        DeleteCalls.Add((id, userId));
        int removed = SavedVacancies.RemoveAll(s => s.VacancyId == id || s.Id == id);
        return Task.FromResult(removed > 0);
    }
}

public sealed class FakeUserStatsService : IUserStatsService
{
    public List<Guid> GetStatsCalls { get; } = [];
    public List<(Guid UserId, string Query)> RecordSearchCalls { get; } = [];
    public List<(Guid UserId, Guid VacancyId)> RecordViewCalls { get; } = [];
    public List<(Guid UserId, Guid VacancyId)> UnmarkViewCalls { get; } = [];
    public List<Guid> ClearSearchHistoryCalls { get; } = [];

    public UserStatsDto StatsToReturn { get; set; } = new();
    public UserStatsRecordResult SearchResult { get; set; } = UserStatsRecordResult.Recorded;
    public UserStatsRecordResult ViewResult { get; set; } = UserStatsRecordResult.Recorded;

    public Task<UserStatsDto> GetStatsAsync(Guid userId, CancellationToken ct = default)
    {
        GetStatsCalls.Add(userId);
        return Task.FromResult(StatsToReturn);
    }

    public Task<UserStatsRecordResult> RecordSearchAsync(Guid userId, string query, CancellationToken ct = default)
    {
        RecordSearchCalls.Add((userId, query));
        return Task.FromResult(SearchResult);
    }

    public Task<UserStatsRecordResult> RecordVacancyViewAsync(Guid userId, Guid vacancyId, CancellationToken ct = default)
    {
        RecordViewCalls.Add((userId, vacancyId));
        return Task.FromResult(ViewResult);
    }

    public Task<UserStatsRecordResult> UnmarkVacancyViewAsync(Guid userId, Guid vacancyId, CancellationToken ct = default)
    {
        UnmarkViewCalls.Add((userId, vacancyId));
        return Task.FromResult(ViewResult);
    }

    public Task<bool> ClearSearchHistoryAsync(Guid userId, CancellationToken ct = default)
    {
        ClearSearchHistoryCalls.Add(userId);
        return Task.FromResult(ClearSearchHistoryCalls.Count > 0);
    }
}

public static class ControllerTestContext
{
    public static void Setup(
        ControllerBase controller,
        Guid? currentUserId = null,
        string? correlationId = null,
        string? requestId = null)
    {
        DefaultHttpContext httpContext = new();

        if (currentUserId.HasValue)
        {
            Claim[] claims =
            [
                new Claim(JwtRegisteredClaimNames.Sub, currentUserId.Value.ToString()),
                new Claim(ClaimTypes.NameIdentifier, currentUserId.Value.ToString())
            ];
            ClaimsIdentity identity = new(claims, "TestAuth");
            httpContext.User = new ClaimsPrincipal(identity);
        }

        if (!string.IsNullOrEmpty(correlationId))
        {
            httpContext.Request.Headers["X-Correlation-Id"] = correlationId;
        }

        if (!string.IsNullOrEmpty(requestId))
        {
            httpContext.Request.Headers["X-Request-Id"] = requestId;
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }
}
