using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Waterfall.Api.Workers;

namespace Waterfall.Ingestion.Tests;

public sealed class PlaywrightBrowserFetcherTests
{
    [Fact]
    public async Task GetHtmlAsync_LaunchesHeadlessBrowserAndClosesEachPage()
    {
        const string url = "https://www.work.ua/jobs/?ss=1";
        const string html = "<html><body>jobs</body></html>";
        List<string> calls = [];
        int launchCount = 0;
        IPlaywright playwright = PlaywrightTestProxy.Create<IPlaywright>((name, _) =>
            name == "get_Chromium"
                ? PlaywrightTestProxy.Create<IBrowserType>((launchName, args) =>
                {
                    if (launchName != "LaunchAsync")
                    {
                        throw new InvalidOperationException($"Unexpected browser type call: {launchName}");
                    }

                    launchCount++;
                    Assert.True(((BrowserTypeLaunchOptions)args[0]!).Headless);
                    return Task.FromResult(PlaywrightTestProxy.Create<IBrowser>((browserName, _) =>
                    {
                        if (browserName == "DisposeAsync")
                        {
                            return ValueTask.CompletedTask;
                        }

                        if (browserName != "NewPageAsync")
                        {
                            throw new InvalidOperationException($"Unexpected browser call: {browserName}");
                        }

                        return Task.FromResult(CreatePage(calls, html));
                    }));
                })
                : name == "Dispose"
                    ? null
                    : throw new InvalidOperationException($"Unexpected Playwright call: {name}"));
        await using PlaywrightBrowserFetcher fetcher = CreateFetcher(playwright);

        Assert.Equal(html, await fetcher.GetHtmlAsync(url, CancellationToken.None));
        Assert.Equal(html, await fetcher.GetHtmlAsync(url, CancellationToken.None));

        Assert.Equal(1, launchCount);
        Assert.Equal(2, calls.Count(name => name == "ClearCookiesAsync"));
        Assert.Equal(2, calls.Count(name => name == "WaitForTimeoutAsync:500"));
        Assert.Equal(2, calls.Count(name => name == "CloseAsync"));
        Assert.Equal(2, calls.Count(name => name == $"GotoAsync:{url}:DOMContentLoaded:30000"));
        Assert.Equal(2, calls.Count(name => name == "WaitForSelectorAsync:div.job-link:Visible:10000"));
    }

    [Fact]
    public async Task GetHtmlAsync_WhenNavigationFails_ThrowsHttpRequestExceptionWithUrl()
    {
        const string url = "https://example.test/jobs";
        PlaywrightException failure = new("navigation failed");
        bool pageClosed = false;
        IPage page = CreatePage([], string.Empty, failure, () => pageClosed = true);
        await using PlaywrightBrowserFetcher fetcher = CreateFetcher(CreatePlaywright(page));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => fetcher.GetHtmlAsync(url, CancellationToken.None));

