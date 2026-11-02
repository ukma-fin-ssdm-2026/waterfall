// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Globalization;
using System.Text;
using Waterfall.Repository.Entities;

namespace Waterfall.Services.Models.Ai;

/// <summary>
/// Узагальнений профіль переваг шукача роботи, побудований ЛИШЕ з його збережених вакансій
/// (FR-010, US-004): частоти широких категорій і форматів роботи, типовий досвід, діапазон
/// зарплати та ваги ключових слів. Жодних персональних даних (id, email, ім'я) тут немає (THR-06),
/// тож профіль можна безпечно передавати в промпт LLM.
/// </summary>
public sealed record PreferenceProfile
{
    /// <summary>Порожній профіль: у користувача немає збережених вакансій, оцінка релевантності не визначається.</summary>
    public static PreferenceProfile Empty { get; } = new();

    /// <summary>Кількість збережених вакансій, на яких побудовано профіль (basis_count).</summary>
    public int BasisCount { get; init; }

    /// <summary>
    /// Частота широких категорій (<see cref="Models.Vacancies.CategoryGroups"/>): 0..1,
    /// частка збережених вакансій із цією категорією.
    /// </summary>
    public IReadOnlyDictionary<string, double> CategoryWeights { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Частота форматів роботи: 0..1, частка збережених вакансій, де формат дозволений.
    /// Вакансія може дозволяти кілька форматів, тому сума ваг може перевищувати 1.
    /// </summary>
    public IReadOnlyDictionary<WorkFormat, double> WorkFormatWeights { get; init; } =
        new Dictionary<WorkFormat, double>();

    /// <summary>Медіана мінімально необхідного досвіду збережених вакансій у місяцях; null — досвід ніде не вказано.</summary>
    public int? MedianExperienceMonths { get; init; }

    /// <summary>Типовий діапазон зарплати в одній валюті; null — зарплата ніде не вказана.</summary>
    public SalaryPreference? Salary { get; init; }

    /// <summary>
    /// Ваги ключових слів: 0..1, нормована частота токена в назвах, описах і тегах збережених
    /// вакансій (тег важить удвічі). Упорядковані за спаданням ваги, не більше
    /// <see cref="Analysis.PreferenceProfileBuilder.MaxKeywords"/> слів.
    /// </summary>
    public IReadOnlyDictionary<string, double> KeywordWeights { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    /// <summary>Чи є на чому будувати оцінку (хоч одна збережена вакансія).</summary>
    public bool HasBasis => BasisCount > 0;

    /// <summary>
    /// Стислий опис профілю для промпту LLM: лише узагальнені переваги, без персональних даних (THR-06).
    /// </summary>
    /// <returns>Один рядок українською; для порожнього профілю — пояснення, що збережених вакансій немає.</returns>
    public string ToPromptSummary()
    {
        if (!HasBasis)
        {
            return "У користувача немає збережених вакансій, тому профіль переваг не визначено.";
        }

        StringBuilder builder = new();
        builder.Append(CultureInfo.InvariantCulture, $"Збережених вакансій: {BasisCount}");

        if (CategoryWeights.Count > 0)
        {
            builder.Append("; категорії: ")
                .Append(string.Join(", ", CategoryWeights.Select(pair => $"{pair.Key} ({FormatShare(pair.Value)})")));
        }

        if (WorkFormatWeights.Count > 0)
        {
            builder.Append("; формати: ")
                .Append(string.Join(", ", WorkFormatWeights.Select(pair => $"{DescribeWorkFormat(pair.Key)} ({FormatShare(pair.Value)})")));
        }

        if (MedianExperienceMonths is int months)
        {
            builder.Append(CultureInfo.InvariantCulture, $"; типовий досвід: {months} міс.");
        }

        if (Salary is SalaryPreference salary)
        {
            builder.Append(CultureInfo.InvariantCulture, $"; зарплата: {DescribeSalary(salary)}");
        }

        if (KeywordWeights.Count > 0)
        {
            builder.Append("; ключові слова: ")
                .Append(string.Join(", ", KeywordWeights.Keys));
        }

        return builder.ToString();
    }

    private static string FormatShare(double weight)
        => (weight * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string DescribeSalary(SalaryPreference salary)
    {
        string min = salary.MedianMin?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";
        string max = salary.MedianMax?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—";
        return $"{min}–{max} {salary.Currency}";
    }

    private static string DescribeWorkFormat(WorkFormat format) => format switch
    {
        WorkFormat.Remote => "віддалено",
        WorkFormat.Hybrid => "гібрид",
        WorkFormat.Office => "офіс",
        _ => "не вказано"
    };
}

/// <summary>
/// Типовий діапазон зарплати зі збережених вакансій в одній валюті: медіани нижніх і верхніх меж.
/// </summary>
/// <param name="Currency">Валюта (верхній регістр); вакансії в інших валютах в узагальнення не потрапляють.</param>
/// <param name="MedianMin">Медіана мінімальних зарплат, якщо хоч одна вакансія її вказала.</param>
/// <param name="MedianMax">Медіана максимальних зарплат, якщо хоч одна вакансія її вказала.</param>
public sealed record SalaryPreference(string Currency, decimal? MedianMin, decimal? MedianMax)
{
    /// <summary>Чи є хоч одна межа діапазону.</summary>
    public bool HasRange => MedianMin is not null || MedianMax is not null;
}
