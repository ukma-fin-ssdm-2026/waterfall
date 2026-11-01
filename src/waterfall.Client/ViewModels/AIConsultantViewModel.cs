// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Ai;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Vacancies;

namespace Waterfall.Client.ViewModels;

/// <summary>
/// Елемент повідомлення у ШІ-чаті (US-005, FR-002, FR-009).
/// </summary>
public partial class AIChatMessageViewModel : ObservableObject
{
    public AIChatMessageViewModel()
    {
        // Картки додаються по одній після події vacancies, тож «є вакансії» треба перерахувати.
        MatchedVacancies.CollectionChanged += OnMatchedVacanciesChanged;
    }

    /// <summary>
    /// Текст повідомлення; відповідь ШІ дописується фрагментами SSE-потоку (delta) у реальному часі.
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    public bool IsUser { get; init; }
    public bool IsBot => !IsUser;
    public string SenderName => IsUser ? "Ви" : "ШІ-консультант";
    public DateTime Time { get; init; } = DateTime.Now;
    public string TimeText => Time.ToString("HH:mm");

    /// <summary>
    /// Список знайдених вакансій, вкладених у відповідь ШІ (US-005 AC-1, AC-3).
    /// </summary>
    public ObservableCollection<VacancyItemViewModel> MatchedVacancies { get; } = [];

    public bool HasVacancies => MatchedVacancies.Count > 0;

    /// <summary>
    /// Позначка релевантності (наприклад «Відповідає запиту не на 100%», US-005 AC-3).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisclaimer))]
    public partial string? MatchDisclaimer { get; set; }

    public bool HasDisclaimer => !string.IsNullOrEmpty(MatchDisclaimer);

    /// <summary>
    /// Які саме критерії довелося зняти, щоб знайти хоч щось (US-005 AC-3).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRelaxedFilters))]
    public partial string? RelaxedFilters { get; set; }

    public bool HasRelaxedFilters => !string.IsNullOrEmpty(RelaxedFilters);

    /// <summary>
    /// Результат аналізу конкретної вакансії: оцінка 0–100 і збіги або підказка зберегти вакансії (FR-010).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRelevance))]
    public partial string? RelevanceText { get; set; }

    public bool HasRelevance => !string.IsNullOrEmpty(RelevanceText);

    /// <summary>
    /// Показати перехід у каталог: REL-003, коли ШІ недоступний або перевищено ліміт запитів.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowCatalogAction { get; set; }

    /// <summary>
    /// Дописує фрагмент відповіді (SSE delta) у кінець тексту.
    /// </summary>
    public void AppendText(string chunk) => Text += chunk;

    /// <summary>
    /// Додає окремий абзац (пояснення сервера, текст помилки після вже показаних вакансій).
    /// </summary>
    public void AppendBlock(string text) => Text = string.IsNullOrEmpty(Text) ? text : $"{Text}\n\n{text}";

    private void OnMatchedVacanciesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(HasVacancies));
}

/// <summary>
/// Вкладка «ШІ-консультант»: текстовий чат зі збереженням контексту сесії (FR-009),
/// пошук звичайною мовою (FR-002) та аналіз конкретної вакансії (FR-010).
/// </summary>
/// <remarks>
/// Потік SSE обробляється інкрементально: індикатор очікування вмикається до першого запиту
/// (PERF-002, ≤ 1 с), текст дописується в те саме повідомлення, вакансії приходять лише з бази (FUNC-001).
/// </remarks>
public partial class AIConsultantViewModel : ObservableObject
{
    /// <summary>
    /// Підказка, коли сервер не має збережених вакансій для розрахунку релевантності (score = null).
    /// </summary>
    public const string NoSavedVacanciesHint = "Збережіть кілька вакансій, щоб отримати оцінку релевантності";

    private const string WelcomeText =
        "Привіт! Я ваш ШІ-консультант із працевлаштування. Напишіть мені звичайною мовою, яку роботу ви шукаєте (наприклад: «Хочу віддалену роботу на Python для джуніора»), і я підберу найкращі вакансії.";

    private const string CatalogFallbackMessage =
        "ШІ-консультант зараз недоступний. Скористайтеся звичайним пошуком у каталозі — він працює незалежно від ШІ.";

    private const string RateLimitMessage =
        "Перевищено ліміт запитів до ШІ-консультанта. Спробуйте, будь ласка, пізніше.";

