// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Waterfall.Services.Llm;

namespace Waterfall.Services.Tests;

/// <summary>
/// Провайдер клієнтів не падає на старті без ключів: роль без придатної ланки
/// віддає клієнт, який повідомляє <see cref="LlmFailureKind.NotConfigured"/> під час використання (REL-003).
/// </summary>
public sealed class LlmClientProviderTests
{
    [Fact]
    public async Task GetClient_WhenNoProviderHasKey_ThrowsNotConfiguredOnUse()
    {
        // Arrange: назва змінної унікальна, щоб жодне реальне середовище її не заповнило
        LlmOptions options = new()
        {
            Providers =
            {
                ["openai"] = new LlmProviderOptions
                {
                    Kind = LlmProviderKinds.OpenAiCompatible,
                    BaseUrl = "https://api.openai.com/v1",
                    ApiKeyEnv = $"WATERFALL_TEST_MISSING_KEY_{Guid.NewGuid():N}"
                }
            },
            Roles =
            {
                ["Chat"] = new LlmRoleOptions
                {
                    Chain = [new LlmChainEntryOptions { Provider = "openai", Model = "gpt-4.1" }]
                }
            }
        };
        LlmClientProvider provider = CreateProvider(options);
        IChatClient client = provider.GetClient(LlmRole.Chat);

        // Act
        LlmUnavailableException exception = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "привіт")]));

        // Assert
        Assert.Equal(LlmFailureKind.NotConfigured, exception.Kind);
    }

    [Fact]
    public async Task GetClient_WhenRoleIsNotConfigured_ThrowsNotConfiguredOnUse()
    {
        // Arrange
        LlmClientProvider provider = CreateProvider(new LlmOptions());
        IChatClient client = provider.GetClient(LlmRole.Analyze);

        // Act
        LlmUnavailableException exception = await Assert.ThrowsAsync<LlmUnavailableException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "привіт")]));

        // Assert
        Assert.Equal(LlmFailureKind.NotConfigured, exception.Kind);
    }

    [Fact]
    public async Task GetClient_WhenChainIsEmpty_ThrowsNotConfiguredOnStreamingUse()
    {
        // Arrange
        LlmOptions options = new()
        {
            Roles = { ["Extract"] = new LlmRoleOptions() }
        };
        LlmClientProvider provider = CreateProvider(options);
        IChatClient client = provider.GetClient(LlmRole.Extract);

        // Act
        IAsyncEnumerator<ChatResponseUpdate> enumerator = client
            .GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "привіт")])
            .GetAsyncEnumerator();

        // Assert
        LlmUnavailableException exception = await Assert.ThrowsAsync<LlmUnavailableException>(
            async () =>
            {
                await enumerator.MoveNextAsync();
            });
        Assert.Equal(LlmFailureKind.NotConfigured, exception.Kind);
        await enumerator.DisposeAsync();
    }

    private static LlmClientProvider CreateProvider(LlmOptions options)
    {
        return new LlmClientProvider(
            Options.Create(options),
            NullLogger<LlmClientProvider>.Instance,
            NullLogger<FallbackChatClient>.Instance);
    }
}
