// AI: тести розбору RSS-стрічки й сторінки вакансії jobs.dou.ua (issue #62); перевірено: запуск dotnet test, звірка зі справжньою стрічкою jobs.dou.ua/vacancies/feeds/ і сторінкою вакансії (жовтень 2026), самоперевірка автора (§8)

using System.Xml;
using Waterfall.Api.Workers;
using Waterfall.Repository.Entities;
using Waterfall.Services;
using Waterfall.Services.Interfaces;
using Waterfall.Services.Models.Vacancies;

namespace Waterfall.Ingestion.Tests;

public class DouPageTests
{
    private static readonly DouParser Dou = new();

    // Елементи справжньої стрічки (жовтень 2026) з вигаданими компаніями. HTML-сутності в заголовку DOU кодує двічі:
    // «&amp;amp;» і «&amp;nbsp;», а опис — один раз («&lt;p&gt;»)
    internal const string SalaryItem = """
        <item><title>Middle Java Developer в Kvitka Bank, $2500–3000, за кордоном, віддалено</title><link>https://jobs.dou.ua/companies/kvitka-bank/vacancies/364458/?utm_source=jobsrss</link><description>&lt;p&gt;Шукаємо Java-розробника для &lt;b&gt;онлайн-банкінгу&lt;/b&gt;.&lt;/p&gt;</description><pubDate>Sun, 25 Oct 2026 21:04:06 +0200</pubDate><guid>https://jobs.dou.ua/companies/kvitka-bank/vacancies/364458/?1792955046</guid></item>
        """;

    internal const string EntitiesItem = """
        <item><title>Paid Search Manager (Google Ads &amp;amp; Microsoft Ads) в Lumos Fintech, від&amp;nbsp;$5000, віддалено</title><link>https://jobs.dou.ua/companies/lumos/vacancies/371570/?utm_source=jobsrss</link><description>&lt;p&gt;Опис&lt;/p&gt;</description><pubDate>Sat, 24 Oct 2026 09:15:00 +0300</pubDate><guid>https://jobs.dou.ua/companies/lumos/vacancies/371570/?1792900000</guid></item>
        """;

    internal const string NoSalaryItem = """
        <item><title>Менеджер в IT-компанію в Dnipro Data Works, Київ</title><link>https://jobs.dou.ua/companies/ddw/vacancies/288852/?utm_source=jobsrss</link><description>&lt;p&gt;Опис&lt;/p&gt;</description><pubDate>Fri, 23 Oct 2026 18:00:00 +0300</pubDate><guid>https://jobs.dou.ua/companies/ddw/vacancies/288852/?1</guid></item>
        """;

    [Fact]
    public void ParseListPage_ReadsFeedItems()
    {
        List<RawVacancy> jobs = Dou.ParseListPage(Feed(SalaryItem, EntitiesItem));

        Assert.Equal(2, jobs.Count);
        RawVacancy job = jobs[0];
        Assert.Equal(JobSource.Dou, job.Source);
        Assert.Equal("364458", job.Id);
        Assert.Equal("https://jobs.dou.ua/companies/kvitka-bank/vacancies/364458/", job.Url); // без utm_source
        Assert.Equal("Middle Java Developer", job.Title);
        Assert.Equal("Kvitka Bank", job.Company);
        Assert.Equal("$2500–3000", job.Salary);
        Assert.Equal("за кордоном, віддалено", job.Location);
        Assert.Equal("2026-10-25T21:04:06.0000000+02:00", job.Posted);
        Assert.Equal("<p>Шукаємо Java-розробника для <b>онлайн-банкінгу</b>.</p>", job.DescriptionHtml);
        Assert.Null(job.Tags); // окремого поля навичок на DOU немає
    }

    [Fact]
    public void ParseListPage_DecodesDoubleEncodedTitle()
    {
        RawVacancy job = Dou.ParseListPage(Feed(EntitiesItem))[0];

        Assert.Equal("Paid Search Manager (Google Ads & Microsoft Ads)", job.Title);
        Assert.Equal("від $5000", job.Salary);
        Assert.Equal("віддалено", job.Location);
    }

