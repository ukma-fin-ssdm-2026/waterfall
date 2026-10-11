// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using Waterfall.Client.Models.Common;

namespace Waterfall.Client;

/// <summary>
/// Єдиний тип помилки HttpGatewayService: відповідь не 2xx, недоступний сервер або некоректна відповідь.
/// Message завжди придатне для показу користувачу.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode? statusCode, ApiError? error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Error = error;
    }

    /// <summary>
    /// HTTP-статус; null, якщо відповіді не було (сервер недоступний, тайм-аут).
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// Розібраний конверт помилки сервера, якщо тіло відповіді його містило.
    /// </summary>
    public ApiError? Error { get; }
}
