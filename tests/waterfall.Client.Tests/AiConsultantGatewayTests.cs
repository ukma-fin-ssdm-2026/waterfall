// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Waterfall.Client;
using Waterfall.Client.Models.Ai;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.Services;

namespace Waterfall.Client.Tests;

/// <summary>
/// Розбір SSE-потоку ШІ-консультанта: фреймінг (коментарі, багаторядкові data:, подія на порожньому
/// рядку), розриви подій між мережевими чанками, типізовані помилки до початку потоку.
/// </summary>
public class AiConsultantGatewayTests
{
    private readonly StubSseHandler _server = new();
    private readonly HttpGatewayService _gateway;

    public AiConsultantGatewayTests()
    {
        _gateway = new HttpGatewayService(
            new HttpClient(_server) { BaseAddress = new Uri("http://api.test:3000") },
            new InMemoryTokenStore());
    }

    [Fact]
    public async Task SendAiMessage_ParsesEventsSplitAcrossChunks()
    {
        // Кожен Read віддає не більше 5 байтів: подія гарантовано розривається посеред JSON.
        _server.Stream(
            """
            : keep-alive

            event: meta
            data: {"session_id":"11111111-1111-1111-1111-111111111111","applied_filter":{"search":"python","category_group":"IT","work_format":["remote","hybrid"],"max_experience_months":12,"min_salary":1000,"location":"Київ","skills":["Python"]}}

            event: vacancies
            data: {"items":[{"id":"22222222-2222-2222-2222-222222222222","title":"Python Developer","company":"ACME","salaryMin":1200,"skills":["Python"]}],"exact":false,"relaxed":["skills"],"disclaimer":"Точних збігів не знайдено"}

            event: delta
            data: {"text":"Привіт"}

            event: delta
            data: {"text":
            data: "!"}

            event: done
            data: {"message_id":42}


            """,
            chunkSize: 5);

        List<AiStreamEvent> events = await Collect(_gateway.SendAiMessageAsync(null, "python junior"));

        Assert.Equal(5, events.Count);

        AiMetaEvent meta = Assert.IsType<AiMetaEvent>(events[0]);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), meta.SessionId);
        Assert.Equal("python", meta.AppliedFilter?.Search);
        Assert.Equal(new[] { "remote", "hybrid" }, meta.AppliedFilter!.WorkFormat);
        Assert.Equal(12, meta.AppliedFilter?.MaxExperienceMonths);
        Assert.Equal(1000m, meta.AppliedFilter?.MinSalary);
        Assert.Equal("Київ", meta.AppliedFilter?.Location);

        AiVacanciesEvent vacancies = Assert.IsType<AiVacanciesEvent>(events[1]);
        Assert.False(vacancies.Exact);
        Assert.Equal(new[] { "skills" }, vacancies.Relaxed);
        Assert.Equal("Точних збігів не знайдено", vacancies.Disclaimer);
        AssertVacancyParsed(vacancies);

        Assert.Equal("Привіт", Assert.IsType<AiDeltaEvent>(events[2]).Text);
        Assert.Equal("!", Assert.IsType<AiDeltaEvent>(events[3]).Text);
        Assert.Equal(42, Assert.IsType<AiDoneEvent>(events[4]).MessageId);

        StubSseHandler.Recorded request = Assert.Single(_server.Requests);
        Assert.Equal("http://api.test:3000/api/v1/ai-consultant/messages", request.Uri);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("session_id").ValueKind);
        Assert.Equal("python junior", body.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendAiMessage_ExistingSession_SendsSessionIdInSnakeCase()
    {
        Guid sessionId = Guid.NewGuid();
        _server.Stream("event: done\ndata: {\"message_id\":1}\n\n");

        List<AiStreamEvent> events = await Collect(_gateway.SendAiMessageAsync(sessionId, "уточнення"));

        Assert.IsType<AiDoneEvent>(Assert.Single(events));
        using JsonDocument body = JsonDocument.Parse(_server.Requests.Single().Body!);
        Assert.Equal(sessionId, body.RootElement.GetProperty("session_id").GetGuid());
    }

    [Fact]
    public async Task SendAiMessage_UnknownEventType_IsSkipped()
    {
        _server.Stream("event: future_payload\ndata: {\"anything\":1}\n\nevent: done\ndata: {\"message_id\":5}\n\n");

        List<AiStreamEvent> events = await Collect(_gateway.SendAiMessageAsync(null, "test"));

        Assert.Equal(5, Assert.IsType<AiDoneEvent>(Assert.Single(events)).MessageId);
    }

    [Fact]
    public async Task SendAiMessage_MalformedEventJson_ThrowsInvalidResponse()
    {
        _server.Stream("event: delta\ndata: {oops\n\n");

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => Collect(_gateway.SendAiMessageAsync(null, "test")));

        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal("Сервер повернув некоректну відповідь.", ex.Message);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "LLM_UNAVAILABLE", "тимчасово недоступний")]
    [InlineData(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "ліміт")]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "Шукач роботи")]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHORIZED", "увійти")]
    public async Task SendAiMessage_PreStreamError_ThrowsTypedExceptionWithCode(
        HttpStatusCode status,
        string expectedCode,
        string expectedMessagePart)
    {
        _server.Respond(status, "");

        AiConsultantException ex = await Assert.ThrowsAsync<AiConsultantException>(
            () => Collect(_gateway.SendAiMessageAsync(null, "test")));

        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(expectedCode, ex.Code);
        Assert.Contains(expectedMessagePart, ex.Message);
        Assert.Equal(status == HttpStatusCode.ServiceUnavailable, ex.IsLlmUnavailable);
        Assert.Equal(status == HttpStatusCode.TooManyRequests, ex.IsRateLimited);
    }

    [Fact]
    public async Task SendAiMessage_ErrorEnvelope_KeepsServerCodeAndMessage()
    {
        _server.Respond(HttpStatusCode.ServiceUnavailable, """
            { "error": { "code": "LLM_UNAVAILABLE", "message": "LLM тимчасово недоступна.", "request_id": "req-9" } }
            """);

        AiConsultantException ex = await Assert.ThrowsAsync<AiConsultantException>(
            () => Collect(_gateway.SendAiMessageAsync(null, "test")));

        Assert.Equal("LLM_UNAVAILABLE", ex.Code);
        Assert.Equal("LLM тимчасово недоступна.", ex.Message);
        Assert.Equal("req-9", ex.Error?.RequestId);
        Assert.True(ex.IsLlmUnavailable);
    }

    [Fact]
    public async Task AnalyzeVacancies_SendsIdsAndParsesRelevance()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        _server.Stream(
            $$"""
            event: relevance
            data: {"items":[{"vacancy_id":"{{first}}","score":73,"matched":["Python","remote"],"basis_count":4},{"vacancy_id":"{{second}}","score":null,"matched":[],"basis_count":0}]}

            event: delta
            data: {"text":"Ключові вимоги"}

            event: done
            data: {}


            """);

        List<AiStreamEvent> events = await Collect(_gateway.AnalyzeVacanciesAsync([first, second]));

        AiRelevanceEvent relevance = Assert.IsType<AiRelevanceEvent>(events[0]);
        Assert.Equal(2, relevance.Items.Count);
        Assert.Equal((first, 73, 4), (relevance.Items[0].VacancyId, relevance.Items[0].Score, relevance.Items[0].BasisCount));
        Assert.Equal(new[] { "Python", "remote" }, relevance.Items[0].Matched);
        Assert.Null(relevance.Items[1].Score);
        Assert.Equal("Ключові вимоги", Assert.IsType<AiDeltaEvent>(events[1]).Text);
        Assert.Null(Assert.IsType<AiDoneEvent>(events[2]).MessageId);

        StubSseHandler.Recorded request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://api.test:3000/api/v1/ai-consultant/analyze", request.Uri);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal(new[] { first, second }, body.RootElement.GetProperty("vacancy_ids").EnumerateArray().Select(e => e.GetGuid()).ToArray());
    }

    [Fact]
    public async Task AnalyzeVacancies_PreStreamCurrencyError_IsTyped()
    {
        _server.Respond(HttpStatusCode.NotFound, """
            { "error": { "code": "NOT_FOUND", "message": "Вакансію не знайдено." } }
            """);

        AiConsultantException ex = await Assert.ThrowsAsync<AiConsultantException>(
            () => Collect(_gateway.AnalyzeVacanciesAsync([Guid.NewGuid()])));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("NOT_FOUND", ex.Code);
        Assert.Equal("Вакансію не знайдено.", ex.Message);
    }

    [Fact]
    public async Task DeleteAiSession_SendsDeleteRequest()
    {
        Guid sessionId = Guid.NewGuid();
        _server.Respond(HttpStatusCode.NoContent, "");

        await _gateway.DeleteAiSessionAsync(sessionId);

        StubSseHandler.Recorded request = Assert.Single(_server.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"http://api.test:3000/api/v1/ai-consultant/sessions/{sessionId}", request.Uri);
    }

    [Fact]
    public async Task DeleteAiSession_MissingSession_ThrowsNotFound()
    {
        _server.Respond(HttpStatusCode.NotFound, "");

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => _gateway.DeleteAiSessionAsync(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task SendAiMessage_CallerCancellation_IsNotWrapped()
    {
        _server.Stream("event: delta\ndata: {\"text\":\"a\"}\n\n", chunkSize: 3);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Collect(_gateway.SendAiMessageAsync(null, "test", cts.Token)));
    }

    private static void AssertVacancyParsed(AiVacanciesEvent vacancies)
    {
        VacancyDto vacancy = Assert.Single(vacancies.Items);
        Assert.Equal("Python Developer", vacancy.Title);
        Assert.Equal("ACME", vacancy.Company);
        Assert.Equal(1200m, vacancy.SalaryMin);
        Assert.Equal(new[] { "Python" }, vacancy.Skills);
    }

    private static async Task<List<AiStreamEvent>> Collect(IAsyncEnumerable<AiStreamEvent> stream)
    {
        List<AiStreamEvent> events = [];
        await foreach (AiStreamEvent streamEvent in stream)
        {
            events.Add(streamEvent);
        }

        return events;
    }

    /// <summary>
    /// Мережевий обробник-заглушка: віддає тіло відповіді шматками заданого розміру.
    /// </summary>
    private sealed class StubSseHandler : HttpMessageHandler
    {
        private Func<HttpResponseMessage> _next = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        public record Recorded(HttpMethod Method, string Uri, string? Body);

        public List<Recorded> Requests { get; } = [];

        public void Stream(string payload, int chunkSize = 16)
        {
            _next = () =>
            {
                StreamContent content = new(new ChunkedStream(Encoding.UTF8.GetBytes(payload), chunkSize));
                content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            };
        }

        public void Respond(HttpStatusCode status, string body) =>
            _next = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded(request.Method, request.RequestUri!.AbsoluteUri, body));
            return _next();
        }
    }

    /// <summary>
    /// Потік, що віддає не більше <c>chunkSize</c> байтів за один Read — імітація мережевих чанків,
    /// які рвуть SSE-подію в довільному місці.
    /// </summary>
    private sealed class ChunkedStream(byte[] payload, int chunkSize) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => payload.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = Math.Min(Math.Min(count, chunkSize), payload.Length - _position);
            if (available <= 0)
            {
                return 0;
            }

            Array.Copy(payload, _position, buffer, offset, available);
            _position += available;
            return available;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            int available = Math.Min(Math.Min(buffer.Length, chunkSize), payload.Length - _position);
            if (available <= 0)
            {
                return ValueTask.FromResult(0);
            }

            payload.AsMemory(_position, available).CopyTo(buffer);
            _position += available;
            return ValueTask.FromResult(available);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
