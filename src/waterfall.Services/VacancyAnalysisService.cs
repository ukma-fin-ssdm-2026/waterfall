// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Waterfall.Repository.Entities;
using Waterfall.Services.Analysis;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Llm;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.SavedVacancies;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services;

/// <summary>
/// Аналіз вибраних вакансій (FR-010, US-005): профіль переваг будується зі збережених вакансій
/// користувача, релевантність оцінюється детерміновано (без LLM), а LLM лише пояснює вимоги,
/// червоні прапорці та відповідність профілю. У промпт потрапляють тільки поля запитаних вакансій,
/// оцінки релевантності й узагальнений профіль — без id, email чи імені користувача (THR-06).
/// </summary>
public sealed class VacancyAnalysisService : IVacancyAnalysisService
{
    /// <summary>Скільки вакансій максимум можна аналізувати за один запит (контракт #5).</summary>
    public const int MaxVacancies = 3;

    /// <summary>Скільки символів опису вакансії потрапляє в промпт.</summary>
    public const int PromptDescriptionLength = 400;

    /// <summary>
    /// Системний промпт аналізу: розбір лише наведених вакансій, українською, з посиланням на score.
    /// </summary>
    private const string AnalysisSystemPrompt =
        """
        Ти — аналітик вакансій платформи пошуку роботи Waterfall. Відповідай українською мовою.

        Для КОЖНОЇ вакансії зі списку в запиті дай стислий розбір:
        1. Ключові вимоги: окремо «обов'язкові» (must-have) і «бажані» (nice-to-have).
        2. Червоні прапорці та неясні місця (наприклад, не вказана зарплата, розмиті обов'язки, суперечливі вимоги, надто широкий стек).
        3. Коротке пояснення відповідності профілю кандидата з посиланням на наведену оцінку релевантності (score 0..100).

        Правила:
        1. Використовуй ЛИШЕ вакансії зі списку. Не вигадуй вакансій, компаній, вимог, зарплат чи посилань і не згадуй вакансії, яких немає у списку.
        2. Спирайся лише на наведені поля вакансій і узагальнений профіль; якщо даних бракує — прямо скажи, чого саме не вистачає.
        3. Якщо оцінки релевантності немає (у користувача немає збережених вакансій) — не вигадуй score, аналізуй лише вимоги вакансії.
        4. Не виводь персональні дані (ім'я, email, телефон, ідентифікатори користувача) і не згадуй внутрішні ідентифікатори вакансій.
        5. Будь стислим: до 5 речень на вакансію, без вступів, повторів і загальних порад.
        6. Пиши звичайним текстом без Markdown (без **, #, таблиць): клієнт показує текст як є. Для структури використовуй нові рядки й тире.
        """;

    private readonly ISavedVacancyService _savedVacancyService;
    private readonly IVacancyService _vacancyService;
    private readonly ILlmClientProvider _llmClientProvider;
    private readonly ILogger<VacancyAnalysisService> _logger;

