using Waterfall.Api.Workers;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;
using Xunit;

namespace Waterfall.Api.Tests.Workers;

public sealed class RobotaUaParserTests
{
    private readonly RobotaUaParser _parser = new();

    [Fact]
    public void Properties_ShouldReturnValidDefaults()
    {
        Assert.Equal("robota.ua", _parser.Name);
        Assert.Equal(new Uri("https://robota.ua/"), _parser.Home);
    }

    [Theory]
    [InlineData(1, "https://robota.ua/zapros/ukraine")]
    [InlineData(2, "https://robota.ua/zapros/ukraine;page=2")]
    [InlineData(5, "https://robota.ua/zapros/ukraine;page=5")]
    public void ListPageUrl_ValidPage_ReturnsExpectedUrl(int page, string expected)
    {
        string? url = _parser.ListPageUrl(page);
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ListPageUrl_InvalidPage_ReturnsNull(int page)
    {
        Assert.Null(_parser.ListPageUrl(page));
    }

    [Fact]
    public void ParseListPage_WithHtmlCards_ParsesAndDeduplicates()
    {
        string html = @"
            <html>
                <body>
                    <main>
                        <!-- Перша валідна вакансія з назвою в h2 -->
                        <a href=""/company123/vacancy9731915?searchId=abc"">
                            <h2>C# Developer</h2>
                        </a>

                        <!-- Дублікат тієї самої вакансії (наприклад, лінк-картинка) -->
                        <a href=""/company123/vacancy9731915"">
                            <span>Логотип</span>
                        </a>

                        <!-- Друга валідна вакансія з альтернативним слешем і класом -->
                        <a href=""https://robota.ua/company456/vacancy/9556822"" title=""QA Engineer"">
                            <span class=""title"">QA Engineer</span>
                        </a>

                        <!-- Стороннє посилання, яке має бути проігнороване -->
                        <a href=""https://google.com/vacancy123"">
                            <h2>Google Job</h2>
                        </a>

                        <!-- Лінк без тексту та без title -->
                        <a href=""/company789/vacancy8187155""></a>
                    </main>
                </body>
            </html>";

        List<RawVacancy> vacancies = _parser.ParseListPage(html);

        Assert.Equal(2, vacancies.Count);

        RawVacancy first = vacancies[0];
        Assert.Equal("9731915", first.Id);
        Assert.Equal("C# Developer", first.Title);
        Assert.Equal("https://robota.ua/company123/vacancy9731915", first.Url);
        Assert.Equal(JobSource.RobotaUa, first.Source);

        RawVacancy second = vacancies[1];
        Assert.Equal("9556822", second.Id);
        Assert.Equal("QA Engineer", second.Title);
        Assert.Equal("https://robota.ua/company456/vacancy/9556822", second.Url);
    }

    [Fact]
    public void ParseListPage_WithStructuredJsonLd_ParsesFullFields()
    {
        string html = @"
            <html>
                <head>
                    <script type=""application/ld+json"">
                    {
                        ""@context"": ""https://schema.org"",
                        ""@type"": ""JobPosting"",
                        ""identifier"": { ""value"": ""1234567"" },
                        ""title"": ""Lead Architect"",
                        ""url"": ""https://robota.ua/company1/vacancy1234567"",
                        ""hiringOrganization"": { ""name"": ""Tech Corp"" },
                        ""baseSalary"": {
                            ""currency"": ""UAH"",
                            ""value"": { ""minValue"": 50000, ""maxValue"": 80000 }
                        },
                        ""jobLocation"": {
                            ""address"": {
                                ""addressLocality"": ""Київ"",
                                ""addressCountry"": ""Україна""
                            }
                        },
                        ""jobLocationType"": ""TELECOMMUTE"",
                        ""datePosted"": ""2026-03-01"",
                        ""employmentType"": ""FULL_TIME"",
                        ""experienceRequirements"": { ""monthsOfExperience"": 36 },
                        ""occupationalCategory"": ""IT"",
                        ""description"": ""<p>Awesome job</p>""
                    }
                    </script>
                </head>
                <body></body>
            </html>";

        List<RawVacancy> vacancies = _parser.ParseListPage(html);

        Assert.Single(vacancies);
        RawVacancy job = vacancies[0];
        Assert.Equal("1234567", job.Id);
        Assert.Equal("Lead Architect", job.Title);
        Assert.Equal("Tech Corp", job.Company);
        Assert.Equal("50000-80000 UAH", job.Salary);
        Assert.Contains("Київ", job.Location);
        Assert.Contains("Віддалено", job.Location);
        Assert.Equal("Повна зайнятість", job.Employment);
        Assert.Equal("3 роки досвіду", job.Experience);
        Assert.Equal("<p>Awesome job</p>", job.DescriptionHtml);
    }

    [Fact]
    public void ParseJobPage_WithHtmlFallback_ExtractsTitleAndDescription()
    {
        string html = @"
            <html>
                <body>
                    <main>
                        <h1>Fullstack .NET & Angular Developer</h1>
                        <div class=""vacancy-description"">
                            <p>Ми шукаємо сильного розробника в команду Waterfall.</p>
                        </div>
                    </main>
                </body>
            </html>";

        RawVacancy details = _parser.ParseJobPage(html);

        Assert.Equal("Fullstack .NET & Angular Developer", details.Title);
        Assert.NotNull(details.DescriptionHtml);
        Assert.Contains("Waterfall", details.DescriptionHtml);
    }

    [Fact]
    public void ParseJobPage_EmptyHtml_ReturnsFallbackDescription()
    {
        string html = "<html><body></body></html>";

        RawVacancy details = _parser.ParseJobPage(html);

        Assert.Null(details.Title);
        Assert.Equal("Опис вакансії на сайті robota.ua", details.DescriptionHtml);
    }
}