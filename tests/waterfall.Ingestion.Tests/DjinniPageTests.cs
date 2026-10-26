// AI: тести розбору сторінок djinni.co (JSON-LD → RawVacancy, поле «Необхідні навички») (issue #62); перевірено: запуск dotnet test, звірка зі справжньою сторінкою djinni.co/jobs/ (жовтень 2026), самоперевірка автора (§8)

using System.Globalization;
using System.Text.Json;
using Waterfall.Api.Workers;
using Waterfall.Repository.Entities;
using Waterfall.Services;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

public class DjinniPageTests
{
    // Скорочена розмітка справжньої сторінки djinni.co/jobs/ (жовтень 2026): структура JSON-LD та ж, компанії й тексти вигадані.
    // Djinni екранує «<» і «&» у JSON як < і &.
    internal const string SamplePage = """
        <!doctype html>
        <html lang="uk">
        <head>
          <script type="application/ld+json">{"@context": "https://schema.org", "@type": "Organization", "name": "Djinni"}</script>
        </head>
        <body>
          <div id="job-item-845516" class="job-item card-link">…</div>
          <div id="job-item-853451" class="job-item card-link">…</div>
          <script type="application/ld+json">
          [{"@context": "https://schema.org/", "@type": "JobPosting",
            "baseSalary": {"@type": "MonetaryAmount", "currency": "USD", "value": {"@type": "QuantitativeValue", "maxValue": 5000, "minValue": 2500, "unitText": "MONTH"}},
            "category": ".NET", "datePosted": "2026-10-24T20:43:05.943290",
            "description": "Шукаємо .NET розробника.\nДосвід з List<T> та LINQ.\n\nКоманда R&D",
            "directApply": true, "employmentType": "FULL_TIME",
            "experienceRequirements": {"@type": "OccupationalExperienceRequirements", "monthsOfExperience": 12.0},
            "hiringOrganization": {"@type": "Organization", "name": "Kvitka Software", "sameAs": "https://kvitka.example"},
            "identifier": 845516, "industry": "saas", "jobLocationType": "TELECOMMUTE",
            "title": "Middle .NET Developer", "url": "https://djinni.co/jobs/845516-middle-net-developer/",
            "validThrough": "2026-11-23T20:43:05.943290"},
           {"@context": "https://schema.org/", "@type": "JobPosting",
            "category": "QA", "datePosted": "2026-10-23T09:15:00.000000",
            "description": "Ручне тестування вебзастосунку.", "employmentType": "PART_TIME",
            "experienceRequirements": {"@type": "OccupationalExperienceRequirements", "monthsOfExperience": 6.0},
            "hiringOrganization": {"@type": "Organization", "name": "Lumos Fintech"},
            "identifier": 853451,
            "jobLocation": {"@type": "Place", "address": {"@type": "PostalAddress", "addressCountry": "Україна", "addressLocality": ["Київ", "Львів"], "addressRegion": null, "postalCode": null}},
            "applicantLocationRequirements": {"@type": "AdministrativeArea", "address": {"@type": "PostalAddress", "addressCountry": "UA"}},
            "title": "QA", "url": "https://djinni.co/jobs/853451-qa/"}]
          </script>
        </body>
        </html>
        """;

    private static readonly DjinniParser Djinni = new();

    [Fact]
    public void ParseListPage_ReadsJobPostings()
    {
        List<RawVacancy> jobs = DjinniPage();

        Assert.Equal(2, jobs.Count); // JSON-LD організації пропущено
        RawVacancy job = jobs[0];
        Assert.Equal("845516", job.Id);
        Assert.Equal("https://djinni.co/jobs/845516-middle-net-developer/", job.Url);
        Assert.Equal("Middle .NET Developer", job.Title);
        Assert.Equal("Kvitka Software", job.Company);
        Assert.Equal("2500-5000 USD", job.Salary);
        Assert.Equal("Віддалено", job.Location);
        Assert.Equal("2026-10-24T20:43:05.9432900+03:00", job.Posted); // київський час із явним зсувом
        Assert.Equal("1 рік досвіду", job.Experience);
        Assert.Equal("Повна зайнятість", job.Employment);
        Assert.Equal(".NET", job.Category);
        Assert.Equal(JobSource.Djinni, job.Source);
        Assert.Null(job.Tags); // навички — лише на сторінці вакансії
    }

    [Fact]
    public void ParseListPage_ReadsOfficeJobWithoutSalary()
    {
        RawVacancy job = DjinniPage()[1];

        Assert.Null(job.Salary);
        Assert.Equal("Україна (Київ, Львів)", job.Location);
        Assert.Equal("6 міс. досвіду", job.Experience);
        Assert.Equal("Часткова зайнятість", job.Employment);
    }

