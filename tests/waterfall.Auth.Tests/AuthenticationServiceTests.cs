using Waterfall.Api.Services;
using Waterfall.Repository.Entities;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Auth.Tests;

/// <summary>
/// Unit tests for <see cref="AuthenticationService"/>: registration and login paths.
/// </summary>
public sealed class AuthenticationServiceTests
{
    private const string Password = "Secret123456";

    [Fact]
    public async Task Register_Success_CreatesNormalizedUser_ReturnsToken()
    {
        var users = new FakeUserRepository();
        var hasher = new FakePasswordHasher();
        var jwt = new FakeJwtTokenService();
        var logger = new FakeLogger<AuthenticationService>();
        var service = new AuthenticationService(users, hasher, jwt, logger);

        AuthResponse response = await service.RegisterAsync(new RegisterRequest
        {
            Email = "  New.User@Example.COM ",
            Password = Password,
            Role = UserRole.Analyst
        });

        Assert.Single(users.Created);
        UserDB created = users.Created[0];
        Assert.Equal("new.user@example.com", created.Email);
        Assert.Equal("hash_" + Password, created.PasswordHash);
        Assert.Equal("Analyst", created.Role);
        Assert.Equal("fake-jwt-token", response.Token);
        Assert.Equal(created.Id, response.UserId);
        Assert.Equal("new.user@example.com", response.Email);
        Assert.Equal(UserRole.Analyst, response.Role);
        Assert.Single(jwt.GenerateCalls);
        Assert.Equal((created.Id, "new.user@example.com", "Analyst"), jwt.GenerateCalls[0]);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Throws_WithoutCreating()
    {
        var users = new FakeUserRepository();
        users.Seed(new UserDB { Id = Guid.NewGuid(), Email = "taken@example.com", PasswordHash = "h", Role = "JobSeeker" });
        var service = new AuthenticationService(users, new FakePasswordHasher(), new FakeJwtTokenService(), new FakeLogger<AuthenticationService>());

        await Assert.ThrowsAsync<DuplicateEmailException>(() =>
            service.RegisterAsync(new RegisterRequest { Email = "  TAKEN@example.com ", Password = Password }));

        Assert.Empty(users.Created);
    }

    [Fact]
    public async Task Register_RaceUniqueViolation_MapsToDuplicateEmail()
    {
        var users = new FakeUserRepository { ThrowUniqueViolationOnCreate = true };
        var service = new AuthenticationService(users, new FakePasswordHasher(), new FakeJwtTokenService(), new FakeLogger<AuthenticationService>());

        await Assert.ThrowsAsync<DuplicateEmailException>(() =>
            service.RegisterAsync(new RegisterRequest { Email = "racer@example.com", Password = Password }));
    }

    [Fact]
    public async Task Login_UnknownStoredRole_FallsBackToJobSeeker()
    {
        var users = new FakeUserRepository();
        users.Seed(new UserDB { Id = Guid.NewGuid(), Email = "legacy@example.com", PasswordHash = "h", Role = "SuperAdmin" });
        var jwt = new FakeJwtTokenService();
        var service = new AuthenticationService(users, new FakePasswordHasher(), jwt, new FakeLogger<AuthenticationService>());

        AuthResponse response = await service.LoginAsync(new LoginRequest { Email = "legacy@example.com", Password = Password });

        Assert.Equal(UserRole.JobSeeker, response.Role);
        Assert.Equal("JobSeeker", jwt.GenerateCalls[0].Role);
    }

    [Fact]
    public async Task Register_LogsUserId_NeverEmail()
    {
        const string email = "private.user@example.com";
        var logger = new FakeLogger<AuthenticationService>();
        var service = new AuthenticationService(new FakeUserRepository(), new FakePasswordHasher(), new FakeJwtTokenService(), logger);

        AuthResponse response = await service.RegisterAsync(new RegisterRequest { Email = email, Password = Password });

        Assert.NotEmpty(logger.Logs);
        foreach (string entry in logger.Logs)
        {
            Assert.DoesNotContain(email, entry, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Password, entry, StringComparison.Ordinal);
        }

        Assert.Contains(logger.Logs, e => e.Contains(response.UserId.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_Success_ReturnsToken()
    {
        var users = new FakeUserRepository();
        var userId = Guid.NewGuid();
        users.Seed(new UserDB { Id = userId, Email = "seeker@example.com", PasswordHash = "hash_" + Password, Role = "JobSeeker" });
        var service = new AuthenticationService(users, new FakePasswordHasher(), new FakeJwtTokenService(), new FakeLogger<AuthenticationService>());

        AuthResponse response = await service.LoginAsync(new LoginRequest { Email = " Seeker@Example.com ", Password = Password });

        Assert.Equal(userId, response.UserId);
        Assert.Equal("seeker@example.com", response.Email);
        Assert.Equal("fake-jwt-token", response.Token);
        Assert.Equal("seeker@example.com", users.GetByEmailCalls[0]);
    }

    [Fact]
    public async Task Login_UnknownEmail_ThrowsInvalidCredentials_AndVerifiesNullHash()
    {
        var users = new FakeUserRepository();
        var hasher = new FakePasswordHasher { VerifyFunc = (_, _) => PasswordVerifyResult.Failed };
        var service = new AuthenticationService(users, hasher, new FakeJwtTokenService(), new FakeLogger<AuthenticationService>());

        InvalidCredentialsException ex = await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.LoginAsync(new LoginRequest { Email = "ghost@example.com", Password = Password }));

        // Generic message: must not reveal whether the email exists (THR-01).
        Assert.Equal("Неправильний email або пароль.", ex.Message);
        Assert.Single(hasher.VerifyCalls);
        Assert.Null(hasher.VerifyCalls[0].Hash);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsSameExceptionAsUnknownEmail()
    {
        var users = new FakeUserRepository();
        users.Seed(new UserDB { Id = Guid.NewGuid(), Email = "real@example.com", PasswordHash = "hash_correct", Role = "JobSeeker" });
        var hasher = new FakePasswordHasher { VerifyFunc = (_, _) => PasswordVerifyResult.Failed };
        var service = new AuthenticationService(users, hasher, new FakeJwtTokenService(), new FakeLogger<AuthenticationService>());

        Task<InvalidCredentialsException> wrongPassword = Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.LoginAsync(new LoginRequest { Email = "real@example.com", Password = "Wrong99999999" }));
        Task<InvalidCredentialsException> unknownEmail = Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            service.LoginAsync(new LoginRequest { Email = "nobody@example.com", Password = "Wrong99999999" }));

        Assert.Equal((await wrongPassword).Message, (await unknownEmail).Message);
        Assert.Equal("hash_correct", hasher.VerifyCalls[0].Hash);
    }

    [Fact]
    public async Task Login_RehashNeeded_RehashesAndUpdates()
    {
        var users = new FakeUserRepository();
        var user = new UserDB { Id = Guid.NewGuid(), Email = "rehash@example.com", PasswordHash = "old-hash", Role = "JobSeeker" };
        users.Seed(user);
        var hasher = new FakePasswordHasher { VerifyFunc = (_, _) => PasswordVerifyResult.SuccessRehashNeeded };
        var service = new AuthenticationService(users, hasher, new FakeJwtTokenService(), new FakeLogger<AuthenticationService>());

        await service.LoginAsync(new LoginRequest { Email = "rehash@example.com", Password = Password });

        Assert.Single(users.Updated);
        Assert.Equal("hash_" + Password, user.PasswordHash);
    }
}
