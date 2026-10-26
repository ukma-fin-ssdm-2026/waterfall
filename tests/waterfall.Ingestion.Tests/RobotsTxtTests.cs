// AI: тести розбору robots.txt за RFC 9309 (issue #62); перевірено: запуск dotnet test, звірка зі справжніми robots.txt djinni.co і work.ua (жовтень 2026), самоперевірка автора (§8)

using Waterfall.Api.Workers;

namespace Waterfall.Ingestion.Tests;

public class RobotsTxtTests
{
    // Справжній robots.txt djinni.co станом на жовтень 2026
    internal const string Djinni = "User-agent: *\nDisallow: /jobs2\nDisallow: /q\nDisallow: /developers\nDisallow: /free-jobs\nDisallow: /set_lang\n\nSitemap: https://djinni.co/sitemap.xml\n";

    // Уривок справжнього robots.txt work.ua (жовтень 2026): правила з «*» і «$», Allow поряд із Disallow, окрема група для іншого бота
    internal const string WorkUa = """
        # robots.txt
        User-agent: Linguee
        Disallow: /

        User-agent: *

        Allow: /jobseeker/my/resumes/$
        Allow: /*/?*student=

        Disallow: /*/print/
        Disallow: /*.php
        Disallow: /jobseeker/my/
        Disallow: /jobs-*-/
        Disallow: /*/?*sort=
        Disallow: /*/?*experience=

        Sitemap: https://www.work.ua/sitemap_index.xml
        """;

    // Уривок справжнього robots.txt jobs.dou.ua (жовтень 2026): загальні боти, окремі групи для Yandex і заборонених ботів
    internal const string Dou = """
        User-agent: *
        Disallow: /ajax-impressions-track/
        Disallow: /ajax-login/
        Disallow: /comment-action/

        User-agent: Yandex
        Disallow: /vacancies/*search=*

        User-agent: Slurp
        disallow: /
        """;

    [Theory]
    [InlineData("/vacancies/feeds/", true)]
    [InlineData("/companies/acme/vacancies/364458/", true)]
    [InlineData("/vacancies/?search=java", true)] // правило лише для Yandex
    [InlineData("/ajax-login/", false)]
    public void Dou_AllowsFeedAndVacancyPages(string path, bool allowed)
    {
        Assert.Equal(allowed, RobotsTxt.Parse(Dou).IsAllowed(path));
    }

    [Theory]
    [InlineData("/jobs/", true)]
    [InlineData("/jobs/?page=2", true)]
    [InlineData("/jobs/845516-middle-net-developer/", true)]
    [InlineData("/jobs2/", false)]
    [InlineData("/q/search/", false)]
    public void Djinni_AllowsJobListAndJobPages(string path, bool allowed)
    {
        Assert.Equal(allowed, RobotsTxt.Parse(Djinni).IsAllowed(path));
    }

    [Theory]
    [InlineData("/jobs/?ss=1", true)]
    [InlineData("/jobs/?ss=1&page=2", true)]
    [InlineData("/jobs/8051901/", true)]
    [InlineData("/jobs/?ss=1&sort=date", false)]          // «*» посередині: /*/?*sort=
    [InlineData("/jobs/?ss=1&page=2&experience=1", false)]
    [InlineData("/jobs-kyiv-/", false)]                   // /jobs-*-/
    [InlineData("/jobs/8051901/print/", false)]
    [InlineData("/index.php", false)]
    [InlineData("/jobseeker/my/resumes/", true)]          // Allow із «$» довше за Disallow /jobseeker/my/
    [InlineData("/jobseeker/my/resumes/1/", false)]       // «$» — лише точний шлях
    [InlineData("/jobs/?student=1&sort=date", true)]      // Allow /*/?*student= довше за Disallow /*/?*sort=
    public void WorkUa_FollowsWildcardsEndAnchorsAndLongestRule(string path, bool allowed)
    {
        Assert.Equal(allowed, RobotsTxt.Parse(WorkUa).IsAllowed(path));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("User-agent: *\nDisallow: /jobs", false)]
    [InlineData("User-agent: *\r\nDisallow: /\r\n", false)]
    [InlineData("User-agent: *\nDisallow:", true)]                                           // порожній Disallow нічого не забороняє
    [InlineData("User-agent: Googlebot\nDisallow: /jobs\n\nUser-agent: *\nDisallow: /q", true)] // правило іншого бота нас не стосується
    [InlineData("User-agent: Googlebot\nUser-agent: *\nDisallow: /jobs", false)]              // кілька User-agent поспіль — одна група
    [InlineData("user-agent: *\nDISALLOW: /jobs", false)]                                     // назви полів без урахування регістру
    [InlineData("# Disallow: /jobs\nUser-agent: *\nDisallow: /q # Disallow: /jobs", true)]
    [InlineData("User-agent: *\nDisallow: /jobs\nAllow: /jobs/", true)]                       // Allow /jobs/ довше за Disallow /jobs
    [InlineData("User-agent: *\nDisallow: /jobs/\nAllow: /jobs/", true)]                      // однакова довжина — перемагає Allow
    public void Parse_FollowsRfc9309(string robotsTxt, bool allowed)
    {
        Assert.Equal(allowed, RobotsTxt.Parse(robotsTxt).IsAllowed("/jobs/"));
    }

    [Fact]
    public void AllowAll_AllowsEverything()
    {
        Assert.True(RobotsTxt.AllowAll.IsAllowed("/anything/?at=all"));
    }
}
