// AI: Генерація структури регресійних тестів безпеки для CWE-532 та допоміжних тестових заглушок (FakeLogger, FakeUserRepository, FakePasswordHasher, FakeJwtTokenService, FakeAuthService); перевірено: dotnet test, ручна самоперевірка (Денис Сидоренко, §8)

using Microsoft.Extensions.Logging;
using Waterfall.Api.Controllers;
using Waterfall.Api.Services;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Api.Tests;

/// <summary>
/// Регресійні тести безпеки для CWE-532 (Insertion of Sensitive Information into Log File).
/// Перевіряють, що email та пароль користувача не потрапляють у логи під час операцій автентифікації (реєстрація та вхід).
/// </summary>
public sealed class SensitiveLoggingRegressionTests
{
    private const string SensitiveEmail = "victim.user@example.com";
    private const string SensitivePassword = "SuperSecretPassword123!";

    [Fact]
    public async Task AuthController_ShouldNotLogSensitiveEmail_RegressionTest()
    {
        // Arrange
        var logger = new FakeSecurityLogger<AuthController>();
        var fakeAuthService = new FakeAuthService();
        var controller = new AuthController(fakeAuthService, logger);

        var request = new RegisterRequest
        {
            Email = SensitiveEmail,
            Password = SensitivePassword,
            Role = UserRole.JobSeeker
        };

        // Act
        await controller.Register(request, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logger.Logs);
        foreach (string logEntry in logger.Logs)
        {
            Assert.DoesNotContain(SensitiveEmail, logEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SensitivePassword, logEntry, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AuthenticationService_ShouldNotLogSensitiveEmail_RegressionTest()
    {
        // Arrange
        var logger = new FakeSecurityLogger<AuthenticationService>();
        var userRepo = new FakeUserRepository();
        var hasher = new FakePasswordHasher();
        var jwt = new FakeJwtTokenService();
        var service = new AuthenticationService(userRepo, hasher, jwt, logger);

        var request = new RegisterRequest
        {
            Email = SensitiveEmail,
            Password = SensitivePassword,
            Role = UserRole.JobSeeker
        };

        // Act
        await service.RegisterAsync(request);

        // Assert
        Assert.NotEmpty(logger.Logs);
        foreach (string logEntry in logger.Logs)
        {
            Assert.DoesNotContain(SensitiveEmail, logEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SensitivePassword, logEntry, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AuthController_ShouldNotLogSensitiveData_OnLogin_RegressionTest()
    {
        // Arrange
        var logger = new FakeSecurityLogger<AuthController>();
        var fakeAuthService = new FakeAuthService();
        var controller = new AuthController(fakeAuthService, logger);

        var request = new LoginRequest
        {
            Email = SensitiveEmail,
            Password = SensitivePassword
        };

        // Act
        await controller.Login(request, CancellationToken.None);

        // Assert
        Assert.NotEmpty(logger.Logs);
        foreach (string logEntry in logger.Logs)
        {
            Assert.DoesNotContain(SensitiveEmail, logEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SensitivePassword, logEntry, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AuthenticationService_ShouldNotLogSensitiveData_OnSuccessfulLogin_RegressionTest()
    {
        // Arrange
        var logger = new FakeSecurityLogger<AuthenticationService>();
        var userRepo = new FakeUserRepository();
        var hasher = new FakePasswordHasher();
        var jwt = new FakeJwtTokenService();
        var service = new AuthenticationService(userRepo, hasher, jwt, logger);

        var existingUser = new UserDB
        {
            Id = Guid.NewGuid(),
            Email = SensitiveEmail.ToLowerInvariant(),
            PasswordHash = hasher.Hash(SensitivePassword),
            Role = UserRole.JobSeeker.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        await userRepo.CreateAsync(existingUser);

        var request = new LoginRequest
        {
            Email = SensitiveEmail,
            Password = SensitivePassword
        };

        // Act
        await service.LoginAsync(request);

        // Assert
        Assert.NotEmpty(logger.Logs);
        foreach (string logEntry in logger.Logs)
        {
            Assert.DoesNotContain(SensitiveEmail, logEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SensitivePassword, logEntry, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AuthenticationService_ShouldNotLogSensitiveData_OnFailedLogin_RegressionTest()
    {
        // Arrange
        var logger = new FakeSecurityLogger<AuthenticationService>();
        var userRepo = new FakeUserRepository(); // Empty repo -> user is null -> login fails
        var hasher = new FakePasswordHasher();
        var jwt = new FakeJwtTokenService();
        var service = new AuthenticationService(userRepo, hasher, jwt, logger);

        var request = new LoginRequest
        {
            Email = SensitiveEmail,
            Password = SensitivePassword
        };

        // Act
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => service.LoginAsync(request));

        // Assert
        Assert.NotEmpty(logger.Logs);
        foreach (string logEntry in logger.Logs)
        {
            Assert.DoesNotContain(SensitiveEmail, logEntry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SensitivePassword, logEntry, StringComparison.OrdinalIgnoreCase);
        }
    }

    #region Fakes

    private sealed class FakeSecurityLogger<T> : ILogger<T>
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
            Func<TState, Exception?, string> formatter) => _logs.Add(formatter(state, exception));
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<UserDB> _users = new();

        public Task<UserDB?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default)
        {
            UserDB? user = _users.FirstOrDefault(u => u.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(user);
        }

        public Task<UserDB?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => throw new NotSupportedException("GetByIdAsync не використовується цими тестами.");

        public Task<UserDB> CreateAsync(UserDB entity, CancellationToken ct = default)
        {
            _users.Add(entity);
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(UserDB entity, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => "fake_hash_" + password;

        public PasswordVerifyResult Verify(string? hashedPassword, string providedPassword) => PasswordVerifyResult.Success;
    }

    private sealed class FakeJwtTokenService : IJwtTokenService
    {
        public (string Token, DateTimeOffset ExpiresAt) Generate(Guid userId, string email, string role) =>
            ("fake-jwt-token", DateTimeOffset.UtcNow.AddMinutes(15));

        public System.Security.Claims.ClaimsPrincipal? Validate(string token) => null;
    }

    private sealed class FakeAuthService : IAuthenticationService
    {
        public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new AuthResponse
            {
                UserId = Guid.NewGuid(),
                Email = request.Email,
                Role = request.Role,
                Token = "token",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
            });
        }

        public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new AuthResponse
            {
                UserId = Guid.NewGuid(),
                Email = request.Email,
                Role = UserRole.JobSeeker,
                Token = "token",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)
            });
        }
    }

    #endregion
}
