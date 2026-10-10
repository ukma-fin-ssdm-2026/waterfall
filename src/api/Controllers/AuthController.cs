// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Api.Controllers;

/// <summary>
/// Реєстрація та вхід користувачів (випуск access-JWT).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
[AllowAnonymous]
[SwaggerTag("Реєстрація та вхід користувачів платформи Waterfall (JWT)")]
public class AuthController : ApiControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthenticationService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Зареєструвати нового користувача та видати access-JWT.
    /// </summary>
    /// <param name="request">Email, пароль (мін. 12 символів + цифра) та роль.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="201">Користувача зареєстровано, токен видано.</response>
    /// <response code="400">Помилка валідації вхідного запиту.</response>
    /// <response code="409">Email вже зареєстровано.</response>
    [HttpPost("register")]
    [SwaggerOperation(
        Summary = "Зареєструвати користувача",
        Description = "Створює обліковий запис (роль JobSeeker або Analyst) та повертає access-JWT на 15 хвилин.",
        OperationId = "Register",
        Tags = ["Auth"])]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ErrorBadRequest("Невалідні дані запиту реєстрації.");
        }

        _logger.LogInformation("Запит реєстрації: {Email}", request.Email);

        try
        {
            AuthResponse response = await _authService.RegisterAsync(request, ct);
            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (DuplicateEmailException)
        {
            return ErrorConflict("Користувач із таким email вже існує.");
        }
    }

    /// <summary>
    /// Увійти та отримати access-JWT.
    /// </summary>
    /// <param name="request">Email та пароль.</param>
    /// <param name="ct">Токен скасування операції.</param>
    /// <response code="200">Вхід успішний, токен видано.</response>
    /// <response code="400">Помилка валідації вхідного запиту.</response>
    /// <response code="401">Невірні облікові дані (однакове повідомлення для обох причин, THR-01).</response>
    [HttpPost("login")]
    [SwaggerOperation(
        Summary = "Увійти",
        Description = "Перевіряє облікові дані та повертає access-JWT на 15 хвилин.",
        OperationId = "Login",
        Tags = ["Auth"])]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ErrorBadRequest("Невалідні дані запиту входу.");
        }

        _logger.LogInformation("Запит входу: {Email}", request.Email);

        try
        {
            AuthResponse response = await _authService.LoginAsync(request, ct);
            return Ok(response);
        }
        catch (InvalidCredentialsException)
        {
            return ErrorUnauthorized("Невірний email або пароль.");
        }
    }
}