        Assert.Contains(url, exception.Message);
        Assert.Same(failure, exception.InnerException);
        Assert.True(pageClosed);
    }

    [Fact]
    public async Task GetHtmlAsync_WhenContentWaitTimesOut_ThrowsHttpRequestExceptionWithUrl()
    {
        const string url = "https://www.work.ua/jobs/?ss=1";
        TimeoutException failure = new("content selector timed out");
        IPage page = CreatePage([], string.Empty, selectorFailure: failure);
        await using PlaywrightBrowserFetcher fetcher = CreateFetcher(CreatePlaywright(page));

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => fetcher.GetHtmlAsync(url, CancellationToken.None));

        Assert.Contains(url, exception.Message);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task GetHtmlAsync_WhenContentWaitTimesOut_LogsPageDiagnostics()
    {
        const string url = "https://www.work.ua/jobs/?ss=1";
        string body = new('x', 350);
        RecordingLogger logger = new();
        PlaywrightException failure = new("selector timed out");
        IPage page = CreatePage([], string.Empty, selectorFailure: failure, body: body);
        await using PlaywrightBrowserFetcher fetcher = new(logger, () => Task.FromResult(CreatePlaywright(page)));

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.GetHtmlAsync(url, CancellationToken.None));

        string warning = Assert.Single(logger.Warnings);
        Assert.Contains("div.job-link", warning);
        Assert.Contains(url, warning);
        Assert.Contains("Work.ua test page", warning);
        Assert.Contains(new string('x', 300), warning);
        Assert.DoesNotContain(new string('x', 301), warning);
    }

    [Fact]
    public async Task GetHtmlAsync_WhenPageDiagnosticsFail_LogsFallbackWarning()
    {
        const string url = "https://www.work.ua/jobs/?ss=1";
        RecordingLogger logger = new();
        PlaywrightException failure = new("selector timed out");
        IPage page = CreatePage(
            [],
            string.Empty,
            selectorFailure: failure,
            diagnosticFailure: new PlaywrightException("page detached"));
        await using PlaywrightBrowserFetcher fetcher = new(logger, () => Task.FromResult(CreatePlaywright(page)));

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.GetHtmlAsync(url, CancellationToken.None));

        string warning = Assert.Single(logger.Warnings);
        Assert.Contains("не вдалося прочитати діагностику сторінки", warning);
        Assert.Contains("page detached", warning);
    }

    [Fact]
    public async Task GetHtmlAsync_WhenCancellationIsAlreadyRequested_DoesNotLaunchBrowser()
    {
        int factoryCalls = 0;
        await using PlaywrightBrowserFetcher fetcher = new(
            NullLogger<PlaywrightBrowserFetcher>.Instance,
            () =>
            {
                factoryCalls++;
                return Task.FromResult(CreatePlaywright(CreatePage([], string.Empty)));
            });
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetcher.GetHtmlAsync("https://example.test/jobs", cancellation.Token));

        Assert.Equal(0, factoryCalls);
    }

    [Theory]
    [InlineData("not a url", false)]
    [InlineData("https://www.work.ua/robots.txt", false)]
    [InlineData("https://www.work.ua/jobs/?ss=1", true)]
    [InlineData("https://www.work.ua/jobs/42/", false)]
    [InlineData("https://work.ua/jobs/?ss=1", false)]
    [InlineData("https://robota.ua/zapros/ukraine", true)]
    [InlineData("https://robota.ua/company1/vacancy42", false)]
    public async Task GetHtmlAsync_WaitsForContentOnlyOnSupportedListingUrls(string url, bool shouldWait)
    {
        List<string> calls = [];
        await using PlaywrightBrowserFetcher fetcher = CreateFetcher(CreatePlaywright(CreatePage(calls, "<html />")));

        await fetcher.GetHtmlAsync(url, CancellationToken.None);

        Assert.Equal(shouldWait, calls.Any(call => call.StartsWith("WaitForSelectorAsync:", StringComparison.Ordinal)));
    }

    private static PlaywrightBrowserFetcher CreateFetcher(IPlaywright playwright) =>
        new(NullLogger<PlaywrightBrowserFetcher>.Instance, () => Task.FromResult(playwright));

    private static IPlaywright CreatePlaywright(IPage page) =>
        PlaywrightTestProxy.Create<IPlaywright>((name, _) =>
            name == "get_Chromium"
                ? PlaywrightTestProxy.Create<IBrowserType>((launchName, _) =>
                    launchName == "LaunchAsync"
                        ? Task.FromResult(PlaywrightTestProxy.Create<IBrowser>((browserName, _) =>
                            browserName switch
                            {
                                "NewPageAsync" => Task.FromResult(page),
                                "DisposeAsync" => ValueTask.CompletedTask,
                                _ => throw new InvalidOperationException($"Unexpected browser call: {browserName}")
                            }))
                        : throw new InvalidOperationException($"Unexpected browser type call: {launchName}"))
                : name == "Dispose"
                    ? null
                    : throw new InvalidOperationException($"Unexpected Playwright call: {name}"));

    private static IPage CreatePage(
        List<string> calls,
        string html,
        Exception? navigationFailure = null,
        Action? onClose = null,
        Exception? selectorFailure = null,
        Exception? diagnosticFailure = null,
        string body = "No vacancy cards")
    {
        IBrowserContext context = PlaywrightTestProxy.Create<IBrowserContext>((name, _) =>
        {
            if (name == "ClearCookiesAsync")
            {
                calls.Add(name);
                return Task.CompletedTask;
            }

            throw new InvalidOperationException($"Unexpected browser context call: {name}");
        });

        return PlaywrightTestProxy.Create<IPage>((name, args) =>
        {
            switch (name)
            {
                case "get_Context":
                    return context;
                case "get_Url":
                    return "https://www.work.ua/jobs/?ss=1";
                case "TitleAsync":
                    return Task.FromResult("Work.ua test page");
                case "Locator":
                    return PlaywrightTestProxy.Create<ILocator>((locatorName, _) =>
                        locatorName == "InnerTextAsync"
                            ? diagnosticFailure is null
                                ? Task.FromResult(body)
                                : Task.FromException<string>(diagnosticFailure)
                            : throw new InvalidOperationException($"Unexpected locator call: {locatorName}"));
                case "GotoAsync":
                    PageGotoOptions options = (PageGotoOptions)args[1]!;
                    calls.Add($"{name}:{args[0]}:{options.WaitUntil}:{options.Timeout:0}");
                    return navigationFailure is null
                        ? Task.FromResult<IResponse?>(null)
                        : Task.FromException<IResponse?>(navigationFailure);
                case "WaitForTimeoutAsync":
                    calls.Add($"{name}:{args[0]}");
                    return Task.CompletedTask;
                case "WaitForSelectorAsync":
                    PageWaitForSelectorOptions waitOptions = (PageWaitForSelectorOptions)args[1]!;
                    calls.Add($"{name}:{args[0]}:{waitOptions.State}:{waitOptions.Timeout:0}");
                    return selectorFailure is null
                        ? Task.FromResult<IElementHandle?>(null)
                        : Task.FromException<IElementHandle?>(selectorFailure);
                case "ContentAsync":
                    return Task.FromResult(html);
                case "CloseAsync":
                    calls.Add(name);
                    onClose?.Invoke();
                    return Task.CompletedTask;
                default:
                    throw new InvalidOperationException($"Unexpected page call: {name}");
            }
        });
    }

    private sealed class RecordingLogger : ILogger<PlaywrightBrowserFetcher>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}

public class PlaywrightTestProxy : DispatchProxy
{
    private Func<string, object?[], object?> _handler = null!;

    public static T Create<T>(Func<string, object?[], object?> handler)
        where T : class
    {
        T proxy = DispatchProxy.Create<T, PlaywrightTestProxy>();
        ((PlaywrightTestProxy)(object)proxy)._handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        _handler(targetMethod!.Name, args ?? []);
}
