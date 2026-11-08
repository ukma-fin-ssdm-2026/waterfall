// AI: тести розбору robota.ua (JobPosting JSON-LD → RawVacancy, посилання зі списку) (issue #62); перевірено: запуск dotnet test, розмітка складена за schema.org, бо справжня сторінка списку без JS — порожня оболонка (листопад 2026), самоперевірка автора (§8)

using Waterfall.Api.Workers;
using Waterfall.Services;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

public sealed class RobotaUaPageTests
{
    private static readonly RobotaUaParser RobotaUa = new();

    [Fact]
    public void ParseListPage_ReadsJobPostingAndSkipsOtherStructuredData()
    {
        List<RawVacancy> jobs = RobotaUa.ParseListPage("""
            <script type="application/ld+json">
            {
              "@graph": [
                {"@type": "Organization", "name": "Robota.ua"},
                {"@type": "JobPosting",
                 "identifier": {"@type": "PropertyValue", "value": "845516"},
                 "url": "https://robota.ua/company123/vacancy845516?utm_source=list",
                 "title": "Middle .NET Developer",
                 "description": "<p>Шукаємо .NET розробника.</p>",
                 "datePosted": "2026-10-24T20:43:05+03:00",
                 "hiringOrganization": {"@type": "Organization", "name": "Kvitka Software"},
                 "baseSalary": {"@type": "MonetaryAmount", "currency": "USD",
                   "value": {"@type": "QuantitativeValue", "minValue": 2500, "maxValue": 5000}},
                 "jobLocation": {"@type": "Place", "address": {"addressLocality": "Київ", "addressCountry": "Україна"}},
                 "employmentType": "FULL_TIME",
                 "experienceRequirements": {"@type": "OccupationalExperienceRequirements", "monthsOfExperience": 12},
                 "occupationalCategory": ".NET"}
              ]
            }
            </script>
            """);

        RawVacancy job = Assert.Single(jobs);
        Assert.Equal(JobSource.RobotaUa, job.Source);
        Assert.Equal("845516", job.Id);
        Assert.Equal("https://robota.ua/company123/vacancy845516", job.Url);
        Assert.Equal("Middle .NET Developer", job.Title);
        Assert.Equal("Kvitka Software", job.Company);
        Assert.Equal("2500-5000 USD", job.Salary);
        Assert.Equal("Київ, Україна", job.Location);
        Assert.Equal("2026-10-24T20:43:05+03:00", job.Posted);
        Assert.Equal("Повна зайнятість", job.Employment);
        Assert.Equal("1 рік досвіду", job.Experience);
        Assert.Equal(".NET", job.Category);
        Assert.Contains(".NET розробника", job.DescriptionHtml);
    }

    [Fact]
    public void ParseListPage_FallsBackToVacancyLinksAndDeduplicates()
    {
        List<RawVacancy> jobs = RobotaUa.ParseListPage("""
            <script type="application/ld+json">
            {"@type":"JobPosting","identifier":"1","url":"https://robota.ua/company1/vacancy1","title":"QA"}
            </script>
            <a href="/company1/vacancy1">Повтор вакансії</a>
            <a href="/company2/vacancy2">  Junior QA  </a>
            <a href="/company3/vacancy3"></a>
            <a href="https://example.com/vacancy4">Зовнішнє посилання</a>
            """);

        Assert.Equal(2, jobs.Count);
        Assert.Equal("QA", jobs[0].Title);
        Assert.Equal("2", jobs[1].Id);
        Assert.Equal("https://robota.ua/company2/vacancy2", jobs[1].Url);
        Assert.Equal(JobSource.RobotaUa, jobs[1].Source);
    }

    [Fact]
    public void ParseListPage_UsesHeadingAsTitleForRenderedVacancyCard()
    {
        List<RawVacancy> jobs = RobotaUa.ParseListPage("""
            <a class="new-design-card" href="/company477532/vacancy11371686">
                <h2>Вантажник (Правий берег, Київ)</h2>
                <span>28 000 — 38 000 ₴</span>
                <span>Нова пошта</span>
                <span>Київ</span>
            </a>
            """);

        RawVacancy job = Assert.Single(jobs);
        Assert.Equal("11371686", job.Id);
        Assert.Equal("Вантажник (Правий берег, Київ)", job.Title);
    }