    private const string LoginRequiredMessage =
        "Увійдіть у обліковий запис, щоб користуватися ШІ-консультантом.";

    private const string JobSeekerOnlyMessage =
        "ШІ-консультант доступний лише користувачам із роллю «Шукач роботи».";

    private readonly IHttpGatewayService _gateway;
    private readonly IMessenger _messenger;
    private readonly IUrlLauncher? _urlLauncher;
    private readonly IDistanceCalculatorService? _distanceService;
    private readonly IAuthTokenStore? _tokens;
    private readonly TimeProvider _time;
    private readonly INavigationService? _navigation;

    private CancellationTokenSource? _streamCts;
    private Guid? _sessionId;

    public AIConsultantViewModel(
        IHttpGatewayService gateway,
        IMessenger messenger,
        IUrlLauncher? urlLauncher = null,
        IDistanceCalculatorService? distanceService = null,
        IAuthTokenStore? tokens = null,
        TimeProvider? time = null,
        INavigationService? navigation = null)
    {
        _gateway = gateway;
        _messenger = messenger;
        _urlLauncher = urlLauncher;
        _distanceService = distanceService;
        _tokens = tokens;
        _time = time ?? TimeProvider.System;
        _navigation = navigation;

        // Подія з картки у будь-якій вкладці: спершу показуємо розділ ШІ, потім запускаємо аналіз (FR-010).
        messenger.Register<AIConsultantViewModel, AnalyzeVacancyRequestedMessage>(this, static (vm, message) =>
            _ = vm.HandleAnalyzeRequestAsync(message.Vacancy));

        ResetConversation();
    }

    public ObservableCollection<AIChatMessageViewModel> Messages { get; } = [];

