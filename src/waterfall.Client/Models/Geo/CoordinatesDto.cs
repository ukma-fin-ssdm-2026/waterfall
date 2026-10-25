// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Client.Models.Geo;

/// <summary>
/// Клієнтська DTO координат адреси з сервера.
/// </summary>
public record CoordinatesDto
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
