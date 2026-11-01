using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Interfaces;

namespace Waterfall.Auth.Tests;

/// <summary>
/// Shared test doubles for auth tests (mirror the fakes in Security.Tests, extended with call tracking).
/// </summary>
internal sealed class FakeLogger<T> : ILogger<T>
{
    private readonly List<string> _logs = new();
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
        _logs.Add(formatter(state, exception));
    }
}

internal sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<string, UserDB> _users = new(StringComparer.OrdinalIgnoreCase);

    public List<string> GetByEmailCalls { get; } = [];
    public List<UserDB> Created { get; } = [];
    public List<UserDB> Updated { get; } = [];
    public bool ThrowUniqueViolationOnCreate { get; set; }

    public void Seed(UserDB user) => _users[user.Email] = user;

    public Task<UserDB?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default)
    {
        GetByEmailCalls.Add(normalizedEmail);
        _users.TryGetValue(normalizedEmail, out UserDB? user);
        return Task.FromResult(user);
    }

    public Task<UserDB?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => throw new NotSupportedException("GetByIdAsync не використовується цими тестами.");

    public Task<UserDB> CreateAsync(UserDB entity, CancellationToken ct = default)
    {
        if (ThrowUniqueViolationOnCreate)
        {
            // Simulate a unique-index race: the rival row is now visible on re-read.
            _users[entity.Email] = new UserDB
            {
                Id = Guid.NewGuid(),
                Email = entity.Email,
                PasswordHash = "rival-hash",
                Role = "JobSeeker",
                CreatedAt = DateTimeOffset.UtcNow
            };
            throw new DbUpdateException("Unique index violation on Users.Email.", new Exception("UNIQUE constraint failed: Users.Email"));
        }

        _users[entity.Email] = entity;
        Created.Add(entity);
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(UserDB entity, CancellationToken ct = default)
    {
        Updated.Add(entity);
        return Task.CompletedTask;
    }
}

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public List<(string? Hash, string Password)> VerifyCalls { get; } = [];
    public Func<string?, string, PasswordVerifyResult> VerifyFunc { get; set; } = (_, _) => PasswordVerifyResult.Success;
    public Func<string, string> HashFunc { get; set; } = password => "hash_" + password;

    public string Hash(string password) => HashFunc(password);

    public PasswordVerifyResult Verify(string? hashedPassword, string providedPassword)
    {
        VerifyCalls.Add((hashedPassword, providedPassword));
        return VerifyFunc(hashedPassword, providedPassword);
    }
}

internal sealed class FakeJwtTokenService : IJwtTokenService
{
    public List<(Guid UserId, string Email, string Role)> GenerateCalls { get; } = [];
    public string TokenToReturn { get; set; } = "fake-jwt-token";
    public DateTimeOffset ExpiresToReturn { get; set; } = DateTimeOffset.UtcNow.AddMinutes(15);

    public (string Token, DateTimeOffset ExpiresAt) Generate(Guid userId, string email, string role)
    {
        GenerateCalls.Add((userId, email, role));
        return (TokenToReturn, ExpiresToReturn);
    }

    public System.Security.Claims.ClaimsPrincipal? Validate(string token) => null;
}
