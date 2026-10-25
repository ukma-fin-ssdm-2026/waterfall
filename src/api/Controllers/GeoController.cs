// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Waterfall.Api.Models.Common;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Geo;

namespace Waterfall.Api.Controllers;

/// <summary>
/// Ендпоінти геокодування та отримання збережених координат із постійного кешу бази даних.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/geo")]
[Produces("application/json")]
[SwaggerTag("Геокодування та постійний геокеш адрес")]
public class GeoController : ApiControllerBase
{
    private readonly IGeoLocationService _geoService;

    public GeoController(IGeoLocationService geoService)
    {
        _geoService = geoService;
    }

    /// <summary>
    /// Отримати географічні координати адреси.
    /// </summary>
    [HttpGet("coordinates")]
    [SwaggerOperation(Summary = "Отримати координати адреси", Description = "Повертає широту й довготу з таблиці GeoLocationCache бази даних (або оновлює кеш із публічних геоданих).", OperationId = "GetCoordinates")]
    [ProducesResponseType(typeof(CoordinatesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CoordinatesDto>> GetCoordinates([FromQuery] string address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return ErrorBadRequest("Параметр адреси не може бути порожнім.");
        }

        (double Latitude, double Longitude)? coords = await _geoService.GetCoordinatesAsync(address, ct);
        if (coords == null)
        {
            return ErrorNotFound($"Координати для адреси '{address}' не знайдено.");
        }

        return Ok(new CoordinatesDto
        {
            Latitude = coords.Value.Latitude,
            Longitude = coords.Value.Longitude
        });
    }

    /// <summary>
    /// Двостороння синхронізація геокешу з клієнтом.
    /// </summary>
    [HttpPost("sync")]
    [SwaggerOperation(Summary = "Синхронізувати геокеш", Description = "Приймає знайдені офлайн координати з клієнта та повертає всі актуальні збережені координати з сервера.", OperationId = "SyncCoordinates")]
    [ProducesResponseType(typeof(GeoSyncResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeoSyncResponseDto>> SyncCoordinates([FromBody] GeoSyncRequestDto request, CancellationToken ct)
    {
        GeoSyncResponseDto response = await _geoService.SyncCacheAsync(request, ct);
        return Ok(response);
    }
}