    [Fact]
    public void ParseListPage_SplitsTitleAtLastPreposition()
    {
        // «в» є й у назві посади: ділимо за останнім, а зарплати тут немає
        RawVacancy job = Dou.ParseListPage(Feed(NoSalaryItem))[0];

        Assert.Equal("Менеджер в IT-компанію", job.Title);
        Assert.Equal("Dnipro Data Works", job.Company);
        Assert.Null(job.Salary);
        Assert.Equal("Київ", job.Location);
    }

    [Theory]
    [InlineData("<item><title>Без компанії</title><link>https://jobs.dou.ua/companies/x/vacancies/1/</link></item>", "Без компанії", null)]
    [InlineData("<item><title>QA в Acme</title><link>https://jobs.dou.ua/companies/x/vacancies/1/</link><pubDate>вчора</pubDate></item>", "QA", "Acme")]
    public void ParseListPage_ToleratesMissingParts(string item, string title, string? company)
    {
        RawVacancy job = Assert.Single(Dou.ParseListPage(Feed(item)));

        Assert.Equal(title, job.Title);
        Assert.Equal(company, job.Company);
        Assert.Null(job.Posted);  // нечитабельна чи відсутня дата — нормалізація візьме момент збору
    }

    [Theory]
    [InlineData("<item><title>Стаття</title><link>https://dou.ua/lenta/articles/1/</link></item>")]       // не вакансія
    [InlineData("<item><title>Чужий сайт</title><link>https://evil.example/companies/x/vacancies/1/</link></item>")]
    [InlineData("")]                                                                                  // порожня стрічка
    public void ParseListPage_SkipsItemsThatAreNotDouVacancies(string item)
    {
        Assert.Empty(Dou.ParseListPage(Feed(item)));
    }

    [Fact]
    public void ParseListPage_RejectsDtd()
    {
        // Стрічку пише сайт: DTD із зовнішньою сутністю (XXE) не обробляємо взагалі
        string xml = """<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]><rss><channel><item><title>&x;</title></item></channel></rss>""";

        Assert.Throws<XmlException>(() => Dou.ParseListPage(xml));
    }

    [Fact]
    public void ListPageUrl_FeedHasSinglePage()
    {
        Assert.Equal("https://jobs.dou.ua/vacancies/feeds/", Dou.ListPageUrl(1));
        Assert.Null(Dou.ListPageUrl(2));
    }

    [Fact]
    public void ParseJobPage_ReadsSeparateFields()
    {
        RawVacancy details = Dou.ParseJobPage(JobPage());

        Assert.Equal("Middle Java Developer", details.Title);
        Assert.Equal("Kvitka Bank", details.Company); // без посилання «Всі вакансії компанії»
        Assert.Equal("$2500–3000", details.Salary);
        Assert.Equal("за кордоном, віддалено", details.Location);
        Assert.Equal("Java", details.Category);
        Assert.Contains("<li>Java 17, Spring Boot</li>", details.DescriptionHtml);
        Assert.Null(details.Tags);
    }

    [Fact]
    public void ParseJobPage_OptionalFieldsMayBeMissing()
    {
        string html = JobPage()
            .Replace("""<span class="salary">$2500–3000</span>""", string.Empty, StringComparison.Ordinal)
            .Replace("""/ <a href="https://jobs.dou.ua/vacancies/?category=Java">Java</a> / <a href="https://jobs.dou.ua/vacancies/?category=Java&amp;relocation">за кордоном</a>""", string.Empty, StringComparison.Ordinal);

        RawVacancy details = Dou.ParseJobPage(html);

        Assert.Null(details.Salary);
        Assert.Null(details.Category);
        Assert.Equal("Kvitka Bank", details.Company);
    }

    [Fact]
    public void ParseJobPage_PageWithoutVacancy_GivesNothing()
    {
        RawVacancy details = Dou.ParseJobPage("<html><body>Вакансію закрито</body></html>");

        Assert.Equal(new RawVacancy(), details);
    }