    public IReadOnlyList<string> QuickPrompts { get; } =
    [
        "Віддалена робота на Python для джуніора",
        "Тільки з зарплатою від $1000",
        ".NET Developer з досвідом 1-3 роки",
        "QA Automation remote"
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    public partial string InputText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusHint))]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeVacancyCommand))]
    public partial bool IsThinking { get; set; }

    public string StatusHint => IsThinking ? "ШІ-консультант аналізує базу вакансій..." : string.Empty;

    /// <summary>
    /// Помилка, яку треба показати банером (наприклад, не вдалося змінити «Обране» з картки в чаті).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Зупиняє поточний потік, коли користувач іде зі сторінки або очищає чат.
    /// </summary>
    public void CancelActiveStream() => _streamCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendMessageAsync()
    {
        if (IsThinking)
        {
            return;
        }

        string prompt = InputText.Trim();
        if (prompt.Length == 0)
        {
            return;
        }

        // Неавторизований запит сервер все одно відхилить (401/403), але краще не витрачати ліміт запитів.
        string? denial = await AccessDenialAsync();
        if (denial is not null)
        {
            Messages.Add(BotMessage(denial));
            return;
        }

        InputText = string.Empty;
        ErrorMessage = null;
        Messages.Add(new AIChatMessageViewModel { IsUser = true, Text = prompt });

        // PERF-002: індикатор очікування вмикається одразу, до першого мережевого запиту.
        IsThinking = true;
        CancellationTokenSource cts = StartStream();
        AIChatMessageViewModel? reply = null;

        try
        {
            await foreach (AiStreamEvent streamEvent in _gateway.SendAiMessageAsync(_sessionId, prompt, cts.Token))
            {
                switch (streamEvent)
                {
                    case AiMetaEvent meta:
                        _sessionId = meta.SessionId;
                        break;
                    case AiVacanciesEvent vacancies:
                        reply ??= AddBotMessage();
                        FillVacancies(reply, vacancies);
                        break;
                    case AiDeltaEvent delta:
                        reply ??= AddBotMessage();
                        reply.AppendText(delta.Text);
                        break;
                    case AiDoneEvent:
                        break;
                    case AiErrorEvent error:
                        reply ??= AddBotMessage();
                        reply.AppendBlock(StreamErrorMessage(error));
                        reply.ShowCatalogAction = true;
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Користувач пішов зі сторінки або очистив чат — часткова відповідь лишається як є.
        }
        catch (AiConsultantException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Сесія зникла на сервері (перезапуск API): далі діалог почнеться з чистого аркуша.
            _sessionId = null;
            (reply ?? AddBotMessage()).AppendBlock("Сесію діалогу втрачено. Напишіть, будь ласка, запит ще раз.");
        }
        catch (AiConsultantException ex)
        {
            reply ??= AddBotMessage();
            reply.AppendBlock(PreStreamErrorMessage(ex));
            reply.ShowCatalogAction = ex.IsLlmUnavailable || ex.IsRateLimited;
        }
        catch (ApiException ex)
        {
            (reply ?? AddBotMessage()).AppendBlock(ex.Message);
        }
        finally
        {
            FinishStream(cts);
        }
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(InputText) && !IsThinking;

    [RelayCommand]
    private Task SendQuickPromptAsync(string prompt)
    {
        InputText = prompt;
        return SendMessageAsync();
    }

    /// <summary>
    /// Аналіз вакансії з картки (FR-010): оцінка релевантності до збережених вакансій і текстовий розбір.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartRequest))]
    private async Task AnalyzeVacancyAsync(VacancyDto vacancy)
    {
        if (IsThinking || vacancy is null)
        {
            return;
        }

        string? denial = await AccessDenialAsync();
        if (denial is not null)
        {
            Messages.Add(BotMessage(denial));
            return;
        }

        ErrorMessage = null;
        Messages.Add(new AIChatMessageViewModel { IsUser = true, Text = $"Аналіз вакансії «{vacancy.Title}»" });

        IsThinking = true;
        CancellationTokenSource cts = StartStream();
        AIChatMessageViewModel? reply = null;

        try
        {
            await foreach (AiStreamEvent streamEvent in _gateway.AnalyzeVacanciesAsync([vacancy.Id], cts.Token))
            {
                switch (streamEvent)
                {
                    case AiRelevanceEvent relevance:
                        reply ??= AddBotMessage();
                        reply.RelevanceText = FormatRelevance(relevance.Items.FirstOrDefault());
                        break;
                    case AiDeltaEvent delta:
                        reply ??= AddBotMessage();
                        reply.AppendText(delta.Text);
                        break;
                    case AiErrorEvent error:
                        reply ??= AddBotMessage();
                        reply.AppendBlock(StreamErrorMessage(error));
                        reply.ShowCatalogAction = true;
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Скасовано — частковий результат лишається видимим.
        }
        catch (AiConsultantException ex)
        {
            reply ??= AddBotMessage();
            reply.AppendBlock(PreStreamErrorMessage(ex));
            reply.ShowCatalogAction = ex.IsLlmUnavailable || ex.IsRateLimited;
        }
        catch (ApiException ex)
        {
            (reply ?? AddBotMessage()).AppendBlock(ex.Message);
        }
        finally
        {
            FinishStream(cts);
        }
    }

    private bool CanStartRequest() => !IsThinking;

    /// <summary>
    /// REL-003: запасний шлях у звичайний каталог, коли ШІ недоступний.
    /// </summary>
    [RelayCommand]
    private async Task GoToCatalogAsync()
    {
        if (_navigation is not null)
        {
            await _navigation.GoToCatalogAsync();
        }
    }

    /// <summary>
    /// Очищення чату (FR-009): зупиняє потік, видаляє сесію на сервері й починає новий діалог.
    /// </summary>
    [RelayCommand]
    private async Task ClearChatAsync()
    {
        CancelActiveStream();
        Guid? session = _sessionId;
        _sessionId = null;
        ResetConversation();

        if (session is not Guid sessionId)
        {
            return;
        }

        try
        {
            await _gateway.DeleteAiSessionAsync(sessionId);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Сесії вже немає (наприклад, очищена з іншого пристрою) — локальний контекст уже очищено.
        }
        catch (ApiException)
        {
            // Офлайн: локальний діалог очищено, серверна сесія просто лишиться невикористаною.
        }
    }

    private async Task HandleAnalyzeRequestAsync(VacancyDto vacancy)
    {
        if (_navigation is not null)
        {
            try
            {
                // Shell створює сторінку вкладки ліниво, тож спершу показуємо її, а вже потім стрімимо аналіз.
                await _navigation.GoToAiConsultantAsync();
            }
            catch (Exception)
            {
                // Навігація не повинна зривати сам аналіз: навіть якщо вкладку не вдалося відкрити, він корисний.
            }
        }

        await AnalyzeVacancyCommand.ExecuteAsync(vacancy);
    }

    /// <summary>
    /// Перевірка локальної сесії: сервер поверне 401/403, але краще одразу пояснити причину.
    /// </summary>
    /// <returns>Повідомлення для користувача або null, якщо запит можна надсилати.</returns>
    private async Task<string?> AccessDenialAsync()
    {
        if (_tokens is null)
        {
            return null;
        }

        string? token = await _tokens.GetTokenAsync();
        AuthSession? session = token is null ? null : AuthSession.FromToken(token);
        if (session is null || session.IsExpired(_time.GetUtcNow()))
        {
            return LoginRequiredMessage;
        }

        return session.Role == UserRole.JobSeeker ? null : JobSeekerOnlyMessage;
    }

    private CancellationTokenSource StartStream()
    {
        CancellationTokenSource cts = new();
        _streamCts = cts;
        return cts;
    }

    private void FinishStream(CancellationTokenSource cts)
    {
        IsThinking = false;
        if (ReferenceEquals(_streamCts, cts))
        {
            _streamCts = null;
        }

        cts.Dispose();
    }

    private void FillVacancies(AIChatMessageViewModel reply, AiVacanciesEvent vacancies)
    {
        reply.MatchedVacancies.Clear();
        foreach (VacancyDto vacancy in vacancies.Items)
        {
            reply.MatchedVacancies.Add(new VacancyItemViewModel(
                vacancy,
                _gateway,
                _messenger,
                message => ErrorMessage = message,
                _urlLauncher,
                _distanceService));
        }

        // US-005 AC-3: показуємо пояснення сервера, а не вигадану «відповідність ~85%».
        reply.MatchDisclaimer = vacancies.Exact
            ? null
            : string.IsNullOrWhiteSpace(vacancies.Disclaimer)
                ? "Точних збігів не знайдено, вакансії відповідають запиту не на 100%."
                : vacancies.Disclaimer;

        reply.RelaxedFilters = vacancies.Exact ? null : FormatRelaxedFilters(vacancies.Relaxed);
    }

    private static string? FormatRelaxedFilters(IReadOnlyList<string> relaxed) =>
        relaxed.Count == 0 ? null : "Знято для розширення пошуку: " + string.Join(", ", relaxed.Select(FilterLabel));

    private static string FilterLabel(string key) => key switch
    {
        "search" => "пошуковий запит",
        "category_group" => "категорія",
        "work_format" => "формат роботи",
        "max_experience_months" => "досвід",
        "min_salary" => "зарплата",
        "max_salary" => "зарплата",
        "location" => "локація",
        "skills" => "навички",
        _ => key.Replace('_', ' ')
    };

    private static string FormatRelevance(AiRelevanceItem? item)
    {
        if (item?.Score is not int score)
        {
            return NoSavedVacanciesHint;
        }

        string text = $"Релевантність: {score}/100";
        return item.Matched.Count == 0 ? text : $"{text} · збіги: {string.Join(", ", item.Matched)}";
    }

    /// <summary>
    /// Повідомлення для помилки, що прийшла подією <c>error</c> уже після початку потоку.
    /// </summary>
    private static string StreamErrorMessage(AiErrorEvent error) => error.Code switch
    {
        AiConsultantException.LlmUnavailableCode => CatalogFallbackMessage,
        AiConsultantException.RateLimitedCode => RateLimitMessage,
        _ => string.IsNullOrWhiteSpace(error.Message) ? "ШІ-консультант не зміг завершити відповідь." : error.Message
    };

    /// <summary>
    /// Повідомлення для помилки до початку потоку (401/403/404/429/503).
    /// </summary>
    private static string PreStreamErrorMessage(AiConsultantException ex) => ex switch
    {
        { IsLlmUnavailable: true } => CatalogFallbackMessage,
        { IsRateLimited: true } => RateLimitMessage,
        _ => ex.Message
    };

    private static AIChatMessageViewModel BotMessage(string text) => new() { IsUser = false, Text = text };

    private AIChatMessageViewModel AddBotMessage()
    {
        AIChatMessageViewModel message = new() { IsUser = false };
        Messages.Add(message);
        return message;
    }

    private void ResetConversation()
    {
        Messages.Clear();
        Messages.Add(BotMessage(WelcomeText));
    }
}
