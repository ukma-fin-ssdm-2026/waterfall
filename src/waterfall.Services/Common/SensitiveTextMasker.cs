// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.RegularExpressions;

namespace Waterfall.Services.Common;

/// <summary>
/// Маскування персональних даних у тексті, який надсилається зовнішньому LLM (THR-06).
/// Користувач може написати в чат власний email чи телефон, і вони не повинні покидати наш периметр
/// навіть у складі промпту. У власній БД текст зберігається без змін — маскування відбувається лише
/// на межі виклику LLM.
/// </summary>
public static partial class SensitiveTextMasker
{
    /// <summary>
    /// Заміна для знайденої електронної пошти.
    /// </summary>
    public const string EmailPlaceholder = "[email приховано]";

    /// <summary>
    /// Заміна для знайденого номера телефону.
    /// </summary>
    public const string PhonePlaceholder = "[телефон приховано]";

    private const int MinPhoneDigits = 10;
    private const int MaxPhoneDigits = 13;

    /// <summary>
    /// Замаскувати email та номери телефонів у тексті повідомлення.
    /// </summary>
    /// <param name="text">Текст повідомлення користувача.</param>
    /// <returns>Текст із замінами; для null/порожнього рядка — порожній рядок.</returns>
    public static string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Спершу email: у ньому теж трапляються цифри, і після заміни телефонний шаблон уже не спрацює
        string masked = EmailRegex().Replace(text, EmailPlaceholder);

        return PhoneRegex().Replace(
            masked,
            match => LooksLikePhone(match.Value) ? PhonePlaceholder : match.Value);
    }

    /// <summary>
    /// Шаблон телефонного кандидата: 9+ символів із цифр, пробілів, дужок і дефісів.
    /// Сам по собі він ловить і діапазони зарплат, тому кількість цифр перевіряється окремо.
    /// </summary>
    [GeneratedRegex(@"(?<![\d+])\+?\d[\d\s()\-]{7,}\d(?![\d])", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    /// <summary>
    /// Шаблон електронної пошти.
    /// </summary>
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    private static bool LooksLikePhone(string candidate)
    {
        int digits = 0;
        foreach (char symbol in candidate)
        {
            if (char.IsAsciiDigit(symbol))
            {
                digits++;
            }
        }

        // «1000-2000 USD» — це зарплата (8 цифр), а не телефон; реальні номери мають 10–13 цифр
        return digits is >= MinPhoneDigits and <= MaxPhoneDigits;
    }
}