    [Fact]
    public void FeedAndJobPage_PassNormalization()
    {
        RawVacancy fromFeed = Dou.ParseListPage(Feed(SalaryItem))[0];
        RawVacancy job = SiteCrawler.WithJobPage(fromFeed, Dou.ParseJobPage(JobPage()));

        (VacancyDB? vacancy, string? error) = VacancyIngestionService.Normalize(job, new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero));

        Assert.Null(error);
        Assert.NotNull(vacancy);
        Assert.Equal("Dou", vacancy.Source);
        Assert.Equal("https://jobs.dou.ua/companies/kvitka-bank/vacancies/364458/", vacancy.SourceUrl);
        Assert.Equal((2500m, 3000m, "USD"), (vacancy.SalaryMin, vacancy.SalaryMax, vacancy.Currency));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 19, 4, 6, TimeSpan.Zero), vacancy.PublishedAt);
        Assert.Equal("Java", vacancy.Category);
        Assert.Equal("[]", vacancy.SkillsJson);
        Assert.StartsWith("Про проєкт\nОнлайн-банкінг", vacancy.Description, StringComparison.Ordinal); // повний опис зі сторінки
    }

    /// <summary>RSS-стрічка DOU з переданими елементами.</summary>
    internal static string Feed(params string[] items) =>
        """<?xml version="1.0" encoding="utf-8"?><rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom"><channel><title>Вакансії на DOU.ua</title><link>https://jobs.dou.ua/vacancies/?utm_source=jobsrss</link><description/><atom:link href="https://jobs.dou.ua/vacancies/feeds/" rel="self"/><language>uk</language>"""
        + string.Concat(items) + "</channel></rss>";

    /// <summary>Мінімальний елемент стрічки з заданим id.</summary>
    internal static string Item(int id) =>
        $"""<item><title>Вакансія {id} в Acme, Київ</title><link>https://jobs.dou.ua/companies/acme/vacancies/{id}/?utm_source=jobsrss</link><description>&lt;p&gt;Опис&lt;/p&gt;</description><pubDate>Sun, 25 Oct 2026 21:04:06 +0200</pubDate></item>""";

    /// <summary>Скорочена розмітка справжньої сторінки вакансії jobs.dou.ua (жовтень 2026) з вигаданою компанією.</summary>
    internal static string JobPage() => """
        <div class="b-vacancy">
          <div class="b-compinfo">
            <a class="logo" href="https://jobs.dou.ua/companies/kvitka-bank/"><img src="https://s.dou.ua/logo.png"></a>
            <div class="info">
              <div class="l-n"> <a href="https://jobs.dou.ua/companies/kvitka-bank/">Kvitka Bank</a> <a class="all-v" href="https://jobs.dou.ua/companies/kvitka-bank/vacancies/">Всі вакансії компанії</a> </div>
              <div class="l-t"> Банк, який розробляє власні цифрові сервіси. </div>
            </div>
          </div>
          <ul class="b-hr-admin-panel">
            <li class="breadcrumbs"> <a href="https://jobs.dou.ua/vacancies/">Всі вакансії</a> / <a href="https://jobs.dou.ua/vacancies/?category=Java">Java</a> / <a href="https://jobs.dou.ua/vacancies/?category=Java&amp;relocation">за кордоном</a> </li>
          </ul>
          <div class="l-vacancy" itemprop="description">
            <div class="date"> 25 жовтня 2026 </div>
            <h1 class="g-h2">Middle Java Developer</h1>
            <div class="sh-info"> <span class="place bi bi-geo-alt-fill"> за кордоном, віддалено</span> <span class="salary">$2500–3000</span> </div>
            <div class="b-typo vacancy-section"><p><b>Про проєкт</b></p><p>Онлайн-банкінг для мільйонів клієнтів.</p><ul><li>Java 17, Spring Boot</li></ul></div>
            <div class="reply"><a id="relogin-link" href="#apply">Відгукнутися</a></div>
          </div>
        </div>
        """;
}
