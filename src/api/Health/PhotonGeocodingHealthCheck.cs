// AI: Health Check для перевірки доступності зовнішнього сервісу геокодування (Lab 9 Bonus, §3.4)
// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Waterfall.Api.Health;

/// <summary>
/// Перевірка доступності зовнішньої залежності геокодування Photon API.
/// Виконує легкозважений HTTP HEAD/GET запит із коротким таймаутом (2 сек).
/// </summary>
public sealed class PhotonGeocodingHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PhotonGeocodingHealthCheck> _logger;

    public PhotonGeocodingHealthCheck(HttpClient httpClient, ILogger<PhotonGeocodingHealthCheck> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://photon.komoot.io/api/?q=Kyiv&limit=1");
            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("Зовнішній Photon API доступний.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return HealthCheckResult.Degraded("Photon API повідомляє про Rate-Limit (429), але доступний.");
            }

            _logger.LogWarning("Photon API повернув код статусу {Status}", response.StatusCode);
            return HealthCheckResult.Unhealthy($"Photon API повернув код відмови: {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Photon API недоступний під час перевірки здоров'я.");
            return HealthCheckResult.Unhealthy($"Зовнішній сервіс геокодування недоступний: {ex.Message}", ex);
        }
    }
}

