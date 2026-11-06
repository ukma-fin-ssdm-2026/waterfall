// AI: Тести перевірки здоров'я сервісу (Health Checks) з ін'єкцією збоїв (Lab 9 Bonus 2, §3.4)
// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Health;
using Waterfall.Repository.Data;

namespace Waterfall.Reliability.Tests;

/// <summary>
/// Тести здоров'я сервісу: перевіряють статус критичних залежностей (БД, зовнішній API),
/// коректність повернення статусів Healthy / Unhealthy та поведінку при ін'єкції збою залежності.
/// </summary>
public class HealthCheckTests
{
    [Fact]
    public async Task DatabaseHealthCheck_ReturnsHealthy_WhenDatabaseConnectionIsAvailable()
    {
        // Arrange - валідне SQLite in-memory з'єднання
        using SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();

        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(connection)
            .Options;

        using WaterfallDbContext dbContext = new(options);
        await dbContext.Database.EnsureCreatedAsync();

        DatabaseHealthCheck healthCheck = new(dbContext, NullLogger<DatabaseHealthCheck>.Instance);
        HealthCheckContext context = new();

        // Act
        HealthCheckResult result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("активне та відповідає", result.Description);
    }

    [Fact]
    public async Task DatabaseHealthCheck_ReturnsUnhealthy_WhenDatabaseConnectionFails_FaultInjection()
    {
        // Arrange - ін'єкція збою: з'єднання з базою примусово закрите і зіпсоване
        using SqliteConnection closedConnection = new("DataSource=invalid_path_fail.db");
        // Не викликаємо OpenAsync, база недоступна

        DbContextOptions<WaterfallDbContext> options = new DbContextOptionsBuilder<WaterfallDbContext>()
            .UseSqlite(closedConnection)
            .Options;

        using WaterfallDbContext dbContext = new(options);
        // Закриваємо або утилізуємо підключення
        closedConnection.Dispose();

        DatabaseHealthCheck healthCheck = new(dbContext, NullLogger<DatabaseHealthCheck>.Instance);
        HealthCheckContext context = new();

        // Act - виконання перевірки при недоступній СУБД
        HealthCheckResult result = await healthCheck.CheckHealthAsync(context);

        // Assert - система чітко сигналізує про нездоровий стан (Unhealthy)
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Description);
        Assert.Contains("недоступна", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PhotonGeocodingHealthCheck_ReturnsHealthy_WhenPhotonApiIsReachable()
    {
        // Arrange
        FakeHttpHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK));
        using HttpClient httpClient = new(handler);

        PhotonGeocodingHealthCheck healthCheck = new(httpClient, NullLogger<PhotonGeocodingHealthCheck>.Instance);
        HealthCheckContext context = new();

        // Act
        HealthCheckResult result = await healthCheck.CheckHealthAsync(context);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("доступний", result.Description);
    }

    [Fact]
    public async Task PhotonGeocodingHealthCheck_ReturnsUnhealthy_WhenPhotonApiFails_FaultInjection()
    {
        // Arrange - ін'єкція збою: мережевий виняток при зверненні до Photon
        FakeHttpHandler handler = new(new HttpRequestException("DNS server failure"));
        using HttpClient httpClient = new(handler);

        PhotonGeocodingHealthCheck healthCheck = new(httpClient, NullLogger<PhotonGeocodingHealthCheck>.Instance);
        HealthCheckContext context = new();

        // Act
        HealthCheckResult result = await healthCheck.CheckHealthAsync(context);

        // Assert - статус Unhealthy при відмові зовнішнього API
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("недоступний", result.Description);
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public FakeHttpHandler(HttpResponseMessage response) => _response = response;
        public FakeHttpHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_exception != null)
            {
                throw _exception;
            }
            return Task.FromResult(_response ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}

