// AI: юніт-тести помічників нормалізації (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using System.Globalization;
using Waterfall.Services.Ingestion.Normalization;

namespace Waterfall.Ingestion.Tests;

public class SalaryParserTests
{
    [Theory]
    [InlineData("$3500-4500", 3500, 4500, "USD")]
    [InlineData("$3000 - 3800", 3000, 3800, "USD")]
    [InlineData("40 000 – 60 000 грн", 40000, 60000, "UAH")]
    [InlineData("€2.5k–3.5k", 2500, 3500, "EUR")]
    [InlineData("1,500-2,000 usd", 1500, 2000, "USD")]
    [InlineData("$6000-5000", 5000, 6000, "USD")]
    public void Range_IsParsedIntoMinAndMax(string text, double min, double max, string currency)
    {
        SalaryInfo salary = SalaryParser.Parse(text, "UAH");

        Assert.Equal((decimal)min, salary.Min);
        Assert.Equal((decimal)max, salary.Max);
        Assert.Equal(currency, salary.Currency);
    }

    [Fact]
    public void From_SetsOnlyMin()
    {
        SalaryInfo salary = SalaryParser.Parse("від $2000", "UAH");

        Assert.Equal(2000m, salary.Min);
        Assert.Null(salary.Max);
        Assert.Equal("USD", salary.Currency);
    }

    [Theory]
    [InlineData("до $2500", 2500)]
    [InlineData("up to $6000", 6000)]
    public void UpTo_SetsOnlyMax(string text, double max)
    {
        SalaryInfo salary = SalaryParser.Parse(text, "UAH");

        Assert.Null(salary.Min);
        Assert.Equal((decimal)max, salary.Max);
    }

    [Fact]
    public void SingleAmount_IsBothMinAndMax()
    {
        SalaryInfo salary = SalaryParser.Parse("1500 USD", "UAH");

        Assert.Equal(1500m, salary.Min);
        Assert.Equal(1500m, salary.Max);
    }

    [Fact]
    public void LetterAfterNumber_IsNotThousandsMultiplier()
    {
        // «к» тут — початок слова «кожного», а не «тисяч»
        SalaryInfo salary = SalaryParser.Parse("4500 кожного місяця", "UAH");

        Assert.Equal(4500m, salary.Min);
        Assert.Equal("UAH", salary.Currency);
    }

    [Theory]
    [InlineData("за домовленістю")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NoAmount_LeavesSalaryEmptyWithDefaultCurrency(string? text)
    {
        SalaryInfo salary = SalaryParser.Parse(text, "USD");

        Assert.Null(salary.Min);
        Assert.Null(salary.Max);
        Assert.Equal("USD", salary.Currency);
    }

    [Fact]
    public void HourlyRate_IsNotConvertedToMonthlySalary()
    {
        SalaryInfo salary = SalaryParser.Parse("$12/год", "UAH");

        Assert.Null(salary.Min);
        Assert.Null(salary.Max);
        Assert.Equal("USD", salary.Currency);
    }

    [Fact]
    public void AmountAboveSchemaLimit_IsDropped()
    {
        SalaryInfo salary = SalaryParser.Parse("2 000 000 грн", "USD");

        Assert.Null(salary.Min);
        Assert.Null(salary.Max);
    }
}

public class PublishedDateParserTests
{
    // 10:00 UTC = 13:00 за Києвом (UTC+3 до переходу на зимовий час 25 жовтня 2026)
    private static readonly DateTimeOffset Now = At("2026-10-12T10:00:00Z");

    [Theory]
    [InlineData("3 години тому", "2026-10-12T07:00:00Z")]
    [InlineData("10 хв. тому", "2026-10-12T09:50:00Z")]
    [InlineData("2 дні тому", "2026-10-10T10:00:00Z")]
    [InlineData("1 тиждень тому", "2026-10-05T10:00:00Z")]
    [InlineData("5 days ago", "2026-10-07T10:00:00Z")]
    public void RelativeDate_IsCountedFromNow(string text, string expected)
    {
        Assert.Equal(At(expected), PublishedDateParser.Parse(text, Now));
    }

    [Theory]
    [InlineData("сьогодні", "2026-10-11T21:00:00Z")]
    [InlineData("вчора", "2026-10-10T21:00:00Z")]
    [InlineData("08.10.2026", "2026-10-07T21:00:00Z")]
    [InlineData("8 жовтня 2026", "2026-10-07T21:00:00Z")]
    [InlineData("8 жовтня", "2026-10-07T21:00:00Z")]
    [InlineData("2026-10-08", "2026-10-07T21:00:00Z")]
    public void DateWithoutTime_IsKyivMidnight(string text, string expected)
    {
        Assert.Equal(At(expected), PublishedDateParser.Parse(text, Now));
    }

