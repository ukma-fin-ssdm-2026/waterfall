using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Workers;
using Waterfall.Services.Interfaces;
using Xunit;

namespace Waterfall.Api.Tests.Workers;

public sealed class BrowserCrawlerTests
{
    private sealed class FakeBrowserSite : IJobSiteParser, IBrowserOnlySite
    {
        public string Name => "fake.browser.site";
        public Uri Home => new("https://fake.site/");
        public string? ListPageUrl(int page) => page == 1 ? "https://fake.site/jobs" : null;
        public List<RawVacancy> ParseListPage(string html) =>
        [
            new RawVacancy { Id = "1", Title = "Browser Vacancy", Url = "https://fake.site/jobs/1" }
        ];
        public RawVacancy ParseJobPage(string html) =>
            new() { Title = "Browser Vacancy", DescriptionHtml = "<p>Full Description</p>" };
    }

    private sealed class TestBrowserFetcher(Func<string, string> responder) : IBrowserFetcher
    {
        public Task<string> GetPageAsync(string url, CancellationToken ct = default) =>
            Task.FromResult(responder(url));
    }

    private sealed class ThrowingBrowserFetcher(Exception exception) : IBrowserFetcher
    {
        public Task<string> GetPageAsync(string url, CancellationToken ct = default) =>
            Task.FromException<string>(exception);
    }

    private sealed class DummyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new HttpClientHandler());
    }

    [Fact]
    public async Task CrawlAsync_BrowserOnlySite_UsesBrowserFetcherForRobotsAndPages()
    {
        var site = new FakeBrowserSite();
        var fetcher = new TestBrowserFetcher(url =>
        {
            if (url.EndsWith("robots.txt"))
            {
                // Симулюємо HTML-обгортку <pre>, яку Chromium додає до текстових файлів
                return "<html><body><pre>User-agent: *\nAllow: /</pre></body></html>";
            }
            return "<html><body><div>Mock Content</div></body></html>";
        });

        var crawler = new SiteCrawler(
            site,
            pages: 1,
            requestDelay: TimeSpan.Zero,
            new DummyHttpClientFactory(),
            NullLogger.Instance,
            TimeProvider.System,
            fetcher);

        List<RawVacancy> results = await crawler.CrawlAsync(CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Browser Vacancy", results[0].Title);
        Assert.Equal("<p>Full Description</p>", results[0].DescriptionHtml);
    }

    [Fact]
    public async Task CrawlAsync_WhenBrowserThrowsTimeout_HandlesGracefullyWithoutCrashing()
    {
        var site = new FakeBrowserSite();
        // Симулюємо таймаут рушія Puppeteer
        var fetcher = new ThrowingBrowserFetcher(new TimeoutException("Puppeteer timed out"));

        var crawler = new SiteCrawler(
            site,
            pages: 1,
            requestDelay: TimeSpan.Zero,
            new DummyHttpClientFactory(),
            NullLogger.Instance,
            TimeProvider.System,
            fetcher);

        // Має не впасти, а штатно повернути порожній список згідно з THR-11
        List<RawVacancy> results = await crawler.CrawlAsync(CancellationToken.None);
        Assert.Empty(results);
    }
}