// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Common;

namespace Waterfall.Services.Tests;

/// <summary>
/// Тести маскування персональних даних перед відправкою тексту зовнішньому LLM (THR-06).
/// Перевіряються і знайдені email/телефони, і те, що звичайні числа (зарплати, дати) не псуються.
/// </summary>
public sealed class SensitiveTextMaskerTests
{
    [Theory]
    [InlineData("пишіть на kosta.dev+job@example.com, будь ласка", "пишіть на [email приховано], будь ласка")]
    [InlineData("мій email: KOSTA@WATERFALL.COM.UA", "мій email: [email приховано]")]
    [InlineData("email", "email")]
    public void Mask_ReplacesEmailsAndKeepsSurroundingText(string text, string expected)
    {
        // Act
        string masked = SensitiveTextMasker.Mask(text);

        // Assert
        Assert.Equal(expected, masked);
    }

    [Theory]
    [InlineData("телефон +380 (67) 123-45-67", "телефон [телефон приховано]")]
    [InlineData("дзвоніть 0671234567 після 18:00", "дзвоніть [телефон приховано] після 18:00")]
    [InlineData("номер 380 50 123 45 67", "номер [телефон приховано]")]
    [InlineData("US: +1 415 555 0123", "US: [телефон приховано]")]
    public void Mask_ReplacesPhoneNumbersInCommonFormats(string text, string expected)
    {
        // Act
        string masked = SensitiveTextMasker.Mask(text);

        // Assert
        Assert.Equal(expected, masked);
    }

    [Theory]
    [InlineData("хочу зарплату від 1000 до 2000 USD")]
    [InlineData("бюджет 3000-5000 доларів")]
    [InlineData("народився 12.03.2024")]
    [InlineData("компанія з 1 000 000 клієнтів")]
    [InlineData(" мінімум 4000 ")]
    public void Mask_DoesNotTouchOrdinaryNumbers(string text)
    {
        // Act
        string masked = SensitiveTextMasker.Mask(text);

        // Assert
        Assert.Equal(text, masked);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Mask_NullOrEmpty_ReturnsEmptyString(string? text)
    {
        // Act
        string masked = SensitiveTextMasker.Mask(text);

        // Assert
        Assert.Equal(string.Empty, masked);
    }

    [Fact]
    public void Mask_IsIdempotent()
    {
        // Arrange
        const string text = "я Іван, email ivan@example.com, тел. +380 67 111 22 33";

        // Act
        string once = SensitiveTextMasker.Mask(text);
        string twice = SensitiveTextMasker.Mask(once);

        // Assert
        Assert.Equal(once, twice);
        Assert.DoesNotContain("ivan@example.com", once, StringComparison.Ordinal);
        Assert.DoesNotContain("67 111 22 33", once, StringComparison.Ordinal);
        Assert.Contains("Іван", once, StringComparison.Ordinal);
    }
}
