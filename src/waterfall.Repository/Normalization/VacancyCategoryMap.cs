// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Repository.Normalization;

/// <summary>
/// Зводить різні назви категорій джерел («QA Automation» на Djinni, «QA» на DOU, «Legal», «Sales», «Finances»)
/// до єдиного широкого довідника. Довідник закритий: ШІ-консультант витягує з запиту лише ці значення,
/// а невідому категорію джерела заводимо в <see cref="Other"/>, щоб вакансія не випадала з каталогу.
/// </summary>
public static class VacancyCategoryMap
{
    /// <summary>Група для категорій, яких немає в довіднику.</summary>
    public const string Other = "Other";

    // Порядок правил важливий: вужчі групи стоять перед ширшими, інакше «Sales Engineer» став би розробником,
    // а «QA Manager» — менеджером. Ключі з «*» — початок слова («financ*» → finance, finances, financial),
    // ключі з пробілом або розділовим знаком — підрядок («business analyst», «.net», «c++»), решта — ціле слово.
    private static readonly (string Group, string[] Keywords)[] Rules =
    [
        ("Finance", ["financ*", "фінанс*", "bank*", "банк*", "audit*", "аудит*", "tax", "податк*", "account*", "бухгал*", "econom*", "економ*", "investment*", "інвестиц*", "insurance", "страхуван*", "payroll", "кредит*"]),
        ("Legal", ["legal", "law", "lawyer", "юрид*", "юрист*", "правозн*", "compliance", "нотаріу*", "патент*"]),
        ("HR", ["hr", "recruit*", "рекрут*", "talent*", "талант*", "human resources", "кадр*", "персонал*", "onboarding"]),
        ("Sales", ["sales", "продаж*", "business development", "bizdev", "partnership*", "партнер*", "account executive", "торгів*"]),
        ("Marketing", ["marketing*", "маркет*", "seo", "smm", "content*", "контент*", "advertis*", "реклам*", "brand*", "бренд*", "growth", "pr", "copywrit*", "копірайт*", "комунікац*"]),
        ("Design", ["design*", "дизайн*", "ux", "ui", "graphic*", "графічн*", "illustrat*", "motion*", "typograph*", "artist"]),
        ("Customer Support", ["customer support", "customer service", "call center", "contact center", "клієнтськ*"]),
        ("Administration", ["administration", "office manager", "офіс-менеджер", "assistant", "асистент*", "помічник*", "секретар*", "reception*"]),
        ("Education", ["educat*", "освіт*", "teacher", "викладач*", "trainer", "тренер*", "tutor", "навчан*"]),
        ("QA", ["qa", "test*", "тест*", "quality assurance", "sdet", "quality control", "qc"]),
        ("AI / ML", ["ai", "ml", "machine learning", "deep learning", "nlp", "computer vision", "llm", "штучн*", "нейромереж*", "data science"]),
        ("Product", ["product", "продукт*", "product manager", "product owner", "head of product"]),
        ("Project Management", ["pm", "project manager", "project management", "scrum*", "agile", "delivery manager", "program manager", "pmo", "проєкт*", "проект*"]),
        ("Business Analysis", ["ba", "business analyst", "business analysis", "system analyst", "systems analyst", "бізнес*"]),
        ("Data", ["data", "analytics", "analyst", "аналіт*", "bi", "big data", "database*", "dba", "etl", "sql", "statistic*", "статистик*"]),
        ("DevOps", ["devops", "sre", "cloud*", "хмар*", "infrastructure", "інфраструктур*", "kubernetes", "platform engineer", "sysadmin", "system administration", "administrator", "адміністратор*", "linux", "network*", "мереж*"]),
        ("Security", ["security", "cyber*", "кібер*", "безпек*", "pentest*", "penetration"]),
        ("Software Development", ["developer*", "develop*", "engineer*", "programmer*", "розроб*", "програміст*", "software", "architect*", "архітектор*", ".net", "dotnet", "java", "javascript", "typescript", "python", "front-end", "frontend", "back-end", "backend", "full-stack", "fullstack", "mobile", "ios", "android", "golang", "rust", "ruby", "php", "c++", "c#", "scala", "kotlin", "flutter", "react", "angular", "vue", "node.js", "embedded", "game", "unity", "unreal", "blockchain*", "sap", "erp", "crm", "1c", "technical writer"])
    ];

    /// <summary>Усі канонічні групи: широкі напрями IT і поза IT, остання — <see cref="Other"/>.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Rules.Select(rule => rule.Group), Other];

    /// <summary>
    /// Канонічна група для категорії джерела; порожня чи невідома категорія дає <see cref="Other"/>.
    /// </summary>
    public static string Resolve(string? category)
    {
        string text = (category ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return Other;
        }

        HashSet<string> words = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => part.Split(['-', '/', '+', '.', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        foreach ((string group, string[] keywords) in Rules)
        {
            if (keywords.Any(keyword => Matches(text, words, keyword)))
            {
                return group;
            }
        }

        return Other;
    }

    private static bool Matches(string text, HashSet<string> words, string keyword)
    {
        if (keyword.EndsWith('*'))
        {
            string stem = keyword[..^1];
            return words.Any(word => word.StartsWith(stem, StringComparison.Ordinal));
        }

        // «business analyst», «.net», «c++» не є окремими словами після розбиття на слова — шукаємо підрядком
        if (keyword.Contains(' ') || keyword.Any(character => !char.IsLetterOrDigit(character)))
        {
            return text.Contains(keyword, StringComparison.Ordinal);
        }

        return words.Contains(keyword);
    }
}
