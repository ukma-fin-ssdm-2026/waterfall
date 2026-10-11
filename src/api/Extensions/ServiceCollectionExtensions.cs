// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Waterfall.Api.Options;
using Waterfall.Api.Services;
using Waterfall.Services.Interfaces;

namespace Waterfall.Api.Extensions;

/// <summary>
/// Реєстрація автентифікації JWT та auth-сервісів у DI-контейнері.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Додає схему JwtBearer, auth-сервіси та прив'язку JwtOptions.
    /// Кидає InvalidOperationException на старті, якщо секрети
    /// чи обов'язкові поля некоректні (fail-fast, THR-09).
    /// </summary>
    /// <param name="services">Колекція сервісів DI-контейнера.</param>
    /// <param name="configuration">Конфігурація застосунку (секція Jwt:*).</param>
    /// <param name="validateConfiguration">
    /// false лише для `--generate-openapi-and-exit`: ключ не впливає на форму YAML,
    /// тож регенерація docs не повинна вимагати секретів. Звичайні запуски завжди валідують.
    /// </param>
    public static IServiceCollection AddWaterfallAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateConfiguration = true)
    {
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        JwtOptions jwtOptions = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        if (validateConfiguration)
        {
            if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) || string.IsNullOrWhiteSpace(jwtOptions.Audience))
            {
                throw new InvalidOperationException("Відсутня конфігурація Jwt:Issuer або Jwt:Audience (див. appsettings.json).");
            }
            if (jwtOptions.Key.Length < 32)
            {
                throw new InvalidOperationException("Недійсний Jwt:Key: потрібно мінімум 32 символи (256 біт для HS256). Передайте через змінну середовища Jwt__Key або User Secrets.");
            }
            if (jwtOptions.ExpiresMinutes <= 0)
            {
                throw new InvalidOperationException("Недійсне Jwt:ExpiresMinutes: має бути додатнім числом.");
            }
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key))
                };
            });

        // Реалізації auth живуть в api (див. remarks в AuthenticationService): без стану — Singleton,
        // AuthenticationService залежить від scoped IUserRepository — Scoped.
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();

        return services;
    }
}
