// AI: тестовий контекст та моки сервісів (ServiceMocks, ControllerTestContext, FakeVacancyService, FakeSavedVacancyService, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Common;
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
    public List<CreateVacancyRequest> CreateCalls { get; } = [];
    public List<(Guid Id, UpdateVacancyRequest Request)> UpdateCalls { get; } = [];
    public List<Guid> DeleteCalls { get; } = [];

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

    public Task<VacancyDto> CreateAsync(CreateVacancyRequest request, CancellationToken ct = default)
    {
        CreateCalls.Add(request);
        VacancyDto created = new()
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Company = request.Company,
            Description = request.Description,
            Category = request.Category,
            Location = request.Location,
            Currency = request.Currency,
            Source = request.Source,
            SourceUrl = request.SourceUrl,
            Skills = request.Skills ?? [],
            PublishedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
        Vacancies.Add(created);
        return Task.FromResult(created);
    }

    public Task<VacancyDto?> UpdateAsync(Guid id, UpdateVacancyRequest request, CancellationToken ct = default)
    {
        UpdateCalls.Add((id, request));
        int index = Vacancies.FindIndex(v => v.Id == id);
        if (index == -1) return Task.FromResult<VacancyDto?>(null);

        VacancyDto existing = Vacancies[index];
        VacancyDto updated = new()
        {
            Id = existing.Id,
            Title = request.Title ?? existing.Title,
            Company = request.Company ?? existing.Company,
            Description = request.Description ?? existing.Description,
            Category = request.Category ?? existing.Category,
            Location = request.Location ?? existing.Location,
            Currency = request.Currency ?? existing.Currency,
            Source = request.Source ?? existing.Source,
            SourceUrl = request.SourceUrl ?? existing.SourceUrl,
            Skills = request.Skills ?? existing.Skills,
            PublishedAt = existing.PublishedAt,
            IsActive = request.IsActive ?? existing.IsActive
        };
        Vacancies[index] = updated;
        return Task.FromResult<VacancyDto?>(updated);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        DeleteCalls.Add(id);
        int removed = Vacancies.RemoveAll(v => v.Id == id);
        return Task.FromResult(removed > 0);
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
        SavedVacancyDto? match = SavedVacancies.FirstOrDefault(s => s.Id == id || s.VacancyId == id);
        return Task.FromResult(match);
    }

    public Task<SavedVacancyDto> SaveAsync(SaveVacancyRequest request, Guid userId, CancellationToken ct = default)
    {
        SaveCalls.Add((request, userId));
        SavedVacancyDto saved = new()
        {
            Id = Guid.NewGuid(),
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
        int removed = SavedVacancies.RemoveAll(s => s.Id == id || s.VacancyId == id);
        return Task.FromResult(removed > 0);
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
