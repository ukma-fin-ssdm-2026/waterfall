// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net.Http.Headers;
using Waterfall.Client.Interfaces;

namespace Waterfall.Client.Services;

/// <summary>
/// Додає Authorization: Bearer до кожного запиту, якщо користувач увійшов.
/// </summary>
public sealed class AuthHeaderHandler : DelegatingHandler
{
    private readonly IAuthTokenStore _tokens;

    public AuthHeaderHandler(IAuthTokenStore tokens)
    {
        _tokens = tokens;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? token = await _tokens.GetTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