    [Fact]
    public void ParseJobPage_ReadsJsonLdAndCanBeNormalized()
    {
        RawVacancy raw = RobotaUa.ParseJobPage("""
            <script type="application/ld+json">
            {"@type":"JobPosting","identifier":42,"url":"/company1/vacancy42",
             "title":"QA Engineer","description":"<p>Тестування продукту</p>",
             "hiringOrganization":{"name":"Lumos"},
             "baseSalary":{"currency":"UAH","value":{"value":45000}},
             "employmentType":["FULL_TIME","CONTRACTOR"]}
            </script>
            """) with { Source = JobSource.RobotaUa };

        (var vacancy, string? error) = VacancyIngestionService.Normalize(raw, DateTimeOffset.UtcNow);

        Assert.Null(error);
        Assert.NotNull(vacancy);
        Assert.Equal("RobotaUa", vacancy.Source);
        Assert.Equal(45000m, vacancy.SalaryMin);
        Assert.Equal(45000m, vacancy.SalaryMax);
        Assert.Equal("UAH", vacancy.Currency);
        Assert.Equal("https://robota.ua/company1/vacancy42", vacancy.SourceUrl);
        Assert.Equal("Повна зайнятість, Контракт", vacancy.Schedule);
    }

    [Fact]
    public void ParseListPage_AppShellWithoutJavaScript_FindsNothing()
    {
        // Так виглядає справжня сторінка списку без JS (листопад 2026): вакансії підвантажує вже браузер
        List<RawVacancy> jobs = RobotaUa.ParseListPage("""
            <html><head><title>Robota.ua</title></head>
            <body><app-root></app-root>
            <script src="runtime.7ffaafca3a6c4326.js" type="module"></script>
            <script src="main.6b1e1c25dd20e50e.js" type="module"></script></body></html>
            """);

        Assert.Empty(jobs);
    }

    [Fact]
    public void ListPageUrl_UsesOneBasedPaging()
    {
        Assert.Null(RobotaUa.ListPageUrl(0));
        Assert.Equal("https://robota.ua/zapros/ukraine", RobotaUa.ListPageUrl(1));
        Assert.Equal("https://robota.ua/zapros/ukraine;page=2", RobotaUa.ListPageUrl(2));
    }

    [Fact]
    public void ParseListPage_UsesSafeSameHostLinksAndTitleFallbacks()
    {
        List<RawVacancy> jobs = RobotaUa.ParseListPage("""
            <a href="https://www.robota.ua/company1/vacancy10?searchId=x#details" title="Title attribute"></a>
            <a href="https://example.com/vacancy12">External link</a>
            <a href="javascript:void(0)/vacancy13">Non-HTTP link</a>
            <a href="/company1/vacancy14"><span class="vacancy-title">  Nested title  </span><span>extra text</span></a>
            """);

        Assert.Collection(
            jobs,
            titleFallback =>
            {
                Assert.Equal("10", titleFallback.Id);
                Assert.Equal("Title attribute", titleFallback.Title);
                Assert.Equal("https://www.robota.ua/company1/vacancy10", titleFallback.Url);
            },
            nestedTitle =>
            {
                Assert.Equal("14", nestedTitle.Id);
                Assert.Equal("Nested title", nestedTitle.Title);
                Assert.Equal("https://robota.ua/company1/vacancy14", nestedTitle.Url);
            });
    }