    public VacancyAnalysisService(
        ISavedVacancyService savedVacancyService,
        IVacancyService vacancyService,
        ILlmClientProvider llmClientProvider,
        ILogger<VacancyAnalysisService> logger)
    {
        _savedVacancyService = savedVacancyService;
        _vacancyService = vacancyService;
        _llmClientProvider = llmClientProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AIAnalysisTurn> PrepareAsync(
        Guid userId,
        IReadOnlyList<Guid> vacancyIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(vacancyIds);

        List<Guid> ids = vacancyIds.Distinct().ToList();
        if (ids.Count is < 1 or > MaxVacancies)
        {
            throw new ArgumentException(
                $"Аналізувати можна від 1 до {MaxVacancies} вакансій.",
                nameof(vacancyIds));
        }

        // Профіль — лише зі збережених вакансій цього користувача (чуже «Обране» недоступне, THR-12).
        // Архівні збереження теж враховуємо: вони описують переваги не гірше за активні
        IReadOnlyList<SavedVacancyDto> saved = await _savedVacancyService.GetAllAsync(userId, onlyActive: false, ct);
        PreferenceProfile profile = PreferenceProfileBuilder.Build(
            saved
                .Where(item => item.Vacancy is not null)
                .Select(item => item.Vacancy!));

        List<VacancyDto> vacancies = [];
        foreach (Guid id in ids)
        {
            // Відсутня вакансія — 404 до першого байта SSE: частковий аналіз не має сенсу
            VacancyDto vacancy = await _vacancyService.GetByIdAsync(id, ct, userId)
                ?? throw new VacancyNotFoundException(id);
            vacancies.Add(vacancy);
        }

        List<VacancyRelevance> relevance = vacancies
            .Select(vacancy => RelevanceScorer.Score(profile, vacancy))
            .ToList();

        _logger.LogInformation(
            "Аналіз вакансій: {VacancyCount} вакансій, базових збережених {BasisCount}, оцінки {Scores}",
            vacancies.Count,
            profile.BasisCount,
            string.Join(", ", relevance.Select(item => item.Score?.ToString(CultureInfo.InvariantCulture) ?? "null")));

        return new AIAnalysisTurn
        {
            Vacancies = vacancies,
            Relevance = relevance,
            Prompt = BuildPrompt(profile, vacancies, relevance)
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AIAnalysisStreamItem> StreamAnalysisAsync(
        AIAnalysisTurn turn,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        IChatClient client = _llmClientProvider.GetClient(LlmRole.Analyze);

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(turn.Prompt, cancellationToken: ct))
        {
            string? chunk = update.Text;
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            yield return new AIAnalysisDelta(chunk);
        }

        yield return new AIAnalysisCompleted();
    }

    /// <summary>
    /// Промпт аналізу: узагальнений профіль (без персональних даних) і рівно запитані вакансії
    /// з їхніми оцінками релевантності (FUNC-001, THR-06).
    /// </summary>
    private static IReadOnlyList<ChatMessage> BuildPrompt(
        PreferenceProfile profile,
        IReadOnlyList<VacancyDto> vacancies,
        IReadOnlyList<VacancyRelevance> relevance)
    {
        StringBuilder builder = new();
        builder.AppendLine("Узагальнений профіль кандидата (за збереженими вакансіями):");
        builder.AppendLine(profile.ToPromptSummary());
        builder.AppendLine();
        builder.AppendLine($"Вакансії для аналізу ({vacancies.Count}):");

        for (int index = 0; index < vacancies.Count; index++)
        {
            builder.AppendLine(DescribeVacancy(index + 1, vacancies[index], relevance[index]));
        }

        return
        [
            new ChatMessage(ChatRole.System, AnalysisSystemPrompt),
            new ChatMessage(ChatRole.User, builder.ToString())
        ];
    }

    private static string DescribeVacancy(int index, VacancyDto vacancy, VacancyRelevance relevance)
    {
        string category = string.IsNullOrWhiteSpace(vacancy.CategoryGroup)
            ? (string.IsNullOrWhiteSpace(vacancy.Category) ? "не вказано" : vacancy.Category)
            : $"{vacancy.CategoryGroup} ({vacancy.Category})";

        return $"{index}. Назва: {vacancy.Title}; компанія: {vacancy.Company}; категорія: {category}; "
            + $"формат: {DescribeWorkFormat(vacancy.WorkFormat)}; досвід: {DescribeExperience(vacancy)}; "
            + $"зарплата: {DescribeSalary(vacancy)}; локація: {vacancy.Location}; "
            + $"навички: {DescribeSkills(vacancy.Skills)}; опис: {Truncate(vacancy.Description, PromptDescriptionLength)}. "
            + DescribeRelevance(relevance);
    }

    private static string DescribeRelevance(VacancyRelevance relevance)
    {
        if (relevance.Score is not int score)
        {
            return "Релевантність профілю: не визначена (у користувача немає збережених вакансій).";
        }

        string matched = relevance.Matched.Count == 0
            ? "збігів немає"
            : string.Join(", ", relevance.Matched);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Релевантність профілю: {score}/100 (збіги: {matched}; база: {relevance.BasisCount} збережених вакансій).");
    }

    private static string DescribeExperience(VacancyDto vacancy)
    {
        if (vacancy.ExperienceMonths is int months)
        {
            return $"{months} міс.";
        }

        return string.IsNullOrWhiteSpace(vacancy.Experience) ? "не вказано" : vacancy.Experience;
    }

    private static string DescribeSalary(VacancyDto vacancy)
    {
        string currency = string.IsNullOrWhiteSpace(vacancy.Currency) ? "USD" : vacancy.Currency;
        string? min = vacancy.SalaryMin?.ToString("0.##", CultureInfo.InvariantCulture);
        string? max = vacancy.SalaryMax?.ToString("0.##", CultureInfo.InvariantCulture);

        return (min, max) switch
        {
            (null, null) => "не вказана",
            (not null, null) => $"від {min} {currency}",
            (null, not null) => $"до {max} {currency}",
            _ when min == max => $"{min} {currency}",
            _ => $"{min}–{max} {currency}"
        };
    }

    private static string DescribeWorkFormat(WorkFormat formats)
    {
        List<string> parts = [];
        if (formats.HasFlag(WorkFormat.Remote))
        {
            parts.Add("віддалено");
        }

        if (formats.HasFlag(WorkFormat.Hybrid))
        {
            parts.Add("гібрид");
        }

        if (formats.HasFlag(WorkFormat.Office))
        {
            parts.Add("офіс");
        }

        return parts.Count == 0 ? "не вказано" : string.Join(", ", parts);
    }

    private static string DescribeSkills(IReadOnlyList<string> skills)
    {
        return skills.Count == 0 ? "не вказано" : string.Join(", ", skills);
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "—";
        }

        string normalized = text.Trim().ReplaceLineEndings(" ");
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].TrimEnd() + "…";
    }
}
