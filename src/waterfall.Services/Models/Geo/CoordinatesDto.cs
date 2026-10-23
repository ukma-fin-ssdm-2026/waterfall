// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Geo;

/// <summary>
/// DTO географічних координат (широта й довгота).
/// </summary>
public record CoordinatesDto
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
