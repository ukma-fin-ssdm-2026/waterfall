using waterfall.Services.Common;
using Xunit;

namespace waterfall.Services.Tests;

public class SalaryRangeParserTests
{
    [Fact]
    public void Parse_StandardRange_ReturnsMinAndMax()
    {
        // Arrange
        var raw = "$2000 - $4000";

        // Act
        var result = SalaryRangeParser.Parse(raw);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2000m, result.Min);
        Assert.Equal(4000m, result.Max);
    }

    [Fact]
    public void Parse_PlusSuffix_ReturnsOnlyMin()
    {
        // Arrange
        var raw = "$2500+";

        // Act
        var result = SalaryRangeParser.Parse(raw);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2500m, result.Min);
        Assert.Null(result.Max);
    }

    [Fact]
    public void Parse_DoPrefix_ReturnsOnlyMax()
    {
        // Arrange
        var raw = "до $3000";

        // Act
        var result = SalaryRangeParser.Parse(raw);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.Min);
        Assert.Equal(3000m, result.Max);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Договірна")]
    [InlineData("abc - def")]
    public void Parse_InvalidOrGarbageInput_ReturnsNull(string? raw)
    {
        // Act
        var result = SalaryRangeParser.Parse(raw);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Parse_MinGreaterThanMax_ReturnsNull()
    {
        // Arrange: інвертований діапазон (помилка у вхідних даних)
        var raw = "$5000 - $2000";

        // Act
        var result = SalaryRangeParser.Parse(raw);

        // Assert: парсер має відхилити нелогічний діапазон
        Assert.Null(result);
    }
}
