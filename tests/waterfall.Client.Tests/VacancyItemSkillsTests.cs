// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using CommunityToolkit.Mvvm.Messaging;
using Waterfall.Client.Models.Vacancies;
using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Tests;

public class VacancyItemSkillsTests
{
    private readonly FakeGateway _gateway = new();
    private readonly List<string> _errors = [];
    private readonly StrongReferenceMessenger _messenger = new();

    private VacancyItemViewModel Item(params string[] skills) =>
        new(new VacancyDto { Id = Guid.NewGuid(), Skills = skills },
            _gateway, _messenger, _errors.Add);

    [Fact]
    public void VisibleSkills_EmptySkills_EmptyAndNoOverflow()
    {
        VacancyItemViewModel item = Item();

        Assert.Empty(item.VisibleSkills);
        Assert.Null(item.MoreSkillsText);
        Assert.False(item.HasMoreSkills);
    }

    [Theory]
    [InlineData("C#")]
    [InlineData("C#", ".NET", "SQL")]
    public void VisibleSkills_UpToThree_AllShownWithoutOverflow(params string[] skills)
    {
        VacancyItemViewModel item = Item(skills);

        Assert.Equal(skills, item.VisibleSkills);
        Assert.Null(item.MoreSkillsText);
    }

    [Fact]
    public void VisibleSkills_MoreThanThree_FirstThreeAndOverflowCount()
    {
        VacancyItemViewModel item = Item("C#", ".NET", "SQL", "Docker", "Azure");

        Assert.Equal(["C#", ".NET", "SQL"], item.VisibleSkills);
        Assert.Equal("+2", item.MoreSkillsText);
        Assert.True(item.HasMoreSkills);
    }

    [Fact]
    public void VisibleSkills_BlanksAndDuplicates_DroppedBeforeCounting()
    {
        VacancyItemViewModel item = Item(" C# ", "", "c#", "SQL", "   ", "sql", "Docker");

        Assert.Equal(["C#", "SQL", "Docker"], item.VisibleSkills);
        Assert.Null(item.MoreSkillsText);
    }
}
