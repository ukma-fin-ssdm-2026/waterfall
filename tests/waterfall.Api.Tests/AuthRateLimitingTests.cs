// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).
// AI: тести обмеження спроб входу (THR-01) і доступу до ендпоінтів, що змінюють спільні дані; перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Waterfall.Api.Controllers;
using Waterfall.Api.Extensions;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Auth;

namespace Waterfall.Api.Tests;

/// <summary>
/// Перебір паролів (THR-01): справжній AuthController і справжня політика обмеження через TestServer.
/// Плюс перевірка атрибутів доступу: каталог і геокеш змінює лише сервер, синхронізація — лише після входу.
/// </summary>
public sealed class AuthRateLimitingTests
{
    private static readonly LoginRequest WrongLogin = new() { Email = "user@example.com", Password = "wrong-password-1" };

    [Fact]
    public async Task Login_AfterFiveAttempts_Returns429WithRetryAfter()
    {
        await using LoginHost host = await LoginHost.StartAsync();

        for (int attempt = 0; attempt < AuthRateLimitingExtensions.AttemptsPerWindow; attempt++)
        {
            HttpResponseMessage allowed = await host.Client.PostAsJsonAsync("api/v1/auth/login", WrongLogin);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        HttpResponseMessage blocked = await host.Client.PostAsJsonAsync("api/v1/auth/login", WrongLogin);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.TryGetValues("Retry-After", out IEnumerable<string>? retryAfter));
        Assert.True(int.Parse(retryAfter.Single()) > 0);
        JsonElement error = await blocked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(AuthRateLimitingExtensions.ErrorCode, error.GetProperty("error").GetProperty("code").GetString());
        // Заблокована спроба до сервісу входу не доходить
        Assert.Equal(AuthRateLimitingExtensions.AttemptsPerWindow, host.Auth.LoginCalls);
    }

    [Fact]
    public async Task Login_SuccessfulAttemptsAlsoCount_SoPasswordCannotBeGuessedBetweenThem()
    {
        await using LoginHost host = await LoginHost.StartAsync();
        host.Auth.AcceptAll = true;

        for (int attempt = 0; attempt < AuthRateLimitingExtensions.AttemptsPerWindow; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync("api/v1/auth/login", WrongLogin)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await host.Client.PostAsJsonAsync("api/v1/auth/login", WrongLogin)).StatusCode);
    }

    [Fact]
    public void VacancyCatalog_HasNoManualCreateUpdateOrDelete()
    {
        // Каталог наповнює лише серверний парсер: з API змінювати його не можна нікому, навіть адміну
        string[] writeActions = typeof(VacanciesController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any(a => a is HttpPostAttribute or HttpPutAttribute or HttpPatchAttribute or HttpDeleteAttribute))
            .Select(m => m.Name)
            .ToArray();

        Assert.Equal([nameof(VacanciesController.Sync)], writeActions);
    }

    [Theory]
    [InlineData(typeof(VacanciesController), nameof(VacanciesController.Sync))]
    [InlineData(typeof(GeoController), nameof(GeoController.SyncCoordinates))]
    public void SyncEndpoints_RequireLogin(Type controller, string action)
    {
        MethodInfo method = Method(controller, action);
        Assert.NotEmpty(method.GetCustomAttributes<AuthorizeAttribute>());
        // [AllowAnonymous] на методі чи на класі переважив би [Authorize]
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public void GeoCoordinatesLookup_StaysPublic()
    {
        Assert.NotEmpty(Method(typeof(GeoController), nameof(GeoController.GetCoordinates)).GetCustomAttributes<AllowAnonymousAttribute>());
    }

    private static MethodInfo Method(Type controller, string name) =>
        controller.GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"{controller.Name}.{name} не знайдено");

    /// <summary>Мінімальний хост: справжні контролери API, справжня політика входу, підмінений сервіс автентифікації.</summary>
    private sealed class LoginHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private LoginHost(WebApplication app, HttpClient client, StubAuthenticationService auth)
        {
            _app = app;
            Client = client;
            Auth = auth;
        }

        public HttpClient Client { get; }

        public StubAuthenticationService Auth { get; }

        public static async Task<LoginHost> StartAsync()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ApplicationName = typeof(AuthRateLimitingTests).Assembly.GetName().Name
            });

            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddControllers()
                .ConfigureApplicationPartManager(parts =>
                {
                    parts.ApplicationParts.Clear();
                    parts.ApplicationParts.Add(new AssemblyPart(typeof(AuthController).Assembly));
                });
            builder.Services.AddAuthorization();
            StubAuthenticationService auth = new();
            builder.Services.AddSingleton<IAuthenticationService>(auth);
            builder.Services.AddWaterfallAuthRateLimiting();

            WebApplication app = builder.Build();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapControllers();

            await app.StartAsync();
            TestServer server = app.Services.GetService<TestServer>()
                ?? (TestServer)app.Services.GetRequiredService<IServer>();
            return new LoginHost(app, server.CreateClient(), auth);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class StubAuthenticationService : IAuthenticationService
    {
        public int LoginCalls { get; private set; }

        public bool AcceptAll { get; set; }

        public Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            LoginCalls++;
            return AcceptAll
                ? Task.FromResult(new AuthResponse { Token = "token", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15) })
                : throw new InvalidCredentialsException();
        }
    }
}
