// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.Extensions.Options;

namespace Waterfall.Services.Llm;

/// <summary>
/// Перевіряє лише структурні помилки секції <c>Llm</c>: невідомий тип провайдера,
/// посилання на невідомого провайдера, неабсолютний <c>BaseUrl</c>, некоректні таймаути.
/// Відсутність ключів помилкою не є: API мусить запускатися без них (REL-003), а роль
/// без жодної придатної ланки повідомляє про <see cref="LlmFailureKind.NotConfigured"/> під час використання.
/// </summary>
public sealed class LlmOptionsValidator : IValidateOptions<LlmOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, LlmOptions options)
    {
        List<string> errors = [];

        if (options.AttemptTimeout <= TimeSpan.Zero)
        {
            errors.Add($"Llm:AttemptTimeout має бути додатним, а задано {options.AttemptTimeout:c}");
        }

        if (options.TotalTimeout <= TimeSpan.Zero)
        {
            errors.Add($"Llm:TotalTimeout має бути додатним, а задано {options.TotalTimeout:c}");
        }

        if (options.AttemptTimeout > options.TotalTimeout)
        {
            errors.Add(
                $"Llm:AttemptTimeout ({options.AttemptTimeout:c}) не може перевищувати Llm:TotalTimeout ({options.TotalTimeout:c})");
        }

        foreach ((string providerName, LlmProviderOptions provider) in options.Providers)
        {
            if (!LlmProviderKinds.All.Any(kind => string.Equals(kind, provider.Kind, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(
                    $"Llm:Providers:{providerName}:Kind має бути одним із [{string.Join(", ", LlmProviderKinds.All)}], а задано '{provider.Kind}'");
            }

            if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out Uri? baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"Llm:Providers:{providerName}:BaseUrl має бути абсолютним http(s)-URL, а задано '{provider.BaseUrl}'");
            }
        }

        foreach ((string roleName, LlmRoleOptions roleOptions) in options.Roles)
        {
            if (!Enum.TryParse(roleName, ignoreCase: true, out LlmRole _))
            {
                errors.Add(
                    $"Llm:Roles:{roleName} — невідома роль; допустимі: {string.Join(", ", Enum.GetNames<LlmRole>())}");
                continue;
            }

            foreach (LlmChainEntryOptions entry in roleOptions.Chain)
            {
                if (!LlmOptionsLookup.TryGetProvider(options, entry.Provider, out LlmProviderOptions _))
                {
                    errors.Add($"Llm:Roles:{roleName}: ланцюжок посилається на невідомого провайдера '{entry.Provider}'");
                }

                if (string.IsNullOrWhiteSpace(entry.Model))
                {
                    errors.Add($"Llm:Roles:{roleName}: для провайдера '{entry.Provider}' не задано Model");
                }
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
