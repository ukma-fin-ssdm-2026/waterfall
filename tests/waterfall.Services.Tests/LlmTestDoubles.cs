// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using Anthropic.Exceptions;

namespace Waterfall.Services.Tests;

/// <summary>
/// Реальні типи винятків офіційних SDK із заданим HTTP-статусом — щоб тести перевіряли
/// саме ту класифікацію, яку шлюз застосовує в бою, а не вигадані підкласи.
/// </summary>
internal static class LlmTestExceptions
{
    public static ClientResultException OpenAiStatus(int status)
        => new($"HTTP {status}", new FakePipelineResponse(status), null);

    public static AnthropicApiException AnthropicStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => new AnthropicRateLimitException
        {
            StatusCode = status,
            ResponseBody = "{}"
        },
        >= HttpStatusCode.InternalServerError => new Anthropic5xxException
        {
            StatusCode = status,
            ResponseBody = "{}"
        },
        _ => new Anthropic4xxException
        {
            StatusCode = status,
            ResponseBody = "{}"
        }
    };
}

/// <summary>Мінімальна відповідь конвеєра System.ClientModel із потрібним статусом.</summary>
internal sealed class FakePipelineResponse : PipelineResponse
{
    private readonly int _status;

    public FakePipelineResponse(int status)
    {
        _status = status;
    }

    public override int Status => _status;

    public override string ReasonPhrase => string.Empty;

    public override Stream? ContentStream { get; set; }

    public override BinaryData Content => BinaryData.FromString("{}");

    protected override PipelineResponseHeaders HeadersCore { get; } = new FakePipelineResponseHeaders();

    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;

    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default)
        => new(Content);

    public override void Dispose()
    {
    }
}

internal sealed class FakePipelineResponseHeaders : PipelineResponseHeaders
{
    public override bool TryGetValue(string name, out string? value)
    {
        value = null;
        return false;
    }

    public override bool TryGetValues(string name, out IEnumerable<string>? values)
    {
        values = null;
        return false;
    }

    public override IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
}
