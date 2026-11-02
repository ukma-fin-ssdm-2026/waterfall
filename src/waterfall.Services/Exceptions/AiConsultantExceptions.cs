// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Exceptions;

/// <summary>
/// Діалог не знайдено або він належить іншому користувачу.
/// Навмисно один виняток для обох випадків: клієнт не повинен дізнатися, що чужий діалог існує (THR-12).
/// Контролер мапить на 404.
/// </summary>
public sealed class ChatSessionNotFoundException : Exception
{
    public ChatSessionNotFoundException(Guid sessionId)
        : base($"Діалог з ШІ-консультантом '{sessionId}' не знайдено.")
    {
        SessionId = sessionId;
    }

    /// <summary>
    /// Ідентифікатор ненайденого діалогу (для журналу; у відповідь клієнту не потрапляє).
    /// </summary>
    public Guid SessionId { get; }
}