    [Fact]
    public void DayMonthWithoutYear_InTheFuture_MeansPreviousYear()
    {
        // 20 грудня ще не настало, тож це грудень 2025 року; взимку Київ живе за UTC+2
        Assert.Equal(At("2025-12-19T22:00:00Z"), PublishedDateParser.Parse("20 грудня", Now));
    }

    [Fact]
    public void IsoDateTime_KeepsExactMoment()
    {
        Assert.Equal(At("2026-10-08T09:30:00Z"), PublishedDateParser.Parse("2026-10-08T09:30:00Z", Now));
        Assert.Equal(At("2026-10-08T06:30:00Z"), PublishedDateParser.Parse("2026-10-08T09:30:00+03:00", Now));
    }

    [Theory]
    [InlineData("незрозуміла дата")]
    [InlineData("31 лютого 2026")]
    [InlineData("")]
    [InlineData(null)]
    public void UnrecognizedText_ReturnsNull(string? text)
    {
        Assert.Null(PublishedDateParser.Parse(text, Now));
    }

    private static DateTimeOffset At(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}

public class TextNormalizerTests
{
    [Fact]
    public void CleanLine_CollapsesWhitespaceAndDropsInvisibleCharacters()
    {
        Assert.Equal("Senior .NET Developer", TextNormalizer.CleanLine("  Senior   .NET\tDeveloper​ "));
    }

    [Fact]
    public void CleanLine_DecodesEntitiesAndStripsTags()
    {
        Assert.Equal("PostgreSQL & Redis", TextNormalizer.CleanLine("PostgreSQL &amp; Redis"));
        Assert.Equal("Bold text", TextNormalizer.CleanLine("<b>Bold</b> text"));
    }

    [Fact]
    public void CleanLine_ReturnsNullForBlankText()
    {
        Assert.Null(TextNormalizer.CleanLine("  \t "));
    }

    [Fact]
    public void HtmlToPlainText_KeepsStructureAndDropsScripts()
    {
        string html = "<p>Перший</p><ul><li>C#</li><li>SQL</li></ul><script>alert('x')</script><p>Другий&nbsp;абзац</p>";

        Assert.Equal("Перший\n• C#\n• SQL\nДругий абзац", TextNormalizer.HtmlToPlainText(html));
    }

    [Fact]
    public void HtmlToPlainText_ReturnsNullWhenOnlyScriptRemains()
    {
        Assert.Null(TextNormalizer.HtmlToPlainText("<script>alert('x')</script>"));
    }

    [Fact]
    public void Truncate_MarksCutWithEllipsis()
    {
        Assert.Equal("abc…", TextNormalizer.Truncate("abcdef", 4));
        Assert.Equal("abc", TextNormalizer.Truncate("abc", 4));
    }
}

public class LocationNormalizerTests
{
    [Theory]
    [InlineData("Kyiv, Remote", "Київ (віддалено)")]
    [InlineData("Remote", "Віддалено")]
    [InlineData("Kyiv / Lviv, Hybrid", "Київ, Львів (гібрид)")]
    [InlineData("Odesa, офіс", "Одеса")]
    [InlineData("Remote (Ukraine)", "Україна (віддалено)")]
    [InlineData("Kyiv, Ukraine", "Київ")]
    [InlineData("Вінниця", "Вінниця")]
    [InlineData("", "Не вказано")]
    [InlineData(null, "Не вказано")]
    public void Location_IsUnified(string? raw, string expected)
    {
        Assert.Equal(expected, LocationNormalizer.Normalize(raw));
    }
}

public class SkillsNormalizerTests
{
    [Fact]
    public void Skills_AreSplitByAnySeparator()
    {
        Assert.Equal(["C#", ".NET", "PostgreSQL", "Docker"], SkillsNormalizer.Normalize("C#, .NET; PostgreSQL | Docker"));
    }

    [Fact]
    public void SynonymsAndDuplicates_CollapseIntoOneSkill()
    {
        Assert.Equal(["React", "TypeScript"], SkillsNormalizer.Normalize("React, TypeScript, ts, react"));
        Assert.Equal(["Kubernetes"], SkillsNormalizer.Normalize("k8s; Kubernetes"));
    }

    [Fact]
    public void EmptyAndOverlongSkills_AreDropped()
    {
        string overlong = new('x', SkillsNormalizer.MaxSkillLength + 1);

        Assert.Equal(["Go"], SkillsNormalizer.Normalize($" , ;  | Go, {overlong}"));
    }

    [Fact]
    public void SkillCount_IsLimited()
    {
        string many = string.Join(", ", Enumerable.Range(1, 40).Select(i => $"skill{i}"));

        Assert.Equal(SkillsNormalizer.MaxSkills, SkillsNormalizer.Normalize(many).Count);
    }
}
