using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Controllers;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Auth.Tests;

/// <summary>
/// Unit tests for <see cref="AuthController"/> HTTP mapping (status codes, no service call on invalid ModelState).
/// </summary>
public sealed class AuthControllerTests
{
    private static AuthResponse SampleResponse() => new()
    {
        Token = "jwt",
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        UserId = Guid.NewGuid(),
        Email = "seeker@example.com",
        Role = UserRole.JobSeeker
    };

    private static AuthController CreateController(IAuthenticationService auth)
    {
        var controller = new AuthController(auth, NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    [Fact]
    public async Task Register_Success_Returns201_WithBody()
    {
        var auth = new StubAuthService(register: (_, _) => Task.FromResult(SampleResponse()));
        var controller = CreateController(auth);

        ActionResult<AuthResponse> result = await controller.Register(new RegisterRequest
        {
            Email = "seeker@example.com",
            Password = "Secret123456"
        }, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status201Created, status.StatusCode);
        Assert.Equal("jwt", ((AuthResponse)status.Value!).Token);
        Assert.True(auth.RegisterCalled);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var auth = new StubAuthService(register: (_, _) => throw new DuplicateEmailException());
        var controller = CreateController(auth);

        ActionResult<AuthResponse> result = await controller.Register(new RegisterRequest
        {
            Email = "taken@example.com",
            Password = "Secret123456"
        }, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var error = Assert.IsType<ApiErrorResponse>(conflict.Value);
        Assert.Equal("CONFLICT", error.Error.Code);
    }

    [Fact]
    public async Task Register_InvalidModel_DoesNotCallService_Returns400()
    {
        var auth = new StubAuthService(register: (_, _) => Task.FromResult(SampleResponse()));
        var controller = CreateController(auth);
        controller.ModelState.AddModelError("Email", "Required");

        ActionResult<AuthResponse> result = await controller.Register(new RegisterRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(auth.RegisterCalled);
    }

    [Fact]
    public async Task Login_Success_Returns200_WithBody()
    {
        var auth = new StubAuthService(login: (_, _) => Task.FromResult(SampleResponse()));
        var controller = CreateController(auth);

        ActionResult<AuthResponse> result = await controller.Login(new LoginRequest
        {
            Email = "seeker@example.com",
            Password = "Secret123456"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("jwt", ((AuthResponse)ok.Value!).Token);
        Assert.True(auth.LoginCalled);
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401_WithGenericMessage()
    {
        var auth = new StubAuthService(login: (_, _) => throw new InvalidCredentialsException());
        var controller = CreateController(auth);

        ActionResult<AuthResponse> result = await controller.Login(new LoginRequest
        {
            Email = "seeker@example.com",
            Password = "Wrong99999999"
        }, CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var error = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("UNAUTHORIZED", error.Error.Code);
        Assert.Equal("Невірний email або пароль.", error.Error.Message);
    }

    [Fact]
    public async Task Login_InvalidModel_DoesNotCallService_Returns400()
    {
        var auth = new StubAuthService(login: (_, _) => Task.FromResult(SampleResponse()));
        var controller = CreateController(auth);
        controller.ModelState.AddModelError("Password", "Required");

        ActionResult<AuthResponse> result = await controller.Login(new LoginRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(auth.LoginCalled);
    }

    private sealed class StubAuthService(
        Func<RegisterRequest, CancellationToken, Task<AuthResponse>>? register = null,
        Func<LoginRequest, CancellationToken, Task<AuthResponse>>? login = null)
        : IAuthenticationService
    {
        public bool RegisterCalled { get; private set; }
        public bool LoginCalled { get; private set; }

        public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
        {
            RegisterCalled = true;
            return (register ?? ((_, _) => throw new NotSupportedException()))(request, ct);
        }

        public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            LoginCalled = true;
            return (login ?? ((_, _) => throw new NotSupportedException()))(request, ct);
        }
    }
}
