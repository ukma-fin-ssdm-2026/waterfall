// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Models.Geo;

/// <summary>
/// Елемент синхронізації геокешу між клієнтом та сервером.
/// </summary>
public record GeoSyncItemDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Address { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Запит двосторонньої синхронізації геокешу: передача знайдених офлайн координат із клієнта на сервер.
/// </summary>
public record GeoSyncRequestDto
{
    public List<GeoSyncItemDto> ClientItems { get; init; } = new();
}

/// <summary>
/// Відповідь двосторонньої синхронізації геокешу: повертає актуальні записи бази сервера.
/// </summary>
public record GeoSyncResponseDto
{
    public List<GeoSyncItemDto> ServerItems { get; init; } = new();
}