    [Fact]
    public void ParseListPage_ResultPassesNormalization()
    {
        DateTimeOffset now = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

        (VacancyDB? vacancy, string? error) = VacancyIngestionService.Normalize(DjinniPage()[0], now);

        Assert.Null(error);
        Assert.NotNull(vacancy);
        Assert.Equal(2500m, vacancy.SalaryMin);
        Assert.Equal(5000m, vacancy.SalaryMax);
        Assert.Equal("USD", vacancy.Currency);
        // 20:43 за Києвом (UTC+3 до переходу на зимовий час) — це 17:43 UTC
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T17:43:05.94329Z", CultureInfo.InvariantCulture), vacancy.PublishedAt);
        Assert.Equal(TimeSpan.Zero, vacancy.PublishedAt.Offset); // в UTC, щоб сортування в БД не плутало зсуви
        // «<T>» з опису не прийнято за HTML-тег, порожній рядок прибрано
        Assert.Equal("Шукаємо .NET розробника.\nДосвід з List<T> та LINQ.\nКоманда R&D", vacancy.Description);
        // У списку поля навичок немає, а з опису їх не вгадуємо
        Assert.Equal("[]", vacancy.SkillsJson);
    }

    [Theory]
    [InlineData("""{"currency": "USD", "value": {"minValue": 2500, "maxValue": 5000, "unitText": "MONTH"}}""", "2500-5000 USD")]
    [InlineData("""{"currency": "USD", "value": {"maxValue": 5000}}""", "до 5000 USD")]
    [InlineData("""{"currency": "eur", "value": {"minValue": 2000.0, "unitText": "MONTH"}}""", "від 2000 EUR")]
    [InlineData("""{"currency": "UAH", "value": {"value": "45000"}}""", "45000 UAH")]
    [InlineData("""{"currency": "USD", "value": {"minValue": 20, "maxValue": 30, "unitText": "HOUR"}}""", null)] // погодинна ставка
    [InlineData("""{"currency": "PLN", "value": {"minValue": 8000}}""", null)] // валюту, якої не знає нормалізація, не вгадуємо
    [InlineData("""{"currency": "USD"}""", null)]
    public void ParseListPage_FormatsSalary(string baseSalary, string? expected)
    {
        Assert.Equal(expected, Single($$"""{"@type": "JobPosting", "baseSalary": {{baseSalary}} }""").Salary);
    }

    [Theory]
    [InlineData(0, "Без досвіду")]
    [InlineData(6, "6 міс. досвіду")]
    [InlineData(18, "18 міс. досвіду")]
    [InlineData(12, "1 рік досвіду")]
    [InlineData(36, "3 роки досвіду")]
    [InlineData(60, "5 років досвіду")]
    [InlineData(132, "11 років досвіду")]
    [InlineData(252, "21 рік досвіду")]
    public void ParseListPage_FormatsExperience(int months, string expected)
    {
        string job = $$"""{"@type": "JobPosting", "experienceRequirements": {"monthsOfExperience": {{months}} } }""";

        Assert.Equal(expected, Single(job).Experience);
    }

    [Theory]
    [InlineData("2026-12-01T10:00:00", "2026-12-01T10:00:00.0000000+02:00")] // зимовий час — UTC+2
    [InlineData("2026-10-24T17:43:05Z", "2026-10-24T17:43:05Z")]             // зсув уже є — не чіпаємо
    [InlineData("вчора", "вчора")]
    public void ParseListPage_AddsKyivOffsetToDate(string datePosted, string expected)
    {
        Assert.Equal(expected, Single($$"""{"@type": "JobPosting", "datePosted": "{{datePosted}}"}""").Posted);
    }

    [Fact]
    public void ParseListPage_ReadsExperienceGivenAsText()
    {
        // schema.org дозволяє замість об'єкта з місяцями звичайний текст
        Assert.Equal("Без досвіду", Single("""{"@type": "JobPosting", "experienceRequirements": "Без досвіду"}""").Experience);
    }

    [Theory]
    [InlineData("""{"jobLocationType": "TELECOMMUTE"}""", "Віддалено")]
    [InlineData("""{"jobLocation": {"address": {"addressLocality": "Одеса"}}}""", "Одеса")] // без країни, місто — рядком
    [InlineData("""{"jobLocationType": "TELECOMMUTE", "jobLocation": [{"address": {"addressCountry": "Польща", "addressLocality": []}}]}""", "Польща / Віддалено")]
    [InlineData("""{"jobLocation": [{"address": {"addressCountry": "Кіпр"}}, {"address": {"addressCountry": "Мальта"}}]}""", "Кіпр; Мальта")]
    [InlineData("""{}""", null)]
    public void ParseListPage_FormatsLocation(string fields, string? expected)
    {
        Assert.Equal(expected, Single(WithType(fields)).Location);
    }

    [Theory]
    [InlineData("""{"employmentType": "FULL_TIME"}""", "Повна зайнятість")]
    [InlineData("""{"employmentType": ["PART_TIME", "INTERN"]}""", "Часткова зайнятість · Стажування")]
    [InlineData("""{"employmentType": ["CONTRACTOR", "TEMPORARY"]}""", "Контракт · Тимчасова робота")]
    [InlineData("""{"employmentType": "VOLUNTEER"}""", "VOLUNTEER")] // невідомий тип не ховаємо
    [InlineData("""{"employmentType": []}""", null)]
    public void ParseListPage_FormatsEmployment(string fields, string? expected)
    {
        Assert.Equal(expected, Single(WithType(fields)).Employment);
    }

