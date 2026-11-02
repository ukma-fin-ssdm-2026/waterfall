// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Services.Analysis;

namespace Waterfall.Services.Tests;

/// <summary>
/// Токени релевантності: назви технологій лишаються цілими, а розділові знаки речення — ні.
/// </summary>
public class VacancyTextTokenizerTests
{
    [Theory]
    [InlineData("Стек: ASP.NET Core, C#, C++ та .NET 10.", new[] { "стек", "asp.net", "core", "c#", "c++", ".net" })]
    [InlineData("Node.js backend. Python is a plus.", new[] { "node.js", "backend", "python" })]
    [InlineData("Ведення обліку в 1С, звітність", new[] { "ведення", "обліку", "1с", "звітність" })]
    public void Tokenize_KeepsTechnologyNamesWhole(string text, string[] expected)
    {
        // Act
        IReadOnlyList<string> tokens = VacancyTextTokenizer.Tokenize(text);

        // Assert
        Assert.Equal(expected, tokens);
    }

    [Fact]
    public void Tokenize_DropsStopWordsNumbersAndDuplicates()
    {
        // Act
        IReadOnlyList<string> tokens = VacancyTextTokenizer.Tokenize("Досвід від 3 років, 2026. Python, python, PYTHON.");

        // Assert
        Assert.Equal(["python"], tokens);
    }
}
