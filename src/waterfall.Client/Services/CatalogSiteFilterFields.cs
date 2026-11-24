// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.ViewModels;

namespace Waterfall.Client.Services;

/// <summary>
/// Поля форм пошуку на сайтах (DOU, Work.ua, Djinni, Robota.ua), що зберігаються разом із фільтрами каталогу (REL-002).
/// Явна таблиця замість рефлексії: так поля не губляться в trimmed-збірках (Android Release, iOS),
/// а кожне значення перевіряється при відновленні: пункт списку лише з наявних варіантів,
/// кількість сторінок лише як невелике додатне число, текст обрізається.
/// Не зберігаються «з якої сторінки почати» (це продовження конкретного пошуку, а не налаштування)
/// і сортування Work.ua (воно синхронізоване із загальним сортуванням, яке вже зберігається).
/// </summary>
public static class CatalogSiteFilterFields
{
    public enum FieldKind
    {
        Text,
        Flag,
        Pages,
        Option
    }

    /// <param name="Options">Допустимі варіанти для <see cref="FieldKind.Option"/>; для інших видів — <c>null</c>.</param>
    public sealed record Field(
        string Name,
        FieldKind Kind,
        Func<CatalogViewModel, string> Get,
        Action<CatalogViewModel, string> Restore,
        Func<CatalogViewModel, IReadOnlyList<string>>? Options = null);

