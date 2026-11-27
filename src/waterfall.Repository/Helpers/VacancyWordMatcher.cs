// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text.Json;

namespace Waterfall.Repository.Helpers;

/// <summary>
/// Узгоджене порівняння навичок і текстових токенів за межами слова для сервера та клієнта (#198).
/// Запобігає хибним збігам на кшталт «Java» → «JavaScript» або «C» → «C#»/«C++»,
/// зберігаючи підтримку Unicode (кирилиця) та технічних назв (C#, C++, .NET).
/// </summary>
public static class VacancyWordMatcher
{
    /// <summary>
    /// Перевіряє, чи містить <paramref name="text"/> слово/токен <paramref name="term"/> як окрему лексему
    /// без урахування регістру.
    /// </summary>
    public static bool ContainsWord(string? text, string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string needle = term.Trim();
        int searchStart = 0;

        while (searchStart <= text.Length - needle.Length)
        {
            int index = text.IndexOf(needle, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            if (HasValidBoundaries(text, index, needle.Length))
            {
                return true;
            }

            searchStart = index + 1;
        }

        return false;
    }

    /// <summary>
    /// Перевіряє окремий тег навички: точний збіг (без урахування регістру) або збіг за межами слова
    /// всередині складеного тега (наприклад, «C# / .NET»).
    /// </summary>
    public static bool MatchesSkillTag(string? tag, string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        string trimmedTag = tag.Trim();
        string trimmedTerm = term.Trim();
        return string.Equals(trimmedTag, trimmedTerm, StringComparison.OrdinalIgnoreCase)
            || ContainsWord(trimmedTag, trimmedTerm);
    }

    /// <summary>
    /// Перевіряє JSON-масив навичок <c>SkillsJson</c> на наявність тега <paramref name="term"/>.
    /// </summary>
    public static bool MatchesSkillsJson(string? skillsJson, string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(skillsJson))
        {
            return false;
        }

        try
        {
            List<string>? tags = JsonSerializer.Deserialize<List<string>>(skillsJson);
            return tags is not null && tags.Any(tag => MatchesSkillTag(tag, term));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Перевіряє збіг терміна навички у тегах <c>SkillsJson</c> або за межами слова в основних полях вакансії.
    /// </summary>
    public static bool MatchesVacancySkill(
        string? skillsJson,
        string? title,
        string? company,
        string? description,
        string? term) =>
        MatchesSkillsJson(skillsJson, term)
        || ContainsWord(title, term)
        || ContainsWord(company, term)
        || ContainsWord(description, term);

    private static bool HasValidBoundaries(string text, int start, int length)
    {
        if (start > 0)
        {
            char prev = text[start - 1];
            if (IsIdentifierContinuation(prev))
            {
                return false;
            }

            // Крапка між буквами/цифрами (наприклад, Node.js) з'єднує один ідентифікатор
            if (prev == '.' && start >= 2 && char.IsLetterOrDigit(text[start - 2]))
            {
                return false;
            }
        }

        int end = start + length;
        if (end < text.Length)
        {
            char next = text[end];
            if (IsIdentifierContinuation(next))
            {
                return false;
            }

            // Крапка перед буквою/цифрою (Node.js) — частина токена; крапка в кінці речення («Java.») — межа
            if (next == '.' && end + 1 < text.Length && char.IsLetterOrDigit(text[end + 1]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierContinuation(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '+';
}
