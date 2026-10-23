// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Profile;

namespace Waterfall.Api.Controllers;

/// <summary>
/// Керування профілем та персональними даними користувача.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/profile")]
[Produces("application/json")]
[SwaggerTag("Керування персональними даними та локаційними налаштуваннями профілю")]
public class ProfileController : ApiControllerBase
{
    private readonly IUserProfileService _profileService;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(IUserProfileService profileService, ILogger<ProfileController> logger)
    {
        _profileService = profileService;
        _logger = logger;
    }

    /// <summary>
    /// Отримати збережену адресу користувача для розрахунку відстаней.
    /// </summary>
    [HttpGet("address")]
    [SwaggerOperation(Summary = "Отримати адресу користувача", Description = "Повертає збережену домашню/поточну адресу для розрахунку відстані до офісів.", OperationId = "GetProfileAddress")]
    [ProducesResponseType(typeof(UserAddressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserAddressDto>> GetAddress(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        string? address = await _profileService.GetUserAddressAsync(userId, ct);
        return Ok(new UserAddressDto { Address = address });
    }

    /// <summary>
    /// Зберегти або оновити адресу користувача в базі даних.
    /// </summary>
    [HttpPut("address")]
    [SwaggerOperation(Summary = "Зберегти адресу користувача", Description = "Зберігає або оновлює адресу в таблиці UserPersonalData.", OperationId = "SaveProfileAddress")]
    [ProducesResponseType(typeof(UserAddressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserAddressDto>> SaveAddress([FromBody] UserAddressDto request, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        if (string.IsNullOrWhiteSpace(request?.Address))
        {
            return ErrorBadRequest("Адреса не може бути порожньою.");
        }

        await _profileService.SaveUserAddressAsync(userId, request.Address, ct);
        _logger.LogInformation("Оновлено адресу для користувача {UserId}.", userId);

        return Ok(new UserAddressDto { Address = request.Address.Trim() });
    }
}
