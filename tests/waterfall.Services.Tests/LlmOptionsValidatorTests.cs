// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.Options;
using Waterfall.Services.Llm;

namespace Waterfall.Services.Tests;

/// <summary>
/// Валідатор секції <c>Llm</c> відхиляє лише структурні помилки: відсутність ключів
/// не є помилкою (REL-003), але невідомий провайдер або безглузді таймаути — так.
/// </summary>
public sealed class LlmOptionsValidatorTests
{
    private readonly LlmOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsNoKeysConfiguration()
    {
        // Arrange: провайдери описані, ключів немає — це штатний стан для запуску без секретів
        LlmOptions options = CreateValidOptions();

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_AcceptsEmptyProvidersAndRoles()
    {
        // Arrange
        LlmOptions options = new();

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_RejectsUnknownProviderKind()
    {
        // Arrange
        LlmOptions options = CreateValidOptions();
        options.Providers["openrouter"].Kind = "Gemini";

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Kind", string.Join(" ", result.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsChainEntryWithUnknownProvider()
    {
        // Arrange
        LlmOptions options = CreateValidOptions();
        options.Roles["Chat"].Chain.Add(new LlmChainEntryOptions { Provider = "unknown-provider", Model = "m" });

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("unknown-provider", string.Join(" ", result.Failures), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/relative/url")]
    [InlineData("openrouter.ai/api/v1")]
    public void Validate_RejectsNonAbsoluteBaseUrl(string baseUrl)
    {
        // Arrange
        LlmOptions options = CreateValidOptions();
        options.Providers["openrouter"].BaseUrl = baseUrl;

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("BaseUrl", string.Join(" ", result.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAttemptTimeoutGreaterThanTotalTimeout()
    {
        // Arrange
        LlmOptions options = CreateValidOptions();
        options.AttemptTimeout = TimeSpan.FromSeconds(31);
        options.TotalTimeout = TimeSpan.FromSeconds(30);

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("AttemptTimeout", string.Join(" ", result.Failures), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_RejectsNonPositiveAttemptTimeout(int seconds)
    {
        // Arrange
        LlmOptions options = CreateValidOptions();
        options.AttemptTimeout = TimeSpan.FromSeconds(seconds);

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("AttemptTimeout", string.Join(" ", result.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsNonPositiveTotalTimeout()
    {
        // Arrange
        LlmOptions options = CreateValidOptions();
        options.TotalTimeout = TimeSpan.Zero;

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("TotalTimeout", string.Join(" ", result.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsUnknownRoleName()
    {
        // Arrange
        LlmOptions options = CreateValidOptions();
        options.Roles["Translate"] = new LlmRoleOptions
        {
            Chain = [new LlmChainEntryOptions { Provider = "openrouter", Model = "m" }]
        };

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Translate", string.Join(" ", result.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsChainEntryWithoutModel()
    {
        // Arrange
        LlmOptions options = CreateValidOptions();
        options.Roles["Chat"].Chain[0].Model = " ";

        // Act
        ValidateOptionsResult result = _validator.Validate(null, options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("Model", string.Join(" ", result.Failures), StringComparison.Ordinal);
    }

    private static LlmOptions CreateValidOptions()
    {
        return new LlmOptions
        {
            Providers =
            {
                ["openrouter"] = new LlmProviderOptions
                {
                    Kind = LlmProviderKinds.OpenAiCompatible,
                    BaseUrl = "https://openrouter.ai/api/v1",
                    ApiKeyEnv = "OPENROUTER_API_KEY"
                }
            },
            Roles =
            {
                ["Extract"] = new LlmRoleOptions
                {
                    Chain = [new LlmChainEntryOptions { Provider = "openrouter", Model = "google/gemini-2.5-flash" }]
                },
                ["Chat"] = new LlmRoleOptions
                {
                    Chain = [new LlmChainEntryOptions { Provider = "openrouter", Model = "anthropic/claude-sonnet-4" }]
                },
                ["Analyze"] = new LlmRoleOptions
                {
                    Chain = [new LlmChainEntryOptions { Provider = "openrouter", Model = "openai/gpt-4.1" }]
                }
            },
            AttemptTimeout = TimeSpan.FromSeconds(12),
            TotalTimeout = TimeSpan.FromSeconds(30)
        };
    }
}
