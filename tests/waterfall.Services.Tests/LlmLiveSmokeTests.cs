// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Waterfall.Services.Llm;

namespace Waterfall.Services.Tests;

/// <summary>
/// Живий димовий тест (вимкнений за замовчуванням): для кожного провайдера, ключ якого
/// присутній у середовищі або User Secrets, перевіряє структуровану відповідь за
/// JSON-схемою та потокову відповідь. Потрібен, щоб перевірити реальні endpoint-и й
/// формати після зміни моделей; у CI не запускається (див. <see cref="LiveLlmFactAttribute"/>).
/// </summary>
public sealed class LlmLiveSmokeTests
{
    [LiveLlmFact]
    public async Task EveryConfiguredProviderWithKey_AnswersStructuredOutputAndStreaming()
    {
        // Arrange
        IConfigurationRoot configuration = BuildConfiguration();
        LlmOptions options = configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();

        List<string> failures = [];
        int checkedProviders = 0;

        foreach ((string providerName, LlmProviderOptions provider) in options.Providers)
        {
            string? apiKey = LlmApiKeyResolver.Resolve(provider);
            if (apiKey is null)
            {
                continue;
            }

            string? model = FindModel(options, providerName);
            if (model is null)
            {
                failures.Add($"{providerName}: немає жодної моделі в ланцюжках ролей");
                continue;
            }

            checkedProviders++;
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(60));

            try
            {
                using IChatClient client = LlmClientFactory.Create(provider, model, apiKey);

                ChatResponse<SmokeAnswer> response = await client.GetResponseAsync<SmokeAnswer>(
                    [new ChatMessage(ChatRole.User, "Поверни лише JSON {\"ok\":true}, без пояснень і розмітки.")],
                    new ChatOptions { Temperature = 0 },
                    cancellationToken: cts.Token);

                if (!response.Result.Ok)
                {
                    failures.Add($"{providerName}: структурована відповідь ok=false");
                }

                StringBuilder streamed = new();
                await foreach (ChatResponseUpdate update in client.GetStreamingResponseAsync(
                    [new ChatMessage(ChatRole.User, "Скажи одне слово: привіт")],
                    new ChatOptions { Temperature = 0 },
                    cts.Token))
                {
                    streamed.Append(update.Text);
                }

                if (streamed.Length == 0)
                {
                    failures.Add($"{providerName}: порожній потік");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cts.IsCancellationRequested)
            {
                failures.Add($"{providerName}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        // Assert
        Assert.True(checkedProviders > 0, "Жоден LLM-провайдер не має ключа — живий тест нічого не перевірив.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string? FindModel(LlmOptions options, string providerName)
    {
        foreach (LlmRoleOptions role in options.Roles.Values)
        {
            LlmChainEntryOptions? entry = role.Chain.FirstOrDefault(
                candidate => string.Equals(candidate.Provider, providerName, StringComparison.OrdinalIgnoreCase));

            if (entry is not null)
            {
                return entry.Model;
            }
        }

        return null;
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        ConfigurationBuilder builder = new();

        string? repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is not null)
        {
            builder.AddJsonFile(
                Path.Combine(repositoryRoot, "src", "api", "appsettings.json"),
                optional: true,
                reloadOnChange: false);
        }

        // Той самий UserSecretsId, що й у src/api, плюс змінні середовища (вони мають вищий пріоритет).
        builder.AddUserSecrets(userSecretsId: "afb13ba0-fcf6-481a-9bf7-b9349ab137a1", reloadOnChange: false);
        builder.AddEnvironmentVariables();
        return builder.Build();
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "waterfall.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

/// <summary>Мінімальна структурована відповідь для перевірки JSON-схеми.</summary>
internal sealed record SmokeAnswer
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }
}