    public static IReadOnlyList<Field> All { get; } =
    [
        // DOU
        Pages(nameof(CatalogViewModel.DouPagesToParse), vm => vm.DouPagesToParse, (vm, v) => vm.DouPagesToParse = v),
        Text(nameof(CatalogViewModel.DouSearchQuery), vm => vm.DouSearchQuery, (vm, v) => vm.DouSearchQuery = v),
        Flag(nameof(CatalogViewModel.DouSearchInDescription), vm => vm.DouSearchInDescription, (vm, v) => vm.DouSearchInDescription = v),
        Option(nameof(CatalogViewModel.DouSelectedCategory), vm => vm.DouSelectedCategory, (vm, v) => vm.DouSelectedCategory = v, vm => vm.DouCategoryOptions),
        Option(nameof(CatalogViewModel.DouSelectedExperience), vm => vm.DouSelectedExperience, (vm, v) => vm.DouSelectedExperience = v, vm => vm.DouExperienceOptions),
        Option(nameof(CatalogViewModel.DouSelectedCity), vm => vm.DouSelectedCity, (vm, v) => vm.DouSelectedCity = v, vm => vm.DouCityOptions),
        Flag(nameof(CatalogViewModel.DouIsRemote), vm => vm.DouIsRemote, (vm, v) => vm.DouIsRemote = v),
        Flag(nameof(CatalogViewModel.DouIsRelocation), vm => vm.DouIsRelocation, (vm, v) => vm.DouIsRelocation = v),
        Flag(nameof(CatalogViewModel.DouIsFirstJob), vm => vm.DouIsFirstJob, (vm, v) => vm.DouIsFirstJob = v),
        Flag(nameof(CatalogViewModel.DouIsGovTech), vm => vm.DouIsGovTech, (vm, v) => vm.DouIsGovTech = v),
        Flag(nameof(CatalogViewModel.DouIsMilTech), vm => vm.DouIsMilTech, (vm, v) => vm.DouIsMilTech = v),
        Flag(nameof(CatalogViewModel.DouIsDefenseForces), vm => vm.DouIsDefenseForces, (vm, v) => vm.DouIsDefenseForces = v),
        Flag(nameof(CatalogViewModel.DouHasReservation), vm => vm.DouHasReservation, (vm, v) => vm.DouHasReservation = v),

        // Work.ua (сфери, зайнятість і мови — це галочки, вони зберігаються окремо: ChoiceGroups)
        Pages(nameof(CatalogViewModel.WorkUaPagesToParse), vm => vm.WorkUaPagesToParse, (vm, v) => vm.WorkUaPagesToParse = v),
        Flag(nameof(CatalogViewModel.WorkUaOpenJobPages), vm => vm.WorkUaOpenJobPages, (vm, v) => vm.WorkUaOpenJobPages = v),
        Text(nameof(CatalogViewModel.WorkUaSearchQuery), vm => vm.WorkUaSearchQuery, (vm, v) => vm.WorkUaSearchQuery = v),
        Option(nameof(CatalogViewModel.WorkUaSelectedWordMatch), vm => vm.WorkUaSelectedWordMatch, (vm, v) => vm.WorkUaSelectedWordMatch = v, vm => vm.WorkUaWordMatchOptions),
        Flag(nameof(CatalogViewModel.WorkUaTitleOnly), vm => vm.WorkUaTitleOnly, (vm, v) => vm.WorkUaTitleOnly = v),
        Option(nameof(CatalogViewModel.WorkUaSelectedCity), vm => vm.WorkUaSelectedCity, (vm, v) => vm.WorkUaSelectedCity = v, vm => vm.WorkUaCityOptions),
        Text(nameof(CatalogViewModel.WorkUaSalaryFrom), vm => vm.WorkUaSalaryFrom, (vm, v) => vm.WorkUaSalaryFrom = v),
        Text(nameof(CatalogViewModel.WorkUaSalaryTo), vm => vm.WorkUaSalaryTo, (vm, v) => vm.WorkUaSalaryTo = v),
        Flag(nameof(CatalogViewModel.WorkUaIncludeNoSalary), vm => vm.WorkUaIncludeNoSalary, (vm, v) => vm.WorkUaIncludeNoSalary = v),
        Option(nameof(CatalogViewModel.WorkUaSelectedExperience), vm => vm.WorkUaSelectedExperience, (vm, v) => vm.WorkUaSelectedExperience = v, vm => vm.WorkUaExperienceOptions),
        Option(nameof(CatalogViewModel.WorkUaSelectedDays), vm => vm.WorkUaSelectedDays, (vm, v) => vm.WorkUaSelectedDays = v, vm => vm.WorkUaDaysOptions),
        Text(nameof(CatalogViewModel.WorkUaAgeFrom), vm => vm.WorkUaAgeFrom, (vm, v) => vm.WorkUaAgeFrom = v),
        Text(nameof(CatalogViewModel.WorkUaAgeTo), vm => vm.WorkUaAgeTo, (vm, v) => vm.WorkUaAgeTo = v),
        Flag(nameof(CatalogViewModel.WorkUaForStudents), vm => vm.WorkUaForStudents, (vm, v) => vm.WorkUaForStudents = v),
        Flag(nameof(CatalogViewModel.WorkUaForPersonsWithDisabilities), vm => vm.WorkUaForPersonsWithDisabilities, (vm, v) => vm.WorkUaForPersonsWithDisabilities = v),
        Flag(nameof(CatalogViewModel.WorkUaForRetirees), vm => vm.WorkUaForRetirees, (vm, v) => vm.WorkUaForRetirees = v),
        Flag(nameof(CatalogViewModel.WorkUaForVeterans), vm => vm.WorkUaForVeterans, (vm, v) => vm.WorkUaForVeterans = v),
        Flag(nameof(CatalogViewModel.WorkUaHasReservation), vm => vm.WorkUaHasReservation, (vm, v) => vm.WorkUaHasReservation = v),
        Flag(nameof(CatalogViewModel.WorkUaWithoutResume), vm => vm.WorkUaWithoutResume, (vm, v) => vm.WorkUaWithoutResume = v),

        // Djinni
        Pages(nameof(CatalogViewModel.DjinniPagesToParse), vm => vm.DjinniPagesToParse, (vm, v) => vm.DjinniPagesToParse = v),
        Text(nameof(CatalogViewModel.DjinniAllKeywords), vm => vm.DjinniAllKeywords, (vm, v) => vm.DjinniAllKeywords = v),
        Text(nameof(CatalogViewModel.DjinniAnyOfKeywords), vm => vm.DjinniAnyOfKeywords, (vm, v) => vm.DjinniAnyOfKeywords = v),
        Text(nameof(CatalogViewModel.DjinniExcludeKeywords), vm => vm.DjinniExcludeKeywords, (vm, v) => vm.DjinniExcludeKeywords = v),
        Option(nameof(CatalogViewModel.DjinniSelectedSearchType), vm => vm.DjinniSelectedSearchType, (vm, v) => vm.DjinniSelectedSearchType = v, vm => vm.DjinniSearchTypeOptions),
        Text(nameof(CatalogViewModel.DjinniCompanyFilter), vm => vm.DjinniCompanyFilter, (vm, v) => vm.DjinniCompanyFilter = v),
        Option(nameof(CatalogViewModel.DjinniSelectedCategory), vm => vm.DjinniSelectedCategory, (vm, v) => vm.DjinniSelectedCategory = v, vm => vm.DjinniCategoryOptions),
        Option(nameof(CatalogViewModel.DjinniSelectedSalary), vm => vm.DjinniSelectedSalary, (vm, v) => vm.DjinniSelectedSalary = v, vm => vm.DjinniSalaryOptions),
        Text(nameof(CatalogViewModel.DjinniCustomMinSalary), vm => vm.DjinniCustomMinSalary, (vm, v) => vm.DjinniCustomMinSalary = v),
        Option(nameof(CatalogViewModel.DjinniSelectedExperience), vm => vm.DjinniSelectedExperience, (vm, v) => vm.DjinniSelectedExperience = v, vm => vm.DjinniExperienceOptions),
        Option(nameof(CatalogViewModel.DjinniSelectedEmployment), vm => vm.DjinniSelectedEmployment, (vm, v) => vm.DjinniSelectedEmployment = v, vm => vm.DjinniEmploymentOptions),
        Option(nameof(CatalogViewModel.DjinniSelectedEnglish), vm => vm.DjinniSelectedEnglish, (vm, v) => vm.DjinniSelectedEnglish = v, vm => vm.DjinniEnglishOptions),
        Option(nameof(CatalogViewModel.DjinniSelectedCompanyType), vm => vm.DjinniSelectedCompanyType, (vm, v) => vm.DjinniSelectedCompanyType = v, vm => vm.DjinniCompanyTypeOptions),
        Option(nameof(CatalogViewModel.DjinniSelectedDomain), vm => vm.DjinniSelectedDomain, (vm, v) => vm.DjinniSelectedDomain = v, vm => vm.DjinniDomainOptions),
        Option(nameof(CatalogViewModel.DjinniSelectedRegion), vm => vm.DjinniSelectedRegion, (vm, v) => vm.DjinniSelectedRegion = v, vm => vm.DjinniRegionOptions),
        Option(nameof(CatalogViewModel.DjinniSelectedCity), vm => vm.DjinniSelectedCity, (vm, v) => vm.DjinniSelectedCity = v, vm => vm.DjinniCityOptions),
        Option(nameof(CatalogViewModel.DjinniSelectedCountry), vm => vm.DjinniSelectedCountry, (vm, v) => vm.DjinniSelectedCountry = v, vm => vm.DjinniCountryOptions),
        Flag(nameof(CatalogViewModel.DjinniHasPublicSalary), vm => vm.DjinniHasPublicSalary, (vm, v) => vm.DjinniHasPublicSalary = v),
        Flag(nameof(CatalogViewModel.DjinniUkrainianProduct), vm => vm.DjinniUkrainianProduct, (vm, v) => vm.DjinniUkrainianProduct = v),
        Flag(nameof(CatalogViewModel.DjinniMilTech), vm => vm.DjinniMilTech, (vm, v) => vm.DjinniMilTech = v),
        Flag(nameof(CatalogViewModel.DjinniHasReservation), vm => vm.DjinniHasReservation, (vm, v) => vm.DjinniHasReservation = v),
        Flag(nameof(CatalogViewModel.DjinniHideStale), vm => vm.DjinniHideStale, (vm, v) => vm.DjinniHideStale = v),

        // Robota.ua і спільні ключові слова сайту
        Pages(nameof(CatalogViewModel.RobotaUaPagesToParse), vm => vm.RobotaUaPagesToParse, (vm, v) => vm.RobotaUaPagesToParse = v),
        Text(nameof(CatalogViewModel.RobotaUaSearchQuery), vm => vm.RobotaUaSearchQuery, (vm, v) => vm.RobotaUaSearchQuery = v),
        Option(nameof(CatalogViewModel.RobotaUaSelectedCity), vm => vm.RobotaUaSelectedCity, (vm, v) => vm.RobotaUaSelectedCity = v, vm => vm.RobotaUaCityOptions),
        Option(nameof(CatalogViewModel.RobotaUaSelectedEmployment), vm => vm.RobotaUaSelectedEmployment, (vm, v) => vm.RobotaUaSelectedEmployment = v, vm => vm.RobotaUaEmploymentOptions),
        Option(nameof(CatalogViewModel.RobotaUaSelectedSalary), vm => vm.RobotaUaSelectedSalary, (vm, v) => vm.RobotaUaSelectedSalary = v, vm => vm.RobotaUaSalaryOptions),
        Option(nameof(CatalogViewModel.RobotaUaSelectedExperience), vm => vm.RobotaUaSelectedExperience, (vm, v) => vm.RobotaUaSelectedExperience = v, vm => vm.RobotaUaExperienceOptions),
        Flag(nameof(CatalogViewModel.RobotaUaIncludeNoSalary), vm => vm.RobotaUaIncludeNoSalary, (vm, v) => vm.RobotaUaIncludeNoSalary = v),
        Text(nameof(CatalogViewModel.SiteKeywords), vm => vm.SiteKeywords, (vm, v) => vm.SiteKeywords = v)
    ];