    [Fact]
    public void ParseListPage_ReadsNestedJsonLdVariantsAndSkipsInvalidJobRecords()
    {
        List<RawVacancy> jobs = RobotaUa.ParseListPage("""
            <script type="application/ld+json">
            [
              {"@type":"Organization","name":"Other"},
              {"@graph":[
                {"@type":["JobPosting"],"identifier":{"name":"20"},
                 "url":"/company20/vacancy20","title":"Remote engineer",
                 "hiringOrganization":"Acme","jobLocationType":"TELECOMMUTE",
                 "baseSalary":{"currency":"eur","value":{"minValue":1250.5}},
                 "employmentType":["PART_TIME","TEMPORARY","INTERNSHIP"],
                 "experienceRequirements":"3+ years"},
                {"@type":"JobPosting","title":"No vacancy id"},
                {"@type":"JobPosting","identifier":{"value":"21"},
                 "url":"https://robota.ua/company21/vacancy21?ref=list","title":"Hybrid engineer",
                 "jobLocation":[
                   {"address":{"addressLocality":"Kyiv","addressCountry":"UA"}},
                   {"address":{"addressLocality":"kyiv","addressCountry":"UA"}}],
                 "jobLocationType":"TELECOMMUTE",
                 "baseSalary":{"currency":"GBP","value":{"maxValue":2500}},
                 "experienceRequirements":{"monthsOfExperience":36},
                 "employmentType":"FULL_TIME"},
                {"@type":"JobPosting","identifier":"22","url":"/vacancy22",
                 "experienceRequirements":{"monthsOfExperience":0},
                 "baseSalary":{"value":{"value":900}},
                 "employmentType":null}
              ]}
            ]
            </script>
            """);

        Assert.Equal(3, jobs.Count);
        Assert.Equal("20", jobs[0].Id);
        Assert.Equal("Acme", jobs[0].Company);
        Assert.Equal("від 1250.5 EUR", jobs[0].Salary);
        Assert.Equal("Віддалено", jobs[0].Location);
        Assert.Equal("Часткова зайнятість, Тимчасова робота, INTERNSHIP", jobs[0].Employment);
        Assert.Equal("3+ years", jobs[0].Experience);

        Assert.Equal("21", jobs[1].Id);
        Assert.Equal("Kyiv, UA / Віддалено", jobs[1].Location);
        Assert.Equal("до 2500", jobs[1].Salary);
        Assert.Equal("3 роки досвіду", jobs[1].Experience);
        Assert.Equal("Повна зайнятість", jobs[1].Employment);
        Assert.Equal("https://robota.ua/company21/vacancy21", jobs[1].Url);

        Assert.Equal("22", jobs[2].Id);
        Assert.Equal("900", jobs[2].Salary);
        Assert.Equal("Без досвіду", jobs[2].Experience);
        Assert.Null(jobs[2].Employment);
    }

    [Theory]
    [InlineData(12, "1 рік досвіду")]
    [InlineData(24, "2 роки досвіду")]
    [InlineData(60, "5 років досвіду")]
    [InlineData(132, "11 років досвіду")]
    [InlineData(144, "12 років досвіду")]
    [InlineData(18, "18 міс. досвіду")]
    public void ParseJobPage_FormatsExperienceMonths(int months, string expected)
    {
        RawVacancy job = RobotaUa.ParseJobPage($$$"""
            <script type="application/ld+json">
            {"@type":"JobPosting","identifier":"1","url":"/vacancy1",
             "description":"Details",
             "experienceRequirements":{"monthsOfExperience":{{{months}}}}}
            </script>
            """);

        Assert.Equal(expected, job.Experience);
    }

    [Fact]
    public void ParseJobPage_FallsBackToRenderedDescriptionAndTitle()
    {
        RawVacancy job = RobotaUa.ParseJobPage("""
            <main>
              <h1>Rendered title</h1>
              <div class="vacancy-description"><p>Rendered description</p></div>
            </main>
            """);

        Assert.Equal("Rendered title", job.Title);
        Assert.Contains("Rendered description", job.DescriptionHtml);
    }

    [Fact]
    public void ParseJobPage_UsesMainAsLastDescriptionFallback()
    {
        RawVacancy job = RobotaUa.ParseJobPage("<main><p>Only main content</p></main>");

        Assert.Contains("Only main content", job.DescriptionHtml);
        Assert.Equal("Опис вакансії на сайті robota.ua", RobotaUa.ParseJobPage(string.Empty).DescriptionHtml);
    }
}
