// AI: юніт-тести нормалізатора вакансій (issue #62); перевірено: запуск dotnet test, самоперевірка автора (§8)

using Waterfall.Services.Ingestion;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

public class VacancyNormalizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
    private readonly VacancyNormalizer _normalizer = new();

    [Fact]
    public void ValidRecord_IsNormalizedIntoSingleFormat()
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(title: "  Senior   .NET Developer "), Now);

        Assert.True(result.IsValid, result.Error);
        NormalizedVacancy vacancy = result.Vacancy;
        Assert.Equal(JobSource.Djinni, vacancy.Source);
        Assert.Equal("720001", vacancy.ExternalId);
        Assert.Equal("Senior .NET Developer", vacancy.Title);
        Assert.Equal(3500m, vacancy.SalaryMin);
        Assert.Equal(4500m, vacancy.SalaryMax);
        Assert.Equal("USD", vacancy.Currency);
        Assert.Equal("Київ (віддалено)", vacancy.Location);
        Assert.Equal("5+ років", vacancy.Experience);
        Assert.Equal(["C#", ".NET"], vacancy.Skills);
        Assert.Equal(Now.AddDays(-2), vacancy.PublishedAt);
        Assert.Equal("Опис вакансії", vacancy.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("QA")]
    public void MissingOrTooShortTitle_IsRejected(string? title)
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(title: title), Now);

        Assert.False(result.IsValid);
        Assert.Contains("назви посади", result.Error);
    }

    [Theory]
    [InlineData("https://djinni-careers.example.net/jobs/1/")]
    [InlineData("https://djinni.co.evil.example/jobs/1/")]
    [InlineData("https://djinni.co@evil.example/jobs/1/")]
    public void LinkToForeignDomain_IsRejected(string url)
    {
        // THR-03: шахрайська вакансія з посиланням на сторонній сайт не потрапляє в каталог
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(url: url), Now);

        Assert.False(result.IsValid);
        Assert.Contains("сторонній домен", result.Error);
    }

    [Theory]
    [InlineData("https://user@djinni.co/jobs/1-x/")]
    [InlineData("https://djinni.co:8443/jobs/1-x/")]
    public void LinkWithCredentialsOrCustomPort_IsRejected(string url)
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(url: url), Now);

        Assert.False(result.IsValid);
        Assert.Contains("облікові дані або нестандартний порт", result.Error);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://djinni.co/jobs/1/")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void MissingOrNonHttpLink_IsRejected(string? url)
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(url: url), Now);

        Assert.False(result.IsValid);
        Assert.Contains("посилання на вакансію відсутнє або некоректне", result.Error);
    }

    [Fact]
    public void SubdomainOfSource_IsAccepted()
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(url: "https://www.djinni.co/jobs/720001-x/"), Now);

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void TrackingParameters_AreRemovedFromLink()
    {
        NormalizationResult result = _normalizer.Normalize(
            RawData.Raw(url: "https://djinni.co/jobs/720096-lead/?utm_source=telegram#apply"), Now);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("https://djinni.co/jobs/720096-lead/", result.Vacancy.SourceUrl);
    }

    [Fact]
    public void MissingId_IsTakenFromLink()
    {
        NormalizationResult result = _normalizer.Normalize(
            RawData.Raw(id: null, url: "https://djinni.co/jobs/720033-middle-react-developer/"), Now);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("720033", result.Vacancy.ExternalId);
    }

    [Fact]
    public void MissingIdWithoutNumberInLink_IsRejected()
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(id: null, url: "https://djinni.co/jobs/developer/"), Now);

        Assert.False(result.IsValid);
        Assert.Contains("ідентифікатора", result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<p>   </p><script>alert(1)</script>")]
    public void EmptyDescription_IsRejected(string? description)
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(description: description), Now);

        Assert.False(result.IsValid);
        Assert.Contains("опис", result.Error);
    }

    [Fact]
    public void UnknownSource_IsRejected()
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(source: "linkedin"), Now);

        Assert.False(result.IsValid);
        Assert.Contains("невідоме джерело", result.Error);
    }

    [Theory]
    [InlineData("QA Automation Engineer", "Playwright, C#", "QA")]
    [InlineData("Senior Developer", "C#, ASP.NET Core", ".NET")]
    [InlineData("Data Engineer", "Python, Airflow", "Python")]
    [InlineData("Backend Developer", "Java, Spring", "Java")]
    [InlineData("Frontend Developer", "JavaScript, React", "Frontend")]
    [InlineData("Office Manager", "Excel", "Інше")]
    public void MissingCategory_IsInferredFromTitleAndSkills(string title, string tags, string expected)
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(title: title, tags: tags, category: null), Now);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Vacancy.Category);
    }

    [Theory]
    [InlineData("5 років досвіду", "5+ років")]
    [InlineData("2+ years", "2+ роки")]
    [InlineData("1 рік", "1+ рік")]
    [InlineData("11 років", "11+ років")]
    [InlineData("без досвіду", "Без досвіду")]
    [InlineData("Senior level", "Senior level")]
    public void Experience_IsUnified(string raw, string expected)
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(experience: raw), Now);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Vacancy.Experience);
    }

    [Fact]
    public void PublicationDateInTheFuture_IsClampedToNow()
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(posted: "20.10.2026"), Now);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(Now, result.Vacancy.PublishedAt);
    }

    [Fact]
    public void MissingSalary_UsesSourceDefaultCurrency()
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(salary: null), Now);

        Assert.True(result.IsValid, result.Error);
        Assert.Null(result.Vacancy.SalaryMin);
        Assert.Equal("USD", result.Vacancy.Currency);
    }

    [Fact]
    public void LongTitle_IsTruncatedToDatabaseLimit()
    {
        NormalizationResult result = _normalizer.Normalize(RawData.Raw(title: new string('T', 300)), Now);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(200, result.Vacancy.Title.Length);
        Assert.EndsWith("…", result.Vacancy.Title);
    }
}
