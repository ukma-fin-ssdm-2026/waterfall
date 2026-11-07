using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Waterfall.Api.Workers;

namespace Waterfall.Ingestion.Tests;

public sealed class PlaywrightBrowserFetcherTests
{
    [Fact]
    public async Task GetHtmlAsync_ReturnsJavaScriptRenderedHtml()
    {
        const string html = """
            <!doctype html>
            <html>
            <body>
                <main id="jobs"></main>
                <script>
                    document.querySelector("#jobs").textContent = "Rendered vacancy";
                </script>
            </body>
            </html>
            """;
        await using PlaywrightBrowserFetcher fetcher = CreateFetcher();

        string result = await fetcher.GetHtmlAsync(ToDataUrl(html), CancellationToken.None);

        Assert.Contains("Rendered vacancy", result);
    }

    [Fact]
    public async Task GetHtmlAsync_CanReuseBrowserForMultiplePages()
    {
        await using PlaywrightBrowserFetcher fetcher = CreateFetcher();

        string first = await fetcher.GetHtmlAsync(ToDataUrl("<p>first</p>"), CancellationToken.None);
        string second = await fetcher.GetHtmlAsync(ToDataUrl("<p>second</p>"), CancellationToken.None);

        Assert.Contains("first", first);
        Assert.Contains("second", second);
    }

    [Fact]
    public async Task GetHtmlAsync_WhenNavigationFails_ThrowsHttpRequestExceptionWithUrl()
    {
        const string url = "not a valid URL";
        await using PlaywrightBrowserFetcher fetcher = CreateFetcher();

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => fetcher.GetHtmlAsync(url, CancellationToken.None));

        Assert.Contains(url, exception.Message);
        Assert.IsType<Microsoft.Playwright.PlaywrightException>(exception.InnerException);
    }

    [Fact]
    public async Task GetHtmlAsync_WhenCancellationIsAlreadyRequested_ThrowsWithoutOpeningPage()
    {
        await using PlaywrightBrowserFetcher fetcher = CreateFetcher();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetcher.GetHtmlAsync(ToDataUrl("<p>unused</p>"), cancellation.Token));
    }

    private static PlaywrightBrowserFetcher CreateFetcher() =>
        new(NullLogger<PlaywrightBrowserFetcher>.Instance);

    private static string ToDataUrl(string html) =>
        $"data:text/html;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(html))}";
}
