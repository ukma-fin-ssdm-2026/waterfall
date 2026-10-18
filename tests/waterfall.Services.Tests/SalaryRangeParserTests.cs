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
}
