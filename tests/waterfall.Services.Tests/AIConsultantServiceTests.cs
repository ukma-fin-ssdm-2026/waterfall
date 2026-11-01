// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Waterfall.Repository.Entities;
using Waterfall.Services.Configuration;
using Waterfall.Services.Exceptions;
using Waterfall.Services.Llm;
using Waterfall.Services.Models.Ai;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Тести ШІ-консультанта (FR-002, FR-009, US-005): уточнення фільтра, серверна валідація значень LLM,
/// послаблення критеріїв за 0 збігів, обмеження промпту вакансіями з БД (FUNC-001) та маскування ПД (THR-06).
/// </summary>
public sealed class AIConsultantServiceTests
{
    private readonly FakeChatSessionRepository _chatRepository = new();
    private readonly FakeCatalogService _catalog = new();
    private readonly ScriptedLlmClientProvider _llm = new();
    private readonly FakeLogger<AIConsultantService> _logger = new();
    private readonly Guid _userId = Guid.NewGuid();

    private AIConsultantService CreateService(AiConsultantOptions? options = null)
    {
        return new AIConsultantService(
            _chatRepository,
            _catalog,
            _llm,
            Options.Create(options ?? new AiConsultantOptions()),
            _logger);
    }

    private static VacancyDto CreateVacancy(
        string title,
        WorkFormat format = WorkFormat.Remote,
        int? experienceMonths = 12,
        decimal? salaryMin = 1000,
        decimal? salaryMax = 2000,
        string location = "Київ",
        string? categoryGroup = "Software Development",
        params string[] skills)
    {
        return new VacancyDto
        {
            Id = Guid.NewGuid(),
            Title = title,
            Company = "Waterfall Labs",
            Description = $"{title} — опис вакансії",
            Category = categoryGroup ?? "Software Development",
            CategoryGroup = categoryGroup,
            Location = location,
            Experience = experienceMonths is int months ? $"{months} міс." : null,
            ExperienceMonths = experienceMonths,
            WorkFormat = format,
            SalaryMin = salaryMin,
            SalaryMax = salaryMax,
            Currency = "USD",
            Source = JobSource.Djinni,
            SourceUrl = "https://example.com/job",
            Skills = skills.Length == 0 ? ["Python"] : skills,
            PublishedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
    }

    private async Task<(string Text, long MessageId)> ConsumeAnswerAsync(
        AIConsultantService service,
        AIConsultantTurn turn)
    {
        StringBuilder text = new();
        long? messageId = null;

        await foreach (AIConsultantStreamItem item in service.StreamAnswerAsync(turn))
        {
            switch (item)
            {
                case AIConsultantDelta delta:
                    text.Append(delta.Text);
                    break;
                case AIConsultantCompleted completed:
                    messageId = completed.MessageId;
                    break;
            }
        }

        Assert.NotNull(messageId);
        return (text.ToString(), messageId.Value);
    }

    private static string MessageText(ChatMessage message)
        => string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));

    private static string Texts(IReadOnlyList<ChatMessage> messages)
    {
        return string.Join("\n", messages.Select(MessageText));
    }

    [Fact]
    public async Task PrepareAsync_NewSession_CreatesOwnedSessionAndStoresUserMessage()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        _llm.Extract.Respond("{\"search\":\"python\"}");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "  шукаю python  ");

        // Assert
        ChatSessionDB session = Assert.Single(_chatRepository.Sessions);
        Assert.Equal(_userId, session.UserId);
        Assert.Equal(turn.SessionId, session.Id);
        Assert.Equal(turn.Filter.ToJson(), session.ContextFilterJson);
        Assert.Contains("\"search\":\"python\"", session.ContextFilterJson, StringComparison.Ordinal);

        ChatMessageDB userMessage = Assert.Single(_chatRepository.Messages);
        Assert.Equal(ChatMessageAuthor.User, userMessage.Author);
        Assert.Equal("шукаю python", userMessage.Content);
    }

    [Fact]
    public async Task PrepareAsync_Refinement_KeepsPreviousFilterAndChangesOnlyTheNewCriterion()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        AIConsultantService service = CreateService();
        _llm.Extract.Respond("{\"work_format\":[\"remote\"],\"max_experience_months\":12}");
        AIConsultantTurn first = await service.PrepareAsync(_userId, null, "віддалена робота для джуніора");

        // Act
        _llm.Extract.Respond("{\"min_salary\":1000}");
        AIConsultantTurn second = await service.PrepareAsync(
            _userId,
            first.SessionId,
            "а тепер тільки з зарплатою від 1000$");

        // Assert
        Assert.Equal(new[] { "remote" }, second.Filter.WorkFormat);
        Assert.Equal(12, second.Filter.MaxExperienceMonths);
        Assert.Equal(1000m, second.Filter.MinSalary);
        Assert.Single(_chatRepository.Sessions);
        Assert.Equal(second.Filter.ToJson(), _chatRepository.Sessions[0].ContextFilterJson);
    }

    [Fact]
    public async Task PrepareAsync_InvalidExtractionValues_AreDroppedOrClamped()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        _llm.Extract.Respond(
            """
            {
              "category_group": "Космічні кораблі",
              "work_format": ["remote", "mars"],
              "max_experience_months": -5,
              "min_salary": -100,
              "max_salary": 5000000,
              "location": "  Київ  ",
              "skills": [" C# ", "c#", "", "Python", "SQL"]
            }
            """);
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "хочу все і одразу");

        // Assert
        Assert.Null(turn.Filter.CategoryGroup);
        Assert.Equal(new[] { "remote" }, turn.Filter.WorkFormat);
        Assert.Null(turn.Filter.MaxExperienceMonths);
        Assert.Null(turn.Filter.MinSalary);
        Assert.Equal(1_000_000m, turn.Filter.MaxSalary);
        Assert.Equal("Київ", turn.Filter.Location);
        Assert.Equal(new[] { "C#", "Python", "SQL" }, turn.Filter.Skills);
    }

    [Fact]
    public async Task PrepareAsync_ClearKeys_RemoveCriterionFromPreviousFilter()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        AIConsultantService service = CreateService();
        _llm.Extract.Respond("{\"min_salary\":2000,\"work_format\":[\"remote\"]}");
        AIConsultantTurn first = await service.PrepareAsync(_userId, null, "віддалено, від 2000$");

        // Act
        _llm.Extract.Respond("{\"clear\":[\"min_salary\",\"work_format\",\"unknown_key\"]}");
        AIConsultantTurn second = await service.PrepareAsync(_userId, first.SessionId, "зарплата і формат не важливі");

        // Assert
        Assert.Null(second.Filter.MinSalary);
        Assert.Null(second.Filter.WorkFormat);
    }

    [Fact]
    public async Task PrepareAsync_ForeignSession_ThrowsBeforeCallingLlm()
    {
        // Arrange
        ChatSessionDB foreignSession = await _chatRepository.CreateSessionAsync(Guid.NewGuid(), "{}");
        AIConsultantService service = CreateService();

        // Act
        Task<AIConsultantTurn> action = service.PrepareAsync(_userId, foreignSession.Id, "привіт");

        // Assert
        await Assert.ThrowsAsync<ChatSessionNotFoundException>(() => action);
        Assert.Equal(0, _llm.Extract.CallCount);
        Assert.Single(_chatRepository.Sessions);
    }

    [Fact]
    public async Task PrepareAsync_ExtractionFailure_PropagatesLlmUnavailableAndCreatesNoSession()
    {
        // Arrange
        _llm.Extract.Throw(new LlmUnavailableException(LlmFailureKind.NotConfigured));
        AIConsultantService service = CreateService();

        // Act
        Task<AIConsultantTurn> action = service.PrepareAsync(_userId, null, "шукаю python");

        // Assert
        LlmUnavailableException exception = await Assert.ThrowsAsync<LlmUnavailableException>(() => action);
        Assert.Equal(LlmFailureKind.NotConfigured, exception.Kind);
        Assert.Empty(_chatRepository.Sessions);
        Assert.Empty(_chatRepository.Messages);
    }

    [Fact]
    public async Task PrepareAsync_UnparsableStructuredOutput_BecomesLlmUnavailable()
    {
        // Arrange
        _llm.Extract.Respond("це не JSON");
        AIConsultantService service = CreateService();

        // Act
        Task<AIConsultantTurn> action = service.PrepareAsync(_userId, null, "шукаю python");

        // Assert
        LlmUnavailableException exception = await Assert.ThrowsAsync<LlmUnavailableException>(() => action);
        Assert.Equal(LlmFailureKind.ServerError, exception.Kind);
    }

    [Fact]
    public async Task PrepareAsync_UserTextWithContacts_IsMaskedBeforeLlm()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        _llm.Extract.Respond("{}");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn first = await service.PrepareAsync(
            _userId,
            null,
            "я Іван, email ivan@example.com, тел +380 67 111 22 33, шукаю python");

        // Assert
        string extractionText = Texts(_llm.Extract.Calls[0].Messages);
        Assert.Contains("[email приховано]", extractionText, StringComparison.Ordinal);
        Assert.Contains("[телефон приховано]", extractionText, StringComparison.Ordinal);
        Assert.DoesNotContain("ivan@example.com", extractionText, StringComparison.Ordinal);
        Assert.DoesNotContain("67 111 22 33", extractionText, StringComparison.Ordinal);

        // У власній БД текст зберігається без маскування — маскування лише на межі LLM
        Assert.Contains("ivan@example.com", _chatRepository.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal(first.SessionId, _chatRepository.Messages[0].SessionId);
    }

    [Fact]
    public async Task PrepareAsync_HistoryMessages_AreMaskedBeforeLlm()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        AIConsultantService service = CreateService();
        _llm.Extract.Respond("{}");
        AIConsultantTurn first = await service.PrepareAsync(_userId, null, "мій email ivan@example.com");

        // Act
        _llm.Extract.Respond("{\"min_salary\":1000}");
        await service.PrepareAsync(_userId, first.SessionId, "додай зарплату від 1000");

        // Assert
        string secondCall = Texts(_llm.Extract.Calls[1].Messages);
        Assert.Contains("[email приховано]", secondCall, StringComparison.Ordinal);
        Assert.DoesNotContain("ivan@example.com", secondCall, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_NeverSendsUserIdentityToLlm()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        _llm.Extract.Respond("{}");
        _llm.Chat.Stream("Ось вакансія.");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "шукаю python");
        await ConsumeAnswerAsync(service, turn);

        // Assert
        string allPrompts = string.Join(
            "\n",
            _llm.Extract.Calls.SelectMany(call => call.Messages)
                .Concat(_llm.Chat.Calls.SelectMany(call => call.Messages))
                .Select(MessageText));

        Assert.DoesNotContain(_userId.ToString(), allPrompts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("seeker@waterfall", allPrompts, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAsync_HistoryWindow_LimitsContextSentToLlm()
    {
        // Arrange
        ChatSessionDB session = await _chatRepository.CreateSessionAsync(_userId, "{}");
        for (int index = 0; index < 5; index++)
        {
            await _chatRepository.AddUserMessageAsync(session.Id, _userId, $"старе повідомлення {index}");
        }

        _llm.Extract.Respond("{}");
        AIConsultantService service = CreateService(new AiConsultantOptions { HistoryMessages = 2 });

        // Act
        await service.PrepareAsync(_userId, session.Id, "нове повідомлення");

        // Assert
        IReadOnlyList<ChatMessage> sent = _llm.Extract.Calls[0].Messages;
        Assert.Equal(4, sent.Count); // system + 2 повідомлення історії + нове повідомлення
        Assert.Contains("старе повідомлення 3", Texts(sent), StringComparison.Ordinal);
        Assert.DoesNotContain("старе повідомлення 0", Texts(sent), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_ExactMatches_DoNotRelaxAnyCriterion()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        _llm.Extract.Respond("{\"search\":\"python\"}");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "шукаю python");

        // Assert
        Assert.True(turn.Exact);
        Assert.Empty(turn.Relaxed);
        Assert.Null(turn.Disclaimer);
        Assert.Single(turn.Vacancies);
        Assert.Single(_catalog.Queries);
    }

    [Fact]
    public async Task PrepareAsync_Relaxation_DropsTheCriterionThatUnblocksTheMostResults()
    {
        // Arrange: жодна вакансія не проходить усі критерії, але зняття min_salary відкриває дві
        _catalog.Vacancies.AddRange(
        [
            CreateVacancy("Python A", WorkFormat.Remote, 12, 1000),
            CreateVacancy("Python D", WorkFormat.Remote, 6, 900),
            CreateVacancy("Python B", WorkFormat.Office, 12, 5000, 6000),
            CreateVacancy("Python C", WorkFormat.Remote, 60, 5000, 6000)
        ]);
        _llm.Extract.Respond(
            "{\"work_format\":[\"remote\"],\"max_experience_months\":12,\"min_salary\":3000,\"skills\":[\"Python\"]}");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "віддалено, джуніор, від 3000, python");

        // Assert
        Assert.False(turn.Exact);
        Assert.Equal(new[] { "min_salary" }, turn.Relaxed);
        Assert.Equal(2, turn.Vacancies.Count);
        Assert.Contains(turn.Vacancies, vacancy => vacancy.Title == "Python A");
        Assert.Contains(turn.Vacancies, vacancy => vacancy.Title == "Python D");
        Assert.NotNull(turn.Disclaimer);
        Assert.Contains("Точних збігів не знайдено", turn.Disclaimer!, StringComparison.Ordinal);
        Assert.Contains("не на 100%", turn.Disclaimer!, StringComparison.Ordinal);
        Assert.Contains("мінімальна зарплата", turn.Disclaimer!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_Relaxation_DropsConditionsBeforeTheSubjectOfTheQuery()
    {
        // Arrange: «Python Developer» провалює дві умови (формат і зарплату), «Java Developer» — суть запиту
        _catalog.Vacancies.AddRange(
        [
            CreateVacancy("Python Developer", WorkFormat.Office, 12, 1000),
            CreateVacancy("Java Developer", WorkFormat.Remote, 12, 5000, 6000, skills: ["Java"])
        ]);
        _llm.Extract.Respond("{\"search\":\"python\",\"work_format\":[\"remote\"],\"min_salary\":3000,\"skills\":[\"python\"]}");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "python, віддалено, від 3000");

        // Assert: знято лише умови, python лишився
        Assert.False(turn.Exact);
        Assert.Equal(new[] { "work_format", "min_salary" }, turn.Relaxed);
        Assert.Single(turn.Vacancies);
        Assert.Equal("Python Developer", turn.Vacancies[0].Title);
    }

    [Fact]
    public async Task PrepareAsync_Relaxation_KeepsTheProfessionEvenWhenDroppingItUnblocksMore()
    {
        // Arrange: у Львові є лише розробники (їх більше), бухгалтер є лише віддалено з Києва
        _catalog.Vacancies.AddRange(
        [
            CreateVacancy("Бухгалтер", WorkFormat.Remote, 12, 1000, location: "Київ", categoryGroup: "Finance"),
            CreateVacancy("Developer A", WorkFormat.Office, 12, 2000, location: "Львів"),
            CreateVacancy("Developer B", WorkFormat.Office, 12, 2000, location: "Львів")
        ]);
        _llm.Extract.Respond("{\"category_group\":\"Finance\",\"location\":\"Львів\"}");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "бухгалтер у Львові");

        // Assert
        Assert.False(turn.Exact);
        Assert.Equal(new[] { "location" }, turn.Relaxed);
        Assert.Equal("Бухгалтер", Assert.Single(turn.Vacancies).Title);
    }

    [Fact]
    public async Task PrepareAsync_EmptyCatalogWithCriteria_ReportsNoResultsWithoutInventingVacancies()
    {
        // Arrange
        _llm.Extract.Respond("{\"search\":\"python\"}");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "шукаю python");

        // Assert
        Assert.Empty(turn.Vacancies);
        Assert.False(turn.Exact);
        Assert.NotNull(turn.Disclaimer);
        Assert.Contains("Точних збігів не знайдено", turn.Disclaimer!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_EmptyCatalogWithoutCriteria_StaysExact()
    {
        // Arrange
        _llm.Extract.Respond("{}");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "привіт");

        // Assert
        Assert.Empty(turn.Vacancies);
        Assert.True(turn.Exact);
        Assert.Empty(turn.Relaxed);
        Assert.Null(turn.Disclaimer);
    }

    [Fact]
    public async Task PrepareAsync_SummaryPrompt_ContainsExactlyTheReturnedVacancies()
    {
        // Arrange
        VacancyDto relevant = CreateVacancy("Python Developer");
        VacancyDto irrelevant = CreateVacancy("Java Architect");
        _catalog.Vacancies.AddRange([relevant, irrelevant]);
        _llm.Extract.Respond("{\"search\":\"python\"}");
        _llm.Chat.Stream("Ось вакансія.");
        AIConsultantService service = CreateService();

        // Act
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "шукаю python");
        await ConsumeAnswerAsync(service, turn);

        // Assert
        Assert.Single(turn.Vacancies);
        string prompt = Texts(_llm.Chat.Calls[0].Messages);
        Assert.Contains(relevant.Id.ToString(), prompt, StringComparison.Ordinal);
        Assert.Contains(relevant.Title, prompt, StringComparison.Ordinal);
        Assert.Contains(relevant.Company, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(irrelevant.Title, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(irrelevant.Id.ToString(), prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAnswerAsync_PersistsFullTextAndExactlyTheSentVacancyLinks()
    {
        // Arrange
        VacancyDto vacancy = CreateVacancy("Python Developer");
        _catalog.Vacancies.Add(vacancy);
        _llm.Extract.Respond("{\"search\":\"python\"}");
        _llm.Chat.Stream("Знайшов ", "одну вакансію.");
        AIConsultantService service = CreateService();
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "шукаю python");

        // Act
        (string text, long messageId) = await ConsumeAnswerAsync(service, turn);

        // Assert
        Assert.Equal("Знайшов одну вакансію.", text);
        ChatMessageDB assistant = Assert.Single(
            _chatRepository.Messages,
            message => message.Author == ChatMessageAuthor.Assistant);
        Assert.Equal(messageId, assistant.Id);
        Assert.Equal(text, assistant.Content);
        ChatMessageVacancyDB link = Assert.Single(_chatRepository.Links);
        Assert.Equal(messageId, link.MessageId);
        Assert.Equal(vacancy.Id, link.VacancyId);
    }

    [Fact]
    public async Task StreamAnswerAsync_SummaryFailure_ThrowsAndPersistsNoAssistantMessage()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        _llm.Extract.Respond("{\"search\":\"python\"}");
        _llm.Chat.StreamThenThrow(new LlmUnavailableException(LlmFailureKind.Timeout), "Частина відповіді");
        AIConsultantService service = CreateService();
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "шукаю python");

        // Act
        Task<(string Text, long MessageId)> action = ConsumeAnswerAsync(service, turn);

        // Assert
        await Assert.ThrowsAsync<LlmUnavailableException>(() => action);
        Assert.DoesNotContain(_chatRepository.Messages, message => message.Author == ChatMessageAuthor.Assistant);
        Assert.Empty(_chatRepository.Links);
        Assert.Single(_chatRepository.Messages); // повідомлення користувача збережене
    }

    [Fact]
    public async Task StreamAnswerAsync_DeletedSessionMidStream_Throws()
    {
        // Arrange
        _catalog.Vacancies.Add(CreateVacancy("Python Developer"));
        _llm.Extract.Respond("{\"search\":\"python\"}");
        _llm.Chat.Stream("Відповідь");
        AIConsultantService service = CreateService();
        AIConsultantTurn turn = await service.PrepareAsync(_userId, null, "шукаю python");
        await _chatRepository.DeleteSessionAsync(turn.SessionId, _userId);

        // Act
        Task<(string Text, long MessageId)> action = ConsumeAnswerAsync(service, turn);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => action);
    }

    [Fact]
    public async Task DeleteSessionAsync_ForeignOwner_ReturnsFalseAndKeepsSession()
    {
        // Arrange
        ChatSessionDB session = await _chatRepository.CreateSessionAsync(_userId, "{}");
        AIConsultantService service = CreateService();

        // Act
        bool deleted = await service.DeleteSessionAsync(session.Id, Guid.NewGuid());

        // Assert
        Assert.False(deleted);
        Assert.Single(_chatRepository.Sessions);

        // Act
        bool ownDeleted = await service.DeleteSessionAsync(session.Id, _userId);

        // Assert
        Assert.True(ownDeleted);
        Assert.Empty(_chatRepository.Sessions);
    }
}
