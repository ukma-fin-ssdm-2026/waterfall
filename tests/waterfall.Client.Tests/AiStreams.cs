// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Runtime.CompilerServices;
using Waterfall.Client.Models.Ai;

namespace Waterfall.Client.Tests;

/// <summary>
/// Керовані SSE-потоки для тестів: тест сам вирішує, які події «прийдуть» і чи потік зависне.
/// </summary>
internal static class AiStreams
{
    public static async IAsyncEnumerable<AiStreamEvent> Of(params AiStreamEvent[] events)
    {
        foreach (AiStreamEvent streamEvent in events)
        {
            await Task.Yield();
            yield return streamEvent;
        }
    }

    /// <summary>
    /// Потік, який ніколи не віддає подій і завершується лише скасуванням (перевірка переривання).
    /// </summary>
    public static async IAsyncEnumerable<AiStreamEvent> WaitForCancel([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }

    /// <summary>
    /// Потік, який падає до першої події (помилка до початку SSE, як 503/429 від сервера).
    /// </summary>
    public static async IAsyncEnumerable<AiStreamEvent> Fail(Exception error)
    {
        await Task.Yield();
        if (error is not null)
        {
            throw error;
        }

        yield break;
    }
}
