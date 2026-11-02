// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Primitives;
using Waterfall.Api.Models.Ai;
using Waterfall.Api.Models.Common;
using Waterfall.Api.Services;
using Waterfall.Services.Configuration;

namespace Waterfall.Api.Extensions;

/// <summary>
/// Rate limiting ШІ-ендпоінтів: ліміт повідомлень на користувача (THR-10).
/// Одна політика обслуговує і <c>/ai-consultant/messages</c> (#4), і <c>/ai-consultant/analyze</c> (#5),
/// тож користувач не може обійти ліміт, чергуючи ендпоінти.
/// </summary>
public static class AiConsultantRateLimitingExtensions
{
    /// <summary>
    /// Ім'я політики для атрибута <c>[EnableRateLimiting]</c>.
    /// </summary>
    public const string PolicyName = "ai-consultant-per-user";

    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>
    /// Реєструє налаштування <c>AiConsultant</c> та політику rate limiting для ШІ-ендпоінтів.
    /// Відмова повертається як 429 у єдиному форматі помилок із кодом <c>RATE_LIMITED</c>.
    /// </summary>
    /// <param name="services">Колекція сервісів DI-контейнера.</param>
    /// <param name="configuration">Конфігурація застосунку (секція AiConsultant).</param>
    public static IServiceCollection AddWaterfallAiConsultantRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AiConsultantOptions>(configuration.GetSection(AiConsultantOptions.SectionName));

        AiConsultantOptions options = new();
        configuration.GetSection(AiConsultantOptions.SectionName).Bind(options);

        // Некоректне або від'ємне значення в конфігурації не повинно робити ШІ недоступним для всіх
        int permitLimit = options.MessagesPerHour > 0
            ? options.MessagesPerHour
            : new AiConsultantOptions().MessagesPerHour;

        services.AddRateLimiter(rateLimiter =>
        {
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rateLimiter.OnRejected = async (context, cancellationToken) =>
            {
                HttpContext httpContext = context.HttpContext;
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                httpContext.Response.ContentType = "application/json; charset=utf-8";

                string requestId = httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out StringValues correlationId)
                    && !StringValues.IsNullOrEmpty(correlationId)
                        ? correlationId.ToString()
                        : httpContext.TraceIdentifier;

                ApiErrorResponse error = ApiErrorResponse.Create(
                    code: AiConsultantErrorCodes.RateLimited,
                    message: $"Перевищено ліміт запитів до ШІ-консультанта ({permitLimit} на годину). "
                        + "Скористайтеся звичайним пошуком за фільтрами каталогу.",
                    requestId: requestId);

                await httpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(error, SseEventWriter.SerializerOptions),
                    cancellationToken);
            };

            rateLimiter.AddPolicy(PolicyName, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    ResolvePartitionKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = Window,
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    /// <summary>
    /// Ключ розділу ліміту: ідентифікатор користувача з JWT (не з тіла запиту, THR-12).
    /// Для запитів без токена — адреса клієнта, щоб анонімний скрипт не мав спільного ліміту з користувачем.
    /// </summary>
    /// <param name="httpContext">Поточний HTTP-контекст.</param>
    public static string ResolvePartitionKey(HttpContext httpContext)
    {
        string? userId = httpContext.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? httpContext.User.FindFirstValue("userId");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId}";
        }

        return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
