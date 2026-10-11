// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Services;

namespace Waterfall.Client.Extensions;

/// <summary>
/// Реєстрація HTTP-шлюзу клієнта у DI-контейнері.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Реєструє <see cref="IHttpGatewayService"/> (типізований HttpClient з Bearer-обробником)
    /// та сховище токена <typeparamref name="TTokenStore"/>.
    /// </summary>
    /// <param name="services">Колекція сервісів.</param>
    /// <param name="baseAddress">Корінь API, напр. http://localhost:3000.</param>
    public static IServiceCollection AddWaterfallClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTokenStore>(
        this IServiceCollection services, Uri baseAddress)
        where TTokenStore : class, IAuthTokenStore
    {
        // Відносні шляхи ("api/v1/...") дописуються до BaseAddress лише якщо він закінчується на "/".
        Uri root = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/");

        services.AddSingleton<IAuthTokenStore, TTokenStore>();
        services.AddTransient<AuthHeaderHandler>();
        services.AddHttpClient<IHttpGatewayService, HttpGatewayService>(client => client.BaseAddress = root)
            .AddHttpMessageHandler<AuthHeaderHandler>();

        return services;
    }
}
