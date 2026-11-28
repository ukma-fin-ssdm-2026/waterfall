// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: обмеження спроб входу з однієї адреси (THR-01); перевірено: тести AuthRateLimitingTests, самоперевірка автора (§8)

using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Primitives;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Services;

namespace Waterfall.Api.Extensions;

/// <summary>
/// Захист входу від перебору паролів (THR-01): не більше <see cref="AttemptsPerWindow"/> спроб
/// за <see cref="Window"/> з однієї IP-адреси. Відмова — 429 у єдиному форматі помилок із кодом
/// <see cref="ErrorCode"/> і заголовком Retry-After.
/// </summary>
public static class AuthRateLimitingExtensions
{
    /// <summary>Ім'я політики для атрибута <c>[EnableRateLimiting]</c>.</summary>
    public const string PolicyName = "auth-login-per-ip";

    /// <summary>Код помилки у відповіді 429.</summary>
    public const string ErrorCode = "TOO_MANY_LOGIN_ATTEMPTS";

    /// <summary>Скільки спроб входу дозволено з однієї адреси за вікно.</summary>
    public const int AttemptsPerWindow = 5;

    /// <summary>Тривалість вікна підрахунку спроб.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Реєструє політику обмеження спроб входу.</summary>
    public static IServiceCollection AddWaterfallAuthRateLimiting(this IServiceCollection services)
    {
        // Власний OnRejected політики замінює загальний (той пише повідомлення про ліміт ШІ-консультанта)
        services.AddRateLimiter(rateLimiter => rateLimiter.AddPolicy(PolicyName, new LoginRateLimiterPolicy()));
        return services;
    }

    private sealed class LoginRateLimiterPolicy : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = WriteRejectionAsync;

        // Користувач ще не увійшов, тож ключ — лише адреса клієнта
        public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
            RateLimitPartition.GetFixedWindowLimiter(
                $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = AttemptsPerWindow,
                    Window = Window,
                    QueueLimit = 0,
                    AutoReplenishment = true
                });

        private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
        {
            HttpContext httpContext = context.HttpContext;
            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            httpContext.Response.ContentType = "application/json; charset=utf-8";

            TimeSpan retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan wait) ? wait : Window;
            int retrySeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            httpContext.Response.Headers.RetryAfter = retrySeconds.ToString(CultureInfo.InvariantCulture);

            string requestId = httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out StringValues correlationId)
                && !StringValues.IsNullOrEmpty(correlationId)
                    ? correlationId.ToString()
                    : httpContext.TraceIdentifier;

            ApiErrorResponse error = ApiErrorResponse.Create(
                code: ErrorCode,
                message: $"Забагато спроб входу. Спробуйте знову через {retrySeconds} с.",
                requestId: requestId);

            await httpContext.Response.WriteAsync(JsonSerializer.Serialize(error, SseEventWriter.SerializerOptions), cancellationToken);
        }
    }
}
