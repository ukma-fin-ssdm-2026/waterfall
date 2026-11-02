// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Repository.Normalization;

namespace Waterfall.Repository.Tests;

/// <summary>
/// Тести запасного розбору досвіду з тексту: українські відмінки й множина, діапазони, місяці, англійські варіанти.
/// </summary>
public sealed class VacancyExperienceParserTests
{
    [Theory]
    [InlineData("1 рік досвіду", 12)]
    [InlineData("2 роки досвіду", 24)]
    [InlineData("5 років досвіду", 60)]
    [InlineData("від 2 років", 24)]
    [InlineData("від 1 року", 12)]
    [InlineData("2+ роки", 24)]
    [InlineData("5+ років", 60)]
    [InlineData("3-5 років", 36)]
    [InlineData("3–5 років", 36)]           // типографське тире
    [InlineData("Досвід роботи від 1 року.", 12)]
    [InlineData("6 міс. досвіду", 6)]
    [InlineData("10 місяців досвіду", 10)]
    [InlineData("2+ years", 24)]
    [InlineData("3 years of experience", 36)]
    [InlineData("1 year", 12)]
    [InlineData("без досвіду", 0)]
    [InlineData("Без досвіду", 0)]
    [InlineData("no experience", 0)]
    [InlineData("0 років досвіду", 0)]
    public void FromText_ReadsMinimumExperience(string text, int expectedMonths)
    {
        Assert.Equal(expectedMonths, VacancyExperienceParser.FromText(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("не вказано")]
    [InlineData("до 2 років")]              // верхня межа — не мінімум
    [InlineData("Junior or Middle")]
    public void FromText_WithoutMinimum_ReturnsNull(string? text)
    {
        Assert.Null(VacancyExperienceParser.FromText(text));
    }

    [Fact]
    public void ParseMonths_PrefersExperienceFieldOverDescription()
    {
        Assert.Equal(12, VacancyExperienceParser.ParseMonths("1 рік досвіду", "Потрібно 5 років досвіду роботи з C#."));
    }

    [Fact]
    public void ParseMonths_FallsBackToDescription()
    {
        Assert.Equal(60, VacancyExperienceParser.ParseMonths("Не вказано", "Потрібно 5 років досвіду роботи з C#."));
    }

    [Fact]
    public void ParseMonths_WithoutAnySignal_ReturnsNull()
    {
        Assert.Null(VacancyExperienceParser.ParseMonths(null, "Цікавий проєкт у продуктовій команді."));
    }
}
