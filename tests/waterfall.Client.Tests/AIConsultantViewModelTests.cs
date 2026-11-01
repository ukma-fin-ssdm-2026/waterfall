// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;
using Waterfall.Client;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Ai;
using Waterfall.Client.Models.Auth;
using Waterfall.Client.Models.Common;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

/// <summary>
/// Поведінка вкладки «ШІ-консультант»: інкрементальний рендер SSE-потоку, вкладені вакансії,
/// помилки до/під час потоку (REL-003, PERF-002), очищення сесії та аналіз вакансії (FR-010).
/// </summary>
public class AIConsultantViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeGateway _gateway = new();
    private readonly InMemoryTokenStore _tokens = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingNavigation _navigation = new();

    [Fact]
    public async Task Send_ConcatenatesDeltasIntoOneReply_AndShowsThinkingUntilDone()
    {
        Guid sessionId = Guid.NewGuid();
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(
            new AiMetaEvent { SessionId = sessionId },
            new AiDeltaEvent { Text = "Привіт" },
            new AiDeltaEvent { Text = ", " },
            new AiDeltaEvent { Text = "світе" },
            new AiDoneEvent { MessageId = 7 });

        AIConsultantViewModel vm = CreateViewModel();

        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Messages.Count);
        AIChatMessageViewModel user = vm.Messages[1];
        Assert.True(user.IsUser);
        Assert.Equal("Python remote", user.Text);

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.False(reply.IsUser);
        Assert.Equal("Привіт, світе", reply.Text);
        Assert.False(vm.IsThinking);

        (Guid? SessionId, string Text) request = Assert.Single(_gateway.AiChatRequests);
        Assert.Null(request.SessionId);
        Assert.Equal("Python remote", request.Text);
    }

    [Fact]
    public async Task Send_VacanciesEvent_FillsCardsAndRelaxationNotice()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(
            new AiMetaEvent { SessionId = Guid.NewGuid() },
            new AiVacanciesEvent
            {
                Items =
                [
                    new VacancyDto { Id = first, Title = "Python Developer", Company = "ACME", IsSaved = true },
                    new VacancyDto { Id = second, Title = "Data Engineer", Company = "DataCorp" }
                ],
                Exact = false,
                Relaxed = ["skills", "work_format"],
                Disclaimer = "Точних збігів не знайдено, вакансії відповідають запиту не на 100%."
            },
            new AiDeltaEvent { Text = "Ось найближчі варіанти." },
            new AiDoneEvent { MessageId = 8 });

        AIConsultantViewModel vm = CreateViewModel();

        await vm.SendMessageCommand.ExecuteAsync(null);

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.Equal(2, reply.MatchedVacancies.Count);
        Assert.Equal("Python Developer", reply.MatchedVacancies[0].Title);
        Assert.True(reply.HasVacancies);
        Assert.Equal("Точних збігів не знайдено, вакансії відповідають запиту не на 100%.", reply.MatchDisclaimer);
        Assert.Contains("навички", reply.RelaxedFilters);
        Assert.Contains("формат роботи", reply.RelaxedFilters);
        Assert.Equal("Ось найближчі варіанти.", reply.Text);

        // Картка в чаті — той самий VacancyItemViewModel, тож «Обране» працює й тут.
        await reply.MatchedVacancies[0].ToggleSaveCommand.ExecuteAsync(null);
        Assert.Equal(("unsave", first), _gateway.ToggleCalls[^1]);
    }

    [Fact]
    public async Task Send_ExactResults_HaveNoDisclaimer()
    {
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(
            new AiVacanciesEvent
            {
                Items = [new VacancyDto { Id = Guid.NewGuid(), Title = "Python Developer" }],
                Exact = true
            });

        AIConsultantViewModel vm = CreateViewModel();

        await vm.SendMessageCommand.ExecuteAsync(null);

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.False(reply.HasDisclaimer);
        Assert.False(reply.HasRelaxedFilters);
    }

    [Fact]
    public async Task Send_ErrorAfterVacancies_KeepsCardsAndShowsMessage()
    {
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(
            new AiVacanciesEvent
            {
                Items = [new VacancyDto { Id = Guid.NewGuid(), Title = "Python Developer" }],
                Exact = true
            },
            new AiErrorEvent { Code = "INTERNAL", Message = "Не вдалося згенерувати відповідь." });

        AIConsultantViewModel vm = CreateViewModel();

        await vm.SendMessageCommand.ExecuteAsync(null);

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.Single(reply.MatchedVacancies);
        Assert.Contains("Не вдалося згенерувати відповідь.", reply.Text);
        Assert.True(reply.ShowCatalogAction);
        Assert.False(vm.IsThinking);
    }

    [Fact]
    public async Task Send_LlmUnavailable_FallsBackToCatalogWithRel003Message()
    {
        _gateway.AiChatStream = (_, _, _) => AiStreams.Fail(new AiConsultantException(
            HttpStatusCode.ServiceUnavailable,
            new ApiError { Code = "LLM_UNAVAILABLE", Message = "LLM тимчасово недоступна." },
            "LLM тимчасово недоступна.",
            "LLM_UNAVAILABLE"));

        AIConsultantViewModel vm = CreateViewModel();

        await vm.SendMessageCommand.ExecuteAsync(null);

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.Contains("каталозі", reply.Text);
        Assert.True(reply.ShowCatalogAction);
        Assert.False(vm.IsThinking);
    }

    [Fact]
    public async Task Send_RateLimited_ShowsLimitMessage()
    {
        _gateway.AiChatStream = (_, _, _) => AiStreams.Fail(new AiConsultantException(
            HttpStatusCode.TooManyRequests,
            null,
            "Перевищено ліміт запитів до ШІ-консультанта. Спробуйте пізніше.",
            AiConsultantException.RateLimitedCode));

        AIConsultantViewModel vm = CreateViewModel();

        await vm.SendMessageCommand.ExecuteAsync(null);

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.Contains("ліміт", reply.Text);
        Assert.True(reply.ShowCatalogAction);
        Assert.False(vm.IsThinking);
    }

    [Fact]
    public async Task Send_StaleSession_ResetsSessionAndAsksToRepeat()
    {
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(new AiMetaEvent { SessionId = Guid.NewGuid() });
        AIConsultantViewModel vm = CreateViewModel();
        await vm.SendMessageCommand.ExecuteAsync(null);

        _gateway.AiChatStream = (_, _, _) => AiStreams.Fail(new AiConsultantException(
            HttpStatusCode.NotFound,
            new ApiError { Code = "NOT_FOUND", Message = "Сесію не знайдено." },
            "Сесію не знайдено.",
            "NOT_FOUND"));
        vm.InputText = "А тепер тільки з зарплатою від 1000$";
        await vm.SendMessageCommand.ExecuteAsync(null);
        Assert.Contains("втрачено", vm.Messages[^1].Text);

        // Наступний запит має почати новий діалог, а не повторювати мертвий session_id.
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(new AiMetaEvent { SessionId = Guid.NewGuid() });
        vm.InputText = "Віддалено";
        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.Equal(3, _gateway.AiChatRequests.Count);
        Assert.NotNull(_gateway.AiChatRequests[1].SessionId);
        Assert.Null(_gateway.AiChatRequests[2].SessionId);
    }

    [Fact]
    public async Task ClearChat_DeletesSession_AndNextSendStartsNewDialogue()
    {
        Guid sessionId = Guid.NewGuid();
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(
            new AiMetaEvent { SessionId = sessionId },
            new AiDeltaEvent { Text = "Відповідь" });

        AIConsultantViewModel vm = CreateViewModel();
        await vm.SendMessageCommand.ExecuteAsync(null);

        await vm.ClearChatCommand.ExecuteAsync(null);

        Assert.Equal(sessionId, Assert.Single(_gateway.DeletedSessions));
        AIChatMessageViewModel welcome = Assert.Single(vm.Messages);
        Assert.False(welcome.IsUser);

        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(new AiMetaEvent { SessionId = Guid.NewGuid() });
        vm.InputText = "Новий діалог";
        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.Null(_gateway.AiChatRequests[1].SessionId);
    }

    [Fact]
    public async Task ClearChat_WhenSessionAlreadyGone_StillResetsLocally()
    {
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(new AiMetaEvent { SessionId = Guid.NewGuid() });
        AIConsultantViewModel vm = CreateViewModel();
        await vm.SendMessageCommand.ExecuteAsync(null);

        _gateway.DeleteSessionError = new ApiException(HttpStatusCode.NotFound, null, "Ресурс не знайдено.");

        await vm.ClearChatCommand.ExecuteAsync(null);

        Assert.Single(vm.Messages);
    }

    [Fact]
    public async Task Send_WithoutToken_ShowsLoginMessage_WithoutCallingServer()
    {
        AIConsultantViewModel vm = CreateViewModel(authenticated: false);

        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.Empty(_gateway.AiChatRequests);
        Assert.Contains("Увійдіть", vm.Messages[^1].Text);
        Assert.Equal("Python remote", vm.InputText);
        Assert.False(vm.IsThinking);
    }

    [Fact]
    public async Task Send_AsAnalyst_ExplainsJobSeekerOnly_WithoutCallingServer()
    {
        AIConsultantViewModel vm = CreateViewModel(role: UserRole.Analyst);

        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.Empty(_gateway.AiChatRequests);
        Assert.Contains("Шукач роботи", vm.Messages[^1].Text);
    }

    [Fact]
    public async Task Analyze_RendersRelevanceScoreAndMatchedTerms()
    {
        Guid vacancyId = Guid.NewGuid();
        _gateway.AiAnalyzeStream = (_, _) => AiStreams.Of(
            new AiRelevanceEvent
            {
                Items =
                [
                    new AiRelevanceItem
                    {
                        VacancyId = vacancyId,
                        Score = 82,
                        Matched = ["Python", "remote"],
                        BasisCount = 3
                    }
                ]
            },
            new AiDeltaEvent { Text = "Вакансія добре підходить." },
            new AiDoneEvent());

        AIConsultantViewModel vm = CreateViewModel();

        await vm.AnalyzeVacancyCommand.ExecuteAsync(new VacancyDto { Id = vacancyId, Title = "Python Developer" });

        Assert.Equal(vacancyId, Assert.Single(Assert.Single(_gateway.AnalyzeRequests)));
        Assert.Equal("Аналіз вакансії «Python Developer»", vm.Messages[1].Text);
        Assert.True(vm.Messages[1].IsUser);

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.Contains("82/100", reply.RelevanceText);
        Assert.Contains("Python", reply.RelevanceText);
        Assert.Equal("Вакансія добре підходить.", reply.Text);
    }

    [Fact]
    public async Task Analyze_WithoutSavedVacancies_RendersRelevanceHint()
    {
        Guid vacancyId = Guid.NewGuid();
        _gateway.AiAnalyzeStream = (_, _) => AiStreams.Of(
            new AiRelevanceEvent
            {
                Items = [new AiRelevanceItem { VacancyId = vacancyId, Score = null, BasisCount = 0 }]
            },
            new AiDeltaEvent { Text = "Вимоги: Python, SQL." });

        AIConsultantViewModel vm = CreateViewModel();

        await vm.AnalyzeVacancyCommand.ExecuteAsync(new VacancyDto { Id = vacancyId, Title = "Python Developer" });

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.Equal(AIConsultantViewModel.NoSavedVacanciesHint, reply.RelevanceText);
        Assert.Equal("Вимоги: Python, SQL.", reply.Text);
    }

    [Fact]
    public async Task Analyze_LlmFailsAfterRelevance_KeepsScoreAndOffersCatalog()
    {
        // REL-003: оцінка релевантності не потребує LLM, тож лишається корисною після збою розбору
        Guid vacancyId = Guid.NewGuid();
        _gateway.AiAnalyzeStream = (_, _) => AiStreams.Of(
            new AiRelevanceEvent { Items = [new AiRelevanceItem { VacancyId = vacancyId, Score = 55, BasisCount = 2 }] },
            new AiErrorEvent { Code = AiConsultantException.LlmUnavailableCode, Message = "LLM недоступна" });

        AIConsultantViewModel vm = CreateViewModel();

        await vm.AnalyzeVacancyCommand.ExecuteAsync(new VacancyDto { Id = vacancyId, Title = "Python Developer" });

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.Contains("55/100", reply.RelevanceText);
        Assert.Contains("каталозі", reply.Text);
        Assert.True(reply.ShowCatalogAction);
        Assert.False(vm.IsThinking);
    }

    [Fact]
    public async Task Analyze_RateLimitedBeforeStream_ShowsLimitMessage()
    {
        _gateway.AiAnalyzeStream = (_, _) => AiStreams.Fail(new AiConsultantException(
            HttpStatusCode.TooManyRequests, null, "ліміт", AiConsultantException.RateLimitedCode));

        AIConsultantViewModel vm = CreateViewModel();

        await vm.AnalyzeVacancyCommand.ExecuteAsync(new VacancyDto { Id = Guid.NewGuid(), Title = "QA" });

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.Contains("ліміт", reply.Text);
        Assert.True(reply.ShowCatalogAction);
    }

    [Fact]
    public async Task Analyze_ServerUnreachable_ShowsConnectionErrorWithoutCatalogAction()
    {
        _gateway.AiAnalyzeStream = (_, _) => AiStreams.Fail(new ApiException(null, null, "Не вдалося з'єднатися з сервером."));

        AIConsultantViewModel vm = CreateViewModel();

        await vm.AnalyzeVacancyCommand.ExecuteAsync(new VacancyDto { Id = Guid.NewGuid(), Title = "QA" });

        AIChatMessageViewModel reply = vm.Messages[2];
        Assert.Contains("з'єднатися", reply.Text);
        Assert.False(reply.ShowCatalogAction);
        Assert.False(vm.IsThinking);
    }

    [Fact]
    public async Task AnalyzeRequestFromCard_OpensAiTabThenStreamsAnalysis()
    {
        // Картка в каталозі шле повідомлення; вкладка ШІ має відкритися до початку стріму
        WeakReferenceMessenger messenger = new();
        Guid vacancyId = Guid.NewGuid();
        TaskCompletionSource streamed = new();
        _gateway.AiAnalyzeStream = (_, _) =>
        {
            Assert.Equal(["//aiconsultant"], _navigation.Routes);
            streamed.TrySetResult();
            return AiStreams.Of(new AiDeltaEvent { Text = "Розбір." }, new AiDoneEvent());
        };
        AIConsultantViewModel vm = CreateViewModel(messenger: messenger);

        messenger.Send(new AnalyzeVacancyRequestedMessage(new VacancyDto { Id = vacancyId, Title = "Python Developer" }));
        await streamed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => !vm.IsThinking && vm.Messages.Count == 3);

        Assert.Equal(vacancyId, Assert.Single(Assert.Single(_gateway.AnalyzeRequests)));
        Assert.Equal("Розбір.", vm.Messages[2].Text);
    }

    [Fact]
    public async Task GoToCatalog_NavigatesToCatalogTab()
    {
        AIConsultantViewModel vm = CreateViewModel();

        await vm.GoToCatalogCommand.ExecuteAsync(null);

        Assert.Equal(["//catalog"], _navigation.Routes);
    }

    [Fact]
    public async Task Send_RelaxedCriteria_AreNamedInUkrainian()
    {
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(
            new AiMetaEvent { SessionId = Guid.NewGuid() },
            new AiVacanciesEvent
            {
                Items = [new VacancyDto { Id = Guid.NewGuid(), Title = "Бухгалтер" }],
                Exact = false,
                Relaxed = ["location", "max_experience_months", "min_salary", "category_group", "search", "brand_new_key"]
            });

        AIConsultantViewModel vm = CreateViewModel();

        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.Equal(
            "Знято для розширення пошуку: локація, досвід, зарплата, категорія, пошуковий запит, brand new key",
            vm.Messages[2].RelaxedFilters);
    }

    [Fact]
    public async Task SendQuickPrompt_SendsThePromptText()
    {
        _gateway.AiChatStream = (_, _, _) => AiStreams.Of(new AiDoneEvent { MessageId = 1 });
        AIConsultantViewModel vm = CreateViewModel();

        await vm.SendQuickPromptCommand.ExecuteAsync(vm.QuickPrompts[1]);

        Assert.Equal(vm.QuickPrompts[1], Assert.Single(_gateway.AiChatRequests).Text);
    }

    [Fact]
    public async Task CancelActiveStream_StopsThinking_AndLeavesPartialReply()
    {
        _gateway.AiChatStream = (_, _, ct) => AiStreams.WaitForCancel(ct);
        AIConsultantViewModel vm = CreateViewModel();

        Task send = vm.SendMessageCommand.ExecuteAsync(null);
        await _gateway.ChatStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(vm.IsThinking);

        vm.CancelActiveStream();
        await send.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(vm.IsThinking);
    }

    [Fact]
    public void AnalyzeWithAi_SendsVacancyToAiConsultant()
    {
        WeakReferenceMessenger messenger = new();
        AnalyzeVacancyRequestedMessage? received = null;
        messenger.Register<AnalyzeVacancyRequestedMessage>(this, (_, message) => received = message);
        VacancyDto vacancy = new() { Id = Guid.NewGuid(), Title = "Python Developer" };
        VacancyItemViewModel card = new(vacancy, _gateway, messenger, _ => { });

        card.AnalyzeWithAiCommand.Execute(null);

        Assert.Equal(vacancy, received?.Vacancy);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "умова не виконалася за 5 с");
            await Task.Delay(10);
        }
    }

    private AIConsultantViewModel CreateViewModel(
        bool authenticated = true,
        UserRole role = UserRole.JobSeeker,
        IMessenger? messenger = null)
    {
        if (authenticated)
        {
            _tokens.Token = Jwt(role, Now.AddHours(1));
        }

        AIConsultantViewModel vm = new(
            _gateway,
            messenger ?? new WeakReferenceMessenger(),
            tokens: _tokens,
            time: _time,
            navigation: _navigation)
        {
            InputText = "Python remote"
        };
        return vm;
    }

    /// <summary>
    /// JWT у форматі сервера (підпис клієнт не перевіряє; копія LoginViewModelTests).
    /// </summary>
    private static string Jwt(UserRole role, DateTimeOffset expires)
    {
        static string B64(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["email"] = "kosta@ukma.edu.ua",
            ["role"] = role.ToString(),
            ["exp"] = expires.ToUnixTimeSeconds()
        });
        return $"{B64("""{"alg":"HS256","typ":"JWT"}""")}.{B64(payload)}.sig";
    }

    private sealed class RecordingNavigation : INavigationService
    {
        public List<string> Routes { get; } = [];

        public Task GoToCatalogAsync()
        {
            Routes.Add("//catalog");
            return Task.CompletedTask;
        }

        public Task GoToAiConsultantAsync()
        {
            Routes.Add("//aiconsultant");
            return Task.CompletedTask;
        }
    }
}