    [Theory]
    [InlineData("<html><body>Перевірка браузера…</body></html>")] // капча чи нова верстка
    [InlineData("<script type=\"application/ld+json\">{\"@type\": \"BreadcrumbList\"}</script>")]
    public void ParseListPage_PageWithoutJobs_ReturnsEmptyList(string html)
    {
        Assert.Empty(Djinni.ParseListPage(html));
    }

    [Fact]
    public void ParseListPage_BrokenJsonLd_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => Djinni.ParseListPage("<script type=\"application/ld+json\">[{</script>"));
    }

    [Theory]
    [InlineData("TypeScript, React.js, Node.js, REST API, Python", "TypeScript, React.js, Node.js, REST API, Python")]
    [InlineData("Google Analytics, Google Sheets / Excel, R&amp;D", "Google Analytics, Google Sheets / Excel, R&D")]
    public void ParseJobPage_ReadsRequiredSkills(string skillsHtml, string expected)
    {
        Assert.Equal(expected, Djinni.ParseJobPage(JobPage(skillsHtml)).Tags);
    }

    [Fact]
    public void ParseJobPage_ReadsSkillsHiddenBehindMoreButton()
    {
        // Справжня розмітка (жовтень 2026): частина навичок схована, поруч кнопка «+ ще 3» і скрипт
        string html = JobPage("Linux, Ansible, Docker, MySQL, Kafka", moreSkillsHtml: "Python, CI/CD, On-Premise Infrastructure");

        Assert.Equal("Linux, Ansible, Docker, MySQL, Kafka, Python, CI/CD, On-Premise Infrastructure", Djinni.ParseJobPage(html).Tags);
    }

    [Fact]
    public void ParseJobPage_FieldIsOptional()
    {
        // Поле не заповнене: не можна взяти «Англійська» з наступного блоку
        Assert.Null(Djinni.ParseJobPage(JobPage(skillsHtml: null)).Tags);
        Assert.Null(Djinni.ParseJobPage(JobPage(skillsHtml: " ")).Tags);
        Assert.Null(Djinni.ParseJobPage(SamplePage).Tags);
    }

    private static List<RawVacancy> DjinniPage() => Djinni.ParseListPage(SamplePage);

    private static RawVacancy Single(string jobJson) => Assert.Single(Djinni.ParseListPage(Page(jobJson)));

    // {"поле": …} → {"@type": "JobPosting", "поле": …}
    private static string WithType(string fields) => fields == "{}"
        ? """{"@type": "JobPosting"}"""
        : """{"@type": "JobPosting", """ + fields[1..];

    /// <summary>Сторінка в розмітці Djinni з переданими вакансіями (JSON-об'єктами JobPosting).</summary>
    internal static string Page(params string[] jobsJson) =>
        $"<html><body><script type=\"application/ld+json\">[{string.Join(", ", jobsJson)}]</script></body></html>";

    /// <summary>
    /// Фрагмент справжньої сторінки вакансії djinni.co (жовтень 2026): необов'язкове поле «Необхідні навички»
    /// стоїть між описом і вимогами до мов.
    /// </summary>
    internal static string JobPage(string? skillsHtml, string? moreSkillsHtml = null)
    {
        // Довгий список Djinni ховає за кнопкою «+ ще N»
        string more = moreSkillsHtml is null ? string.Empty : $"""
            <div id="more_skills_exp" data-fold="more_skills_exp" hidden="until-found"> <span> {moreSkillsHtml} </span> </div>
            """;
        string toggle = moreSkillsHtml is null ? string.Empty : """
            <a id="more_skills_exp-toggle" href="#more_skills_exp" role="button" class="d-inline-block mt-2 text-primary fs-5 fw-semibold js-fold-toggle" aria-expanded="false"> + ще 3 </a>
            <script type="module" src="/static/generated/js/fold.js"></script>
            """;
        string skills = skillsHtml is null ? string.Empty : $"""
            <h2 class="fs-4">Необхідні навички</h2>
            <div class="mb-3">
              <div>
                <span>
                  {skillsHtml}
                </span>
                {more}
              </div>
              {toggle}
            </div>
            """;
        return $"""
            <div class="mb-4 job-post__description"><p>Опис вакансії</p></div>
            {skills}
            <div class="mb-4">
              <div>
                <h2 class="fs-4">Вимоги до володіння мовами</h2>
                <div class="detail-rows">
                  <div class="detail-rows__line"><span class="detail-rows__name">Англійська</span></div>
                </div>
              </div>
            </div>
            """;
    }

    /// <summary>Мінімальна валідна вакансія Djinni з заданим id.</summary>
    internal static string Job(int id) =>
        $$"""{"@type": "JobPosting", "identifier": {{id}}, "title": "Вакансія {{id}}", "url": "https://djinni.co/jobs/{{id}}-job/", "description": "Опис"}""";
}
