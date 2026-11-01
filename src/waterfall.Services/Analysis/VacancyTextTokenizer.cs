// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Analysis;

/// <summary>
/// Простий детермінований токенізатор тексту вакансії для профілю переваг і оцінки релевантності
/// (FR-010): нижній регістр, розбиття за всіма не-літеро-цифровими символами, відкидання
/// стоп-слів української й англійської мов та загальних слів оголошень («вакансія», «досвід»,
/// «requirements»). Словник не залежить від IT-тематики, тож працює й для бухгалтерів, юристів
/// чи продажів.
/// </summary>
public static class VacancyTextTokenizer
{
    /// <summary>Мінімальна довжина токена, щоб він не був шумом («та», «і», «of»).</summary>
    public const int MinTokenLength = 3;

    /// <summary>
    /// Короткі доменні слова, які лишаємо попри довжину: «1С» у бухгалтерії, «QA»/«Go»/«HR» в IT.
    /// </summary>
    private static readonly HashSet<string> ShortTokens = new(StringComparer.Ordinal)
    {
        "1c", "1с", "3d", "ai", "ba", "bi", "go", "hr", "ml", "pm", "qa", "seo", "sre", "ui", "ux"
    };

    /// <summary>
    /// Стоп-слова: службові слова української й англійської мов та загальні слова оголошень,
    /// які трапляються в кожній вакансії й не описують переваг користувача.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // Українські службові слова
        "але", "без", "був", "була", "були", "бути", "вам", "вас", "ваш", "ваша", "ваші", "вже",
        "від", "він", "вона", "вони", "все", "всі", "для", "до", "його", "її", "їх", "із", "яка",
        "яке", "який", "яких", "як", "між", "може", "можна", "на", "над", "нам", "нас", "наш",
        "не", "ні", "під", "після", "по", "при", "про", "так", "також", "там", "та", "той", "то",
        "у", "це", "цей", "ця", "ці", "через", "що", "щоб", "якщо", "або", "за", "з", "і", "й",
        // Загальні слова оголошень (українською)
        "вакансія", "вакансії", "вакансію", "вимоги", "вимог", "вимога", "досвід", "досвіду",
        "досвідом", "знання", "знань", "знаннями", "зарплата", "зарплатня", "зайнятість", "кандидат",
        "кандидата", "компанія", "компанії", "компанію", "команда", "команді", "команду", "місто",
        "навички", "навичок", "навичками", "обов'язки", "обов'язків", "опис", "посада", "посади",
        "потрібен", "потрібна", "потрібні", "потрібно", "проєкт", "проєкту", "проект", "проекту",
        "резюме", "робота", "роботи", "роботі", "роботу", "роботодавець", "графік", "умови", "умов",
        "повна", "повний", "гнучкий", "можливості", "можливість", "серед", "лише", "буде", "має",
        "мають", "якщо", "кожен", "кожна", "інше", "інші", "тощо", "років", "роки", "рік", "міс",
        "місяців", "місяці", "днів", "тижнів",
        // Англійські службові слова та слова оголошень
        "about", "after", "all", "also", "and", "any", "are", "been", "but", "can", "company",
        "could", "description", "developer", "does", "each", "engineer", "etc", "experience", "for", "from", "has", "have",
        "here", "how", "into", "its", "job", "jobs", "more", "must", "not", "our", "out", "over",
        "own", "plus", "required", "requirements", "responsibilities", "role", "should", "skills",
        "such", "team", "than", "that", "the", "their", "them", "then", "there", "these", "they",
        "this", "those", "through", "under", "vacancy", "was", "were", "what", "when", "where",
        "which", "while", "who", "why", "will", "with", "work", "working", "would", "you", "your"
    };

    /// <summary>
    /// Розбити текст на унікальні токени в порядку появи.
    /// </summary>
    /// <param name="text">Довільний текст (назва, опис, теги); null і порожній рядок дають порожній список.</param>
    /// <returns>Нижній регістр, без стоп-слів, без суто числових токенів.</returns>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        List<string> tokens = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        StringBuilder current = new();

        foreach (char character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
                continue;
            }

            Flush(current, tokens, seen);
        }

        Flush(current, tokens, seen);
        return tokens;
    }

    /// <summary>
    /// Токени вакансії: назва + опис + теги (еквівалент <c>SearchText</c> без назви компанії,
    /// щоб бренд роботодавця не ставав «ключовою навичкою»).
    /// </summary>
    /// <param name="vacancy">Вакансія каталогу.</param>
    public static IReadOnlyList<string> TokenizeVacancy(VacancyDto vacancy)
    {
        ArgumentNullException.ThrowIfNull(vacancy);

        List<string> tokens = [.. Tokenize(vacancy.Title), .. Tokenize(vacancy.Description)];
        foreach (string skill in vacancy.Skills)
        {
            tokens.AddRange(Tokenize(skill));
        }

        return tokens
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Віддати накопичений токен, якщо він інформативний, і очистити буфер.
    /// </summary>
    private static void Flush(StringBuilder current, List<string> tokens, HashSet<string> seen)
    {
        if (current.Length == 0)
        {
            return;
        }

        string token = current.ToString();
        current.Clear();

        if (IsMeaningful(token) && seen.Add(token))
        {
            tokens.Add(token);
        }
    }

    private static bool IsMeaningful(string token)
    {
        if (StopWords.Contains(token)
            || (token.Length < MinTokenLength && !ShortTokens.Contains(token)))
        {
            return false;
        }

        // «2024», «100» — не навичка; «1с», «3d» лишаємо, бо це реальні назви технологій
        return token.Any(char.IsLetter);
    }
}
