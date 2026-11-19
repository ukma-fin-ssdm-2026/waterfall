// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Analytics;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Api.Controllers;

/// <summary>
/// Ринкова аналітика вакансій для ролі «Аналітик» (FR-003, FR-008, US-006, US-007).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/analytics")]
[Produces("application/json")]
[SwaggerTag("Агрегована ринкова аналітика вакансій")]
public class AnalyticsController : ApiControllerBase
{
    private readonly IAnalyticsService _analyticsService;

    public AnalyticsController(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [HttpGet]
    [SwaggerOperation(
        Summary = "Отримати ринкову аналітику вакансій",
        Description = "Повертає динаміку публікацій, топ-технології та середню зарплату за обраними категорією й періодом. Доступно лише аналітикам.",
        OperationId = "GetMarketAnalytics",
        Tags = ["Analytics"])]
    [ProducesResponseType(typeof(MarketAnalyticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MarketAnalyticsDto>> Get(
        [FromQuery] string? category = null,
        [FromQuery] int months = 3,
        CancellationToken ct = default)
    {
        if (CurrentUserId is null)
        {
            return ErrorUnauthorized("Користувач не авторизований.");
        }

        string? roleValue = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
        if (!Enum.TryParse(roleValue, ignoreCase: true, out UserRole role) || role != UserRole.Analyst)
        {
            return ErrorForbidden("Розділ доступний виключно користувачам із роллю «Роботодавець / Аналітик ринку».");
        }

        MarketAnalyticsDto result = await _analyticsService.GetMarketAnalyticsAsync(category, months, ct);
        return Ok(result);
    }
}
