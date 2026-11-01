// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using System.ComponentModel.DataAnnotations;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Services.Tests;

/// <summary>
/// Тести розбору й перевірки параметрів фільтра: список навичок приходить і повторюваними параметрами,
/// і рядком через кому, а порожні та зайві терміни не мають впливати на пошук.
/// </summary>
public sealed class VacancyFilterParametersTests
{
    [Fact]
    public void Skills_CommaSeparatedList_IsSplitAndTrimmed()
    {
        VacancyFilterParameters filter = new() { Skills = ["C#, .NET"] };

        Assert.Equal(["C#", ".NET"], filter.Skills);
    }

    [Fact]
    public void Skills_RepeatedParameters_AreKeptSeparately()
    {
        VacancyFilterParameters filter = new() { Skills = ["C#", ".NET"] };

        Assert.Equal(["C#", ".NET"], filter.Skills);
    }

    [Fact]
    public void Skills_DuplicatesAndBlanks_AreDropped()
    {
        VacancyFilterParameters filter = new() { Skills = ["C#", "c#", " .NET ", "", ","] };

        Assert.Equal(["C#", ".NET"], filter.Skills);
    }

    [Fact]
    public void Skills_WithoutTerms_IsNull()
    {
        VacancyFilterParameters filter = new() { Skills = ["  ", ","] };

        Assert.Null(filter.Skills);
    }

    [Fact]
    public void Validate_TooManySkills_IsRejected()
    {
        VacancyFilterParameters filter = new()
        {
            Skills = Enumerable.Range(1, VacancyFilterParameters.MaxSkills + 1).Select(i => $"skill{i}").ToList()
        };

        List<ValidationResult> errors = filter.Validate(new ValidationContext(filter)).ToList();

        Assert.Contains(errors, error => error.MemberNames.Contains("skills"));
    }

    [Fact]
    public void Validate_TooLongSkill_IsRejected()
    {
        VacancyFilterParameters filter = new() { Skills = [new string('a', VacancyFilterParameters.MaxSkillLength + 1)] };

        List<ValidationResult> errors = filter.Validate(new ValidationContext(filter)).ToList();

        Assert.Contains(errors, error => error.MemberNames.Contains("skills"));
    }

    [Fact]
    public void Validate_MaximumAllowedSkills_IsAccepted()
    {
        VacancyFilterParameters filter = new()
        {
            Skills = Enumerable.Range(1, VacancyFilterParameters.MaxSkills).Select(i => $"skill{i}").ToList()
        };

        Assert.Empty(filter.Validate(new ValidationContext(filter)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(600)]
    public void MaxExperienceMonths_WithinRange_IsValid(int months)
    {
        VacancyFilterParameters filter = new() { MaxExperienceMonths = months };

        List<ValidationResult> errors = new();
        Validator.TryValidateObject(filter, new ValidationContext(filter), errors, validateAllProperties: true);

        Assert.Empty(errors);
    }
}
