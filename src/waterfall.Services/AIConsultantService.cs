// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Waterfall.Repository.Entities;
using Waterfall.Repository.Interfaces;
using Waterfall.Services.Common;
using Waterfall.Services.Configuration;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Llm;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Common;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services;

/// <summary>
/// ШІ-консультант (FR-002, FR-009, US-005): перетворює репліку природною мовою на структурований
/// фільтр каталогу через LLM, шукає вакансії в БД, за потреби послаблює критерії (критерій 3 US-005)
/// і стрімить коротку відповідь, склад, якої обмежений лише знайденими вакансіями (FUNC-001).
/// Персональні дані користувача до LLM не потрапляють: id/email/ім'я не передаються, email і
/// телефони в тексті маскуються (THR-06).
/// </summary>
public sealed class AIConsultantService : IAIConsultantService
{
    /// <summary>Скільки найближчих вакансій показуємо, коли точних збігів немає (US-005, критерій 3).</summary>
    private const int RelaxationVacancyLimit = 3;

    /// <summary>Скільки критеріїв максимум знімаємо під час послаблення.</summary>
    private const int MaxRelaxedKeys = 3;

    /// <summary>Максимальна довжина текстового поля фільтра (збігається з обмеженнями каталогу).</summary>
    private const int MaxFilterTextLength = 100;

    /// <summary>Максимальна кількість навичок у фільтрі.</summary>
    private const int MaxSkills = 10;

    /// <summary>Максимальна довжина однієї навички.</summary>
    private const int MaxSkillLength = 50;

    /// <summary>Верхня межа досвіду в місяцях (50 років): усе більше — явна помилка моделі.</summary>
    private const int MaxExperienceMonthsBound = 600;

    /// <summary>Межа зарплати, яку приймає каталог.</summary>
    private const decimal MaxSalaryBound = 1_000_000m;

    /// <summary>Скільки символів опису вакансії потрапляє в промпт (короткий опис, не весь текст).</summary>
    private const int PromptDescriptionLength = 300;

    /// <summary>Максимальна довжина повідомлення користувача.</summary>
    private const int MaxUserMessageLength = 1000;

    /// <summary>
    /// Ключі фільтра в сталому порядку: він визначає і порядок послаблення, і детермінованість відповіді.
    /// </summary>
    private static readonly string[] FilterKeys =
    [
        "search",
        "category_group",
        "work_format",
        "max_experience_months",
        "min_salary",
        "max_salary",
        "location",
        "skills"
    ];

    /// <summary>Дозволені значення формату роботи (закритий словник).</summary>
    private static readonly string[] WorkFormatValues = ["remote", "hybrid", "office"];

    /// <summary>
    /// Системний промпт відповіді: жорстко забороняє вигадувати вакансії (FUNC-001) і вимагає
    /// згадати наближеність результатів, коли точних збігів не було.
    /// </summary>
    private const string SummarySystemPrompt =
        """
        Ти — ШІ-консультант платформи пошуку роботи Waterfall. Відповідай українською мовою, коротко й по суті.

        Правила:
        1. Використовуй ЛИШЕ вакансії зі списку, доданого до запиту користувача. Не вигадуй вакансій, компаній, зарплат чи посилань і не згадуй вакансії, яких немає у списку.
        2. Не перелічуй усі вакансії поспіль: 2–4 речення про те, що знайдено і чим воно підходить під запит.
        3. Якщо список порожній — скажи, що збігів немає, і запропонуй, який критерій послабити.
        4. Якщо в запиті є попередження про наближені результати — обов'язково згадай, що вакансії відповідають запиту не на 100%.
        5. Не виводь персональні дані, email чи телефони, навіть якщо вони є в історії діалогу.
        6. Не згадуй у тексті відповіді внутрішні ідентифікатори вакансій.
        """;

    private readonly IChatSessionRepository _chatRepository;
    private readonly IVacancyService _vacancyService;
    private readonly ILlmClientProvider _llmClientProvider;
    private readonly IOptions<AiConsultantOptions> _options;
    private readonly ILogger<AIConsultantService> _logger;

