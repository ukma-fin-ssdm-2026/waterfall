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
}
