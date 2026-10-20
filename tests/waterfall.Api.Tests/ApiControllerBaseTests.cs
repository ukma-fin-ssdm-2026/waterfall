// AI: модульні тести базового контролера (ApiControllerBase, трасування, JWT claims, ApiErrorResponse, Lab 06); перевірено: запуск dotnet test, самоперевірка автора (Денис Сидоренко, §8)

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Waterfall.Api.Controllers;
using Waterfall.Api.Models.Common;

namespace Waterfall.Api.Tests;

/// <summary>
/// Тестовий клас-нащадок ApiControllerBase для ізольованого тестування базової функціональності контролерів.
/// </summary>
public sealed class TestableApiController : ApiControllerBase
{
    public string PublicRequestId => RequestId;
    public Guid? PublicCurrentUserId => CurrentUserId;

    public NotFoundObjectResult TestErrorNotFound(string message) => ErrorNotFound(message);
    public BadRequestObjectResult TestErrorBadRequest(string message, List<ValidationErrorDetail>? details = null) => ErrorBadRequest(message, details);
    public UnauthorizedObjectResult TestErrorUnauthorized(string message) => ErrorUnauthorized(message);
    public ConflictObjectResult TestErrorConflict(string message) => ErrorConflict(message);
}

/// <summary>
/// Модульні тести для ApiControllerBase:
/// - Трасування запитів (X-Correlation-Id -> X-Request-Id -> TraceIdentifier)
/// - Парсинг ідентифікатора користувача з JWT claims (sub / userId)
/// - Уніфіковані відповіді помилок (ApiErrorResponse)
/// </summary>
public sealed class ApiControllerBaseTests
{
    private readonly TestableApiController _controller = new();

    [Theory]
    [InlineData("corr-123", "req-456", "corr-123")]
    [InlineData(null, "req-456", "req-456")]
    [InlineData(null, null, "trace-system-abc")]
    public void RequestId_ResolvesHeaderPrecedenceCorrectly(
        string? correlationId,
        string? requestIdHeader,
        string expectedResult)
    {
        // Arrange
        DefaultHttpContext context = new() { TraceIdentifier = "trace-system-abc" };
        if (correlationId != null) context.Request.Headers["X-Correlation-Id"] = correlationId;
        if (requestIdHeader != null) context.Request.Headers["X-Request-Id"] = requestIdHeader;
        _controller.ControllerContext = new ControllerContext { HttpContext = context };

        // Act
        string actualRequestId = _controller.PublicRequestId;

        // Assert
        Assert.Equal(expectedResult, actualRequestId);
    }

    [Fact]
    public void CurrentUserId_ParsesJwtSubClaim_WhenPresent()
    {
        // Arrange
        Guid expectedId = Guid.NewGuid();
        DefaultHttpContext context = new();
        Claim[] claims = [new Claim(JwtRegisteredClaimNames.Sub, expectedId.ToString())];
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        _controller.ControllerContext = new ControllerContext { HttpContext = context };

        // Act
        Guid? actualId = _controller.PublicCurrentUserId;

        // Assert
        Assert.NotNull(actualId);
        Assert.Equal(expectedId, actualId.Value);
    }

    [Fact]
    public void CurrentUserId_FallsBackToUserIdClaim_WhenSubMissing()
    {
        // Arrange
        Guid expectedId = Guid.NewGuid();
        DefaultHttpContext context = new();
        Claim[] claims = [new Claim("userId", expectedId.ToString())];
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        _controller.ControllerContext = new ControllerContext { HttpContext = context };

        // Act
        Guid? actualId = _controller.PublicCurrentUserId;

        // Assert
        Assert.NotNull(actualId);
        Assert.Equal(expectedId, actualId.Value);
    }

    [Fact]
    public void CurrentUserId_ReturnsNull_WhenUnauthenticatedOrInvalidGuid()
    {
        // Arrange
        DefaultHttpContext anonymousContext = new();
        _controller.ControllerContext = new ControllerContext { HttpContext = anonymousContext };

        // Act & Assert
        Assert.Null(_controller.PublicCurrentUserId);

        // Invalid GUID format
        DefaultHttpContext invalidContext = new();
        invalidContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, "not-a-guid")], "Bearer"));
        _controller.ControllerContext = new ControllerContext { HttpContext = invalidContext };

        Assert.Null(_controller.PublicCurrentUserId);
    }

    [Fact]
    public void ErrorHelpers_ProduceConsistentApiErrorResponseStructures()
    {
        // Arrange
        DefaultHttpContext context = new() { TraceIdentifier = "trace-err-test" };
        _controller.ControllerContext = new ControllerContext { HttpContext = context };

        // Act 1: NotFound
        NotFoundObjectResult notFound = _controller.TestErrorNotFound("Resource not found");
        ApiErrorResponse err404 = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("NOT_FOUND", err404.Error.Code);
        Assert.Equal("Resource not found", err404.Error.Message);
        Assert.Equal("trace-err-test", err404.Error.RequestId);

        // Act 2: BadRequest
        List<ValidationErrorDetail> details = [new ValidationErrorDetail { Field = "Email", Message = "Invalid format" }];
        BadRequestObjectResult badReq = _controller.TestErrorBadRequest("Validation failed", details);
        ApiErrorResponse err400 = Assert.IsType<ApiErrorResponse>(badReq.Value);
        Assert.Equal("VALIDATION_ERROR", err400.Error.Code);
        Assert.Single(err400.Error.Details);
        Assert.Equal("Email", err400.Error.Details[0].Field);

        // Act 3: Unauthorized
        UnauthorizedObjectResult unauthorized = _controller.TestErrorUnauthorized("Invalid credentials");
        ApiErrorResponse err401 = Assert.IsType<ApiErrorResponse>(unauthorized.Value);
        Assert.Equal("UNAUTHORIZED", err401.Error.Code);

        // Act 4: Conflict
        ConflictObjectResult conflict = _controller.TestErrorConflict("Resource state conflict");
        ApiErrorResponse err409 = Assert.IsType<ApiErrorResponse>(conflict.Value);
        Assert.Equal("CONFLICT", err409.Error.Code);
    }
}
