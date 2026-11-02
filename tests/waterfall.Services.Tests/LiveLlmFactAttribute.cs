// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

namespace Waterfall.Services.Tests;

/// <summary>
/// <see cref="FactAttribute"/> для живих тестів LLM. За замовчуванням тест пропускається,
/// щоб CI ніколи не звертався до зовнішніх API; запуск — лише з <c>WATERFALL_LLM_LIVE=1</c>.
/// </summary>
public sealed class LiveLlmFactAttribute : FactAttribute
{
    /// <summary>Змінна середовища, яка вмикає живі тести (потрібні реальні ключі провайдерів).</summary>
    public const string EnvironmentVariable = "WATERFALL_LLM_LIVE";

    /// <summary>Створює атрибут і виставляє Skip, якщо живі тести не ввімкнено.</summary>
    public LiveLlmFactAttribute()
    {
        string? enabled = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.Equals(enabled, "1", StringComparison.Ordinal))
        {
            Skip = $"Живий тест LLM пропущено: задайте {EnvironmentVariable}=1 (потрібні ключі провайдерів).";
        }
    }
}