    public AIConsultantService(
        IChatSessionRepository chatRepository,
        IVacancyService vacancyService,
        ILlmClientProvider llmClientProvider,
        IOptions<AiConsultantOptions> options,
        ILogger<AIConsultantService> logger)
    {
        _chatRepository = chatRepository;
        _vacancyService = vacancyService;
        _llmClientProvider = llmClientProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AIConsultantTurn> PrepareAsync(
        Guid userId,
        Guid? sessionId,
        string text,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        string userText = text.Trim();
        if (userText.Length > MaxUserMessageLength)
        {
            throw new ArgumentException(
                $"Текст запиту не може бути довшим за {MaxUserMessageLength} символів.",
                nameof(text));
        }

        ChatSessionDB? session = null;
        if (sessionId is Guid existingSessionId)
        {
            // Чужий діалог для нас не існує (THR-12)
            session = await _chatRepository.GetSessionAsync(existingSessionId, userId, ct)
                ?? throw new ChatSessionNotFoundException(existingSessionId);
        }

        AppliedFilter previousFilter = AppliedFilter.FromJson(session?.ContextFilterJson);
        IReadOnlyList<ChatMessageDB> history = session is null
            ? Array.Empty<ChatMessageDB>()
            : await _chatRepository.GetRecentMessagesAsync(session.Id, userId, ResolveHistoryLimit(), ct);

        // Найризикованіший крок (зовнішній LLM) виконується до створення діалогу й до першого байта SSE:
        // відмова провайдера дає клієнту звичайний 503, а не обірваний потік (REL-003)
        AppliedFilter filter = await ExtractFilterAsync(previousFilter, history, userText, ct);

        if (session is null)
        {
            // Порожніх діалогів не буває (data-model.md, інваріант 5): створюємо лише разом із першим запитом
            session = await _chatRepository.CreateSessionAsync(userId, filter.ToJson(), ct);
        }
        else if (!await _chatRepository.UpdateContextFilterAsync(session.Id, userId, filter.ToJson(), ct))
        {
            throw new ChatSessionNotFoundException(session.Id);
        }

        if (await _chatRepository.AddUserMessageAsync(session.Id, userId, userText, ct) is null)
        {
            throw new ChatSessionNotFoundException(session.Id);
        }

        SearchOutcome outcome = await SearchVacanciesAsync(filter, ct);

        _logger.LogInformation(
            "ШІ-консультант: діалог {SessionId}, точних збігів {Exact}, показано {VacancyCount} вакансій, знято критеріїв {RelaxedCount}",
            session.Id,
            outcome.Exact,
            outcome.Vacancies.Count,
            outcome.Relaxed.Count);

        return new AIConsultantTurn
        {
            SessionId = session.Id,
            UserId = userId,
            Filter = filter,
            Vacancies = outcome.Vacancies,
            Exact = outcome.Exact,
            Relaxed = outcome.Relaxed,
            Disclaimer = outcome.Disclaimer,
            SummaryPrompt = BuildSummaryPrompt(history, userText, filter, outcome)
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AIConsultantStreamItem> StreamAnswerAsync(
        AIConsultantTurn turn,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turn);

        IChatClient client = _llmClientProvider.GetClient(LlmRole.Chat);
        StringBuilder answer = new();

        await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(turn.SummaryPrompt, cancellationToken: ct))
        {
            string? chunk = update.Text;
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            answer.Append(chunk);
            yield return new AIConsultantDelta(chunk);
        }

        // Останній елемент потоку фіксує рівно ті вакансії, які вже пішли клієнту (FUNC-001)
        long? messageId = await _chatRepository.AddAssistantMessageAsync(
            turn.SessionId,
            turn.UserId,
            answer.ToString(),
            turn.Vacancies.Select(vacancy => vacancy.Id).ToList(),
            ct);

        if (messageId is null)
        {
            throw new InvalidOperationException(
                "Діалог було видалено під час формування відповіді ШІ — зберегти відповідь неможливо.");
        }

        yield return new AIConsultantCompleted(messageId.Value);
    }

    /// <inheritdoc />
    public Task<bool> DeleteSessionAsync(Guid sessionId, Guid userId, CancellationToken ct = default)
        => _chatRepository.DeleteSessionAsync(sessionId, userId, ct);

    /// <summary>
    /// Витягнути фільтр із нового повідомлення з урахуванням попереднього фільтра та історії діалогу.
    /// </summary>
    private async Task<AppliedFilter> ExtractFilterAsync(
        AppliedFilter previousFilter,
        IReadOnlyList<ChatMessageDB> history,
        string userText,
        CancellationToken ct)
    {
        IChatClient client = _llmClientProvider.GetClient(LlmRole.Extract);

        List<ChatMessage> messages = [new ChatMessage(ChatRole.System, BuildExtractionPrompt(previousFilter))];
        foreach (ChatMessageDB message in history)
        {
            messages.Add(new ChatMessage(
                message.Author == ChatMessageAuthor.User ? ChatRole.User : ChatRole.Assistant,
                SensitiveTextMasker.Mask(message.Content)));
        }

        messages.Add(new ChatMessage(ChatRole.User, SensitiveTextMasker.Mask(userText)));

        FilterExtractionResult? extraction;
        try
        {
            ChatResponse<FilterExtractionResult> response =
                await client.GetResponseAsync<FilterExtractionResult>(messages, cancellationToken: ct);

            // Result розбирає JSON ліниво: невалідна відповідь провайдера падає саме тут
            extraction = response.Result;
        }
        catch (LlmUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Провайдер відповів, але структурований результат не розпізнано. Для користувача це та сама
            // відмова ШІ (звичайний пошук лишається доступним), тож віддаємо 503, а не 500 (REL-003)
            _logger.LogWarning(exception, "Витяг фільтра ШІ-консультанта завершився помилкою");
            throw new LlmUnavailableException(
                LlmFailureKind.ServerError,
                "Не вдалося розпізнати структурований фільтр у відповіді LLM.",
                exception);
        }

        if (extraction is null)
        {
            _logger.LogWarning("LLM не повернула структурований фільтр; лишаємо попередній фільтр діалогу");
            return MergeWithPrevious(previousFilter, new FilterExtractionResult());
        }

        return MergeWithPrevious(previousFilter, extraction);
    }

    /// <summary>
    /// Злити новий фільтр із попереднім: уточнення змінює лише ті поля, про які йдеться в повідомленні,
    /// решта лишається з попереднього кроку діалогу (US-005, критерій 2).
    /// </summary>
    private static AppliedFilter MergeWithPrevious(AppliedFilter previous, FilterExtractionResult extraction)
    {
        AppliedFilter merged = previous.Clone();

        if (!string.IsNullOrWhiteSpace(extraction.Search))
        {
            merged.Search = extraction.Search;
        }

        if (!string.IsNullOrWhiteSpace(extraction.CategoryGroup))
        {
            merged.CategoryGroup = extraction.CategoryGroup;
        }

        if (extraction.WorkFormat is { Length: > 0 })
        {
            merged.WorkFormat = extraction.WorkFormat;
        }

        if (extraction.MaxExperienceMonths is not null)
        {
            merged.MaxExperienceMonths = extraction.MaxExperienceMonths;
        }

        if (extraction.MinSalary is not null)
        {
            merged.MinSalary = extraction.MinSalary;
        }

        if (extraction.MaxSalary is not null)
        {
            merged.MaxSalary = extraction.MaxSalary;
        }

        if (!string.IsNullOrWhiteSpace(extraction.Location))
        {
            merged.Location = extraction.Location;
        }

        if (extraction.Skills is { Length: > 0 })
        {
            merged.Skills = extraction.Skills;
        }

        foreach (string key in extraction.Clear ?? [])
        {
            ClearKey(merged, key);
        }

        return Sanitize(merged);
    }

    /// <summary>
    /// Прибрати критерій фільтра за його ключем (невалідні ключі ігноруються).
    /// </summary>
    private static void ClearKey(AppliedFilter filter, string? key)
    {
        switch (key?.Trim().ToLowerInvariant())
        {
            case "search":
                filter.Search = null;
                break;
            case "category_group":
                filter.CategoryGroup = null;
                break;
            case "work_format":
                filter.WorkFormat = null;
                break;
            case "max_experience_months":
                filter.MaxExperienceMonths = null;
                break;
            case "min_salary":
                filter.MinSalary = null;
                break;
            case "max_salary":
                filter.MaxSalary = null;
                break;
            case "location":
                filter.Location = null;
                break;
            case "skills":
                filter.Skills = null;
                break;
        }
    }

    /// <summary>
    /// Серверна валідація фільтра: модель може повернути категорію поза словником, від'ємну зарплату
    /// чи невідомий формат роботи — такі значення не повинні потрапити ні в БД, ні в каталог.
    /// </summary>
    private static AppliedFilter Sanitize(AppliedFilter filter)
    {
        AppliedFilter sanitized = new()
        {
            Search = NormalizeText(filter.Search, MaxFilterTextLength),
            CategoryGroup = NormalizeCategoryGroup(filter.CategoryGroup),
            WorkFormat = NormalizeWorkFormat(filter.WorkFormat),
            MaxExperienceMonths = NormalizeExperience(filter.MaxExperienceMonths),
            MinSalary = NormalizeSalary(filter.MinSalary),
            MaxSalary = NormalizeSalary(filter.MaxSalary),
            Location = NormalizeText(filter.Location, MaxFilterTextLength),
            Skills = NormalizeSkills(filter.Skills)
        };

        if (sanitized.MinSalary is decimal minSalary
            && sanitized.MaxSalary is decimal maxSalary
            && minSalary > maxSalary)
        {
            // Модель інколи плутає межі: мінімум — те, що користувач просив явно, верхню межу відкидаємо
            sanitized.MaxSalary = null;
        }

        return sanitized;
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    private static string? NormalizeCategoryGroup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        return CategoryGroups.All.FirstOrDefault(
            category => string.Equals(category, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string>? NormalizeWorkFormat(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        HashSet<string> normalized = [];
        foreach (string value in values)
        {
            string token = value?.Trim().ToLowerInvariant() ?? string.Empty;
            if (WorkFormatValues.Contains(token, StringComparer.Ordinal))
            {
                normalized.Add(token);
            }
        }

        return normalized.Count == 0
            ? null
            : WorkFormatValues.Where(normalized.Contains).ToArray();
    }

    private static int? NormalizeExperience(int? months)
    {
        if (months is null or < 0)
        {
            return null;
        }

        return Math.Min(months.Value, MaxExperienceMonthsBound);
    }

    private static decimal? NormalizeSalary(decimal? salary)
    {
        if (salary is null or < 0)
        {
            return null;
        }

        return Math.Min(salary.Value, MaxSalaryBound);
    }

    private static IReadOnlyList<string>? NormalizeSkills(IReadOnlyList<string>? skills)
    {
        if (skills is null || skills.Count == 0)
        {
            return null;
        }

        List<string> normalized = [];
        foreach (string skill in skills)
        {
            string trimmed = skill?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                continue;
            }

            trimmed = trimmed.Length <= MaxSkillLength ? trimmed : trimmed[..MaxSkillLength].TrimEnd();
            if (!normalized.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(trimmed);
            }

            if (normalized.Count == MaxSkills)
            {
                break;
            }
        }

        return normalized.Count == 0 ? null : normalized;
    }

    /// <summary>
    /// Пошук за фільтром; якщо збігів немає — послаблення критеріїв (US-005, критерій 3).
    /// </summary>
    private async Task<SearchOutcome> SearchVacanciesAsync(AppliedFilter filter, CancellationToken ct)
    {
        int pageSize = Math.Clamp(_options.Value.MaxVacancies, 1, 20);
        PagedResult<VacancyDto> exact = await _vacancyService.GetAllAsync(BuildParameters(filter, pageSize), ct);

        if (exact.Items.Count > 0 || filter.IsEmpty)
        {
            // Порожній каталог без критеріїв — не привід вигадувати «послаблення»
            return new SearchOutcome(exact.Items, true, Array.Empty<string>(), null);
        }

        List<(string Key, int Count)> probes = [];
        foreach (string key in FilterKeys)
        {
            if (!HasCriteria(filter, key))
            {
                continue;
            }

            // Пробний запит: скільки вакансій дасть зняття саме цього критерію
            PagedResult<VacancyDto> probe = await _vacancyService.GetAllAsync(
                BuildParameters(filter, RelaxationVacancyLimit, [key]),
                ct);
            probes.Add((key, probe.TotalCount));
        }

        if (probes.Count == 0)
        {
            return new SearchOutcome(exact.Items, true, Array.Empty<string>(), null);
        }

        // Найбільше «звузив» той критерій, зняття якого відкриває найбільше вакансій;
        // за рівної кількості — порядок ключів у FilterKeys (детермінованість)
        List<string> order = probes
            .OrderByDescending(probe => probe.Count)
            .ThenBy(probe => Array.IndexOf(FilterKeys, probe.Key))
            .Select(probe => probe.Key)
            .ToList();

        List<string> relaxed = [];
        IReadOnlyList<VacancyDto> nearest = Array.Empty<VacancyDto>();

        // Знімаємо критерії по одному, доки не з'являться результати (не більше трьох)
        foreach (string key in order.Take(MaxRelaxedKeys))
        {
            relaxed.Add(key);
            PagedResult<VacancyDto> relaxedSearch = await _vacancyService.GetAllAsync(
                BuildParameters(filter, RelaxationVacancyLimit, relaxed),
                ct);

            if (relaxedSearch.Items.Count > 0)
            {
                nearest = relaxedSearch.Items.Take(RelaxationVacancyLimit).ToList();
                break;
            }
        }

        if (nearest.Count == 0)
        {
            return new SearchOutcome(exact.Items, false, relaxed, BuildNoResultsDisclaimer(relaxed));
        }

        return new SearchOutcome(nearest, false, relaxed, BuildDisclaimer(relaxed));
    }

    /// <summary>
    /// Побудувати параметри каталогу з фільтра діалогу, пропускаючи зняті критерії.
    /// </summary>
    private static VacancyFilterParameters BuildParameters(
        AppliedFilter filter,
        int pageSize,
        IReadOnlyCollection<string>? skipped = null)
    {
        HashSet<string> skippedKeys = skipped is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(skipped, StringComparer.Ordinal);

        return new VacancyFilterParameters
        {
            Page = 1,
            PageSize = pageSize,
            OnlyActive = true,
            Search = skippedKeys.Contains("search") ? null : filter.Search,
            CategoryGroup = skippedKeys.Contains("category_group") ? null : filter.CategoryGroup,
            WorkFormat = skippedKeys.Contains("work_format") ? null : ToWorkFormatFlags(filter.WorkFormat),
            MaxExperienceMonths = skippedKeys.Contains("max_experience_months") ? null : filter.MaxExperienceMonths,
            MinSalary = skippedKeys.Contains("min_salary") ? null : filter.MinSalary,
            MaxSalary = skippedKeys.Contains("max_salary") ? null : filter.MaxSalary,
            Location = skippedKeys.Contains("location") ? null : filter.Location,
            Skills = skippedKeys.Contains("skills") ? null : filter.Skills
        };
    }

    /// <summary>
    /// Перетворити формати роботи з фільтра на прапорці каталогу (об'єднання «будь-який із»).
    /// </summary>
    private static WorkFormat? ToWorkFormatFlags(IReadOnlyList<string>? formats)
    {
        if (formats is null || formats.Count == 0)
        {
            return null;
        }

        WorkFormat flags = WorkFormat.None;
        foreach (string format in formats)
        {
            flags |= format.ToLowerInvariant() switch
            {
                "remote" => WorkFormat.Remote,
                "hybrid" => WorkFormat.Hybrid,
                "office" => WorkFormat.Office,
                _ => WorkFormat.None
            };
        }

        return flags == WorkFormat.None ? null : flags;
    }

    private static bool HasCriteria(AppliedFilter filter, string key) => key switch
    {
        "search" => !string.IsNullOrWhiteSpace(filter.Search),
        "category_group" => !string.IsNullOrWhiteSpace(filter.CategoryGroup),
        "work_format" => filter.WorkFormat is { Count: > 0 },
        "max_experience_months" => filter.MaxExperienceMonths is not null,
        "min_salary" => filter.MinSalary is not null,
        "max_salary" => filter.MaxSalary is not null,
        "location" => !string.IsNullOrWhiteSpace(filter.Location),
        "skills" => filter.Skills is { Count: > 0 },
        _ => false
    };

    /// <summary>
    /// Промпт відповіді: історія діалогу (з замаскованими ПД) + поточний запит + рівно ті вакансії,
    /// які пішли клієнту (FUNC-001).
    /// </summary>
    private static IReadOnlyList<ChatMessage> BuildSummaryPrompt(
        IReadOnlyList<ChatMessageDB> history,
        string userText,
        AppliedFilter filter,
        SearchOutcome outcome)
    {
        List<ChatMessage> messages = [new ChatMessage(ChatRole.System, SummarySystemPrompt)];

        foreach (ChatMessageDB message in history)
        {
            messages.Add(new ChatMessage(
                message.Author == ChatMessageAuthor.User ? ChatRole.User : ChatRole.Assistant,
                SensitiveTextMasker.Mask(message.Content)));
        }

        messages.Add(new ChatMessage(ChatRole.User, BuildSummaryUserPrompt(userText, filter, outcome)));
        return messages;
    }

    private static string BuildSummaryUserPrompt(string userText, AppliedFilter filter, SearchOutcome outcome)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Запит користувача: {SensitiveTextMasker.Mask(userText)}");
        builder.AppendLine($"Застосований фільтр: {DescribeFilter(filter)}");

        if (!string.IsNullOrWhiteSpace(outcome.Disclaimer))
        {
            builder.AppendLine($"Увага: {outcome.Disclaimer}");
        }

        builder.AppendLine();

        if (outcome.Vacancies.Count == 0)
        {
            builder.AppendLine("Вакансії з каталогу: 0 (точних збігів немає).");
            return builder.ToString();
        }

        builder.AppendLine($"Вакансії з каталогу ({outcome.Vacancies.Count}):");
        int index = 1;
        foreach (VacancyDto vacancy in outcome.Vacancies)
        {
            builder.AppendLine(DescribeVacancy(index++, vacancy));
        }

        return builder.ToString();
    }

    private static string DescribeVacancy(int index, VacancyDto vacancy)
    {
        string experience = vacancy.ExperienceMonths is int months
            ? $"{months} міс."
            : string.IsNullOrWhiteSpace(vacancy.Experience) ? "не вказано" : vacancy.Experience;

        return $"{index}. id: {vacancy.Id}; назва: {vacancy.Title}; компанія: {vacancy.Company}; "
            + $"зарплата: {DescribeSalary(vacancy)}; локація: {vacancy.Location}; "
            + $"формат: {DescribeWorkFormat(vacancy.WorkFormat)}; досвід: {experience}; "
            + $"опис: {Truncate(vacancy.Description, PromptDescriptionLength)}";
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

    private static string DescribeFilter(AppliedFilter filter)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            parts.Add($"пошук «{filter.Search}»");
        }

        if (!string.IsNullOrWhiteSpace(filter.CategoryGroup))
        {
            parts.Add($"категорія {filter.CategoryGroup}");
        }

        if (filter.WorkFormat is { Count: > 0 })
        {
            parts.Add($"формат роботи: {string.Join(", ", filter.WorkFormat)}");
        }

        if (filter.MaxExperienceMonths is int months)
        {
            parts.Add($"досвід до {months} міс.");
        }

        if (filter.MinSalary is not null || filter.MaxSalary is not null)
        {
            string min = filter.MinSalary?.ToString("0.##", CultureInfo.InvariantCulture) ?? "0";
            string max = filter.MaxSalary?.ToString("0.##", CultureInfo.InvariantCulture) ?? "∞";
            parts.Add($"зарплата {min}–{max} USD");
        }

        if (!string.IsNullOrWhiteSpace(filter.Location))
        {
            parts.Add($"локація {filter.Location}");
        }

        if (filter.Skills is { Count: > 0 })
        {
            parts.Add($"навички: {string.Join(", ", filter.Skills)}");
        }

        return parts.Count == 0 ? "без обмежень" : string.Join("; ", parts);
    }

    private static string DescribeFilterKey(string key) => key switch
    {
        "search" => "пошуковий запит",
        "category_group" => "категорія",
        "work_format" => "формат роботи",
        "max_experience_months" => "досвід",
        "min_salary" => "мінімальна зарплата",
        "max_salary" => "максимальна зарплата",
        "location" => "локація",
        "skills" => "навички",
        _ => key
    };

    private static string BuildDisclaimer(IReadOnlyList<string> relaxed)
    {
        string criteria = string.Join(", ", relaxed.Select(DescribeFilterKey));
        return $"Точних збігів не знайдено. Щоб показати результати, знято критерії: {criteria}. "
            + "Наведені вакансії відповідають запиту не на 100%.";
    }

    private static string BuildNoResultsDisclaimer(IReadOnlyList<string> relaxed)
    {
        if (relaxed.Count == 0)
        {
            return "Точних збігів не знайдено, і навіть після послаблення критеріїв вакансій немає. "
                + "Спробуйте змінити або прибрати частину умов.";
        }

        string criteria = string.Join(", ", relaxed.Select(DescribeFilterKey));
        return $"Точних збігів не знайдено, і навіть після зняття критеріїв ({criteria}) вакансій немає. "
            + "Спробуйте інший запит або звичайний пошук за фільтрами.";
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

    /// <summary>
    /// Системний промпт витягу фільтра: закриті словники (категорії, формати) і семантика кожного поля.
    /// </summary>
    private static string BuildExtractionPrompt(AppliedFilter previousFilter)
    {
        string categories = string.Join(", ", CategoryGroups.All);

        return $"""
            Ти перетворюєш запит користувача на структурований фільтр каталогу вакансій платформи Waterfall.
            Поверни JSON за схемою. Заповнюй лише ті поля, які випливають з нового повідомлення або мають змінити поточний фільтр; решту залишай null.

            Поточний фільтр діалогу (те, що вже обрано раніше; уточнення має його звужувати, а не починати пошук заново):
            {previousFilter.ToJson()}

            Правила значень:
            - category_group: лише одне значення з переліку: {categories}. Якщо запит не про категорію — null.
            - work_format: будь-яка підмножина ["remote","hybrid","office"]; "віддалено" — remote, "гібрид" — hybrid, "в офісі"/"офіс" — office.
            - max_experience_months: ціле число місяців мінімального досвіду, який влаштовує користувача (джуніор — близько 12, мідл — близько 36, сеньйор — близько 60).
            - min_salary і max_salary: зарплата в USD на місяць, лише якщо користувач її назвав; суми в іншій валюті переведи приблизно.
            - location: місто або країна підрядком.
            - skills: окремі конкретні технології чи навички (Python, C#, SQL, Figma), без загальних слів.
            - search: вільний текст лише тоді, коли його не можна виразити іншими полями (наприклад, назва посади).
            - clear: ключі фільтра, які користувач просить прибрати ("зарплата не важлива" — ["min_salary","max_salary"], "будь-який формат" — ["work_format"]).
            Не вигадуй значень і не додавай полів, про які користувач не говорив.
            """;
    }

    private int ResolveHistoryLimit() => Math.Clamp(_options.Value.HistoryMessages, 1, 50);

    /// <summary>
    /// Результат пошуку з можливим послабленням критеріїв.
    /// </summary>
    private sealed record SearchOutcome(
        IReadOnlyList<VacancyDto> Vacancies,
        bool Exact,
        IReadOnlyList<string> Relaxed,
        string? Disclaimer);
}