    /// <summary>Групи галочок Work.ua (сфери, зайнятість, мови).</summary>
    public static IEnumerable<IReadOnlyList<SelectableOption>> ChoiceGroups(CatalogViewModel vm) =>
        [vm.WorkUaCategoryChoices, vm.WorkUaEmploymentChoices, vm.WorkUaLanguageChoices];

    /// <summary>Стабільний ідентифікатор галочки — параметр і код сайту («category=24»), а не підпис, який можуть перейменувати.</summary>
    public static string ChoiceId(SelectableOption option) => $"{option.Key}={option.Value}";

    /// <summary>Кількість сторінок: лише 1–999, інакше лишається значення за замовчуванням.</summary>
    private static Field Pages(string name, Func<CatalogViewModel, string> get, Action<CatalogViewModel, string> set) =>
        new(name, FieldKind.Pages, get, (vm, value) =>
        {
            if (value.Length is > 0 and <= 3 && value.All(char.IsAsciiDigit) && value.Any(c => c != '0'))
            {
                set(vm, value);
            }
        });

    private static Field Text(string name, Func<CatalogViewModel, string> get, Action<CatalogViewModel, string> set) =>
        new(name, FieldKind.Text, get, (vm, value) => set(vm, CatalogFilterPersistence.Clip(value)));

    private static Field Flag(string name, Func<CatalogViewModel, bool> get, Action<CatalogViewModel, bool> set) =>
        new(name, FieldKind.Flag, vm => get(vm) ? "true" : "false", (vm, value) =>
        {
            if (bool.TryParse(value, out bool flag))
            {
                set(vm, flag);
            }
        });

    private static Field Option(
        string name,
        Func<CatalogViewModel, string> get,
        Action<CatalogViewModel, string> set,
        Func<CatalogViewModel, IReadOnlyList<string>> options) =>
        new(name, FieldKind.Option, get, (vm, value) =>
        {
            if (options(vm).Contains(value))
            {
                set(vm, value);
            }
        }, options);
}
