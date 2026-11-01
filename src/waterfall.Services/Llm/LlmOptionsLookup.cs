// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Llm;

/// <summary>
/// Пошук у словниках конфігурації без урахування регістру: ключі з appsettings
/// і назви в ланцюжках можуть відрізнятися регістром, і це не має ламати запуск.
/// </summary>
internal static class LlmOptionsLookup
{
    /// <summary>Знаходить провайдера за назвою.</summary>
    public static bool TryGetProvider(LlmOptions options, string? name, out LlmProviderOptions provider)
    {
        provider = null!;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (options.Providers.TryGetValue(name, out LlmProviderOptions? exact))
        {
            provider = exact;
            return true;
        }

        foreach ((string key, LlmProviderOptions value) in options.Providers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                provider = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Знаходить ланцюжок ролі за назвою.</summary>
    public static bool TryGetRole(LlmOptions options, LlmRole role, out LlmRoleOptions roleOptions)
    {
        roleOptions = null!;
        string name = role.ToString();
        if (options.Roles.TryGetValue(name, out LlmRoleOptions? exact))
        {
            roleOptions = exact;
            return true;
        }

        foreach ((string key, LlmRoleOptions value) in options.Roles)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                roleOptions = value;
                return true;
            }
        }

        return false;
    }
}
