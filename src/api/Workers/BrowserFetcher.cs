// AI: отримання сторінок, захищених перевіркою на ботів (Cloudflare), через справжній браузер; перевірено: юніт-тести JobParserServiceTests з підробленим браузером, самоперевірка автора (§8)
// AI: перевірка Cloudflare розпізнається незалежно від мови браузера, сторінка вакансії robota.ua чекає опису (issue #62); перевірено: ручна перевірка robota.ua 14.11.2026, самоперевірка автора (§8)

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using PuppeteerSharp;
namespace Waterfall.Api.Workers;

public interface IBrowserFetcher
{
    Task<string> GetPageAsync(string url, CancellationToken ct = default);
}

[ExcludeFromCodeCoverage] // SonarQube не буде штрафувати за запуск реального процесу браузера
public sealed class BrowserFetcher : IBrowserFetcher, IAsyncDisposable
{
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private readonly ILogger<BrowserFetcher> _logger;
    private IBrowser? _browser;
    private readonly ConcurrentDictionary<string, IPage> _sessionPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warmedHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public BrowserFetcher(ILogger<BrowserFetcher> logger)
    {
        _logger = logger;
    }

    private async Task<IBrowser> EnsureBrowserAsync()
    {
        if (_browser is { IsClosed: false })
        {
            return _browser;
        }

        _logger.LogInformation("Ініціалізація headless Chromium для обходу перевірок Cloudflare...");

        var fetcher = new PuppeteerSharp.BrowserFetcher();
        await fetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            Args =
            [
                "--headless=new",
                "--disable-blink-features=AutomationControlled",
                "--no-sandbox",
                "--disable-dev-shm-usage",
                "--window-size=1920,1080"
            ],
            IgnoredDefaultArgs = ["--enable-automation"]
        });

        return _browser;
    }

    private async Task<IPage> GetOrCreateSessionPageAsync(IBrowser browser, string host)
    {
        if (_sessionPages.TryGetValue(host, out IPage? existing) && !existing.IsClosed)
        {
            return existing;
        }

        _warmedHosts.Remove(host);
        IPage page = await browser.NewPageAsync();
        await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
        await page.SetUserAgentAsync(BrowserUserAgent);
        await page.EvaluateExpressionOnNewDocumentAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");
        await page.SetRequestInterceptionAsync(true);
        page.Request += async (_, e) =>
        {
            try
            {
                if (e.Request.ResourceType is ResourceType.Image or ResourceType.Media or ResourceType.Font)
                {
                    await e.Request.AbortAsync();
                }
                else
                {
                    await e.Request.ContinueAsync();
                }
            }
            catch
            {
               
            }
        };

        _sessionPages[host] = page;
        return page;
    }

    public async Task<string> GetPageAsync(string url, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            IBrowser browser = await EnsureBrowserAsync();
            Uri uri = new(url);
            string host = uri.Host;
            IPage page = await GetOrCreateSessionPageAsync(browser, host);
            bool hadWarmSession = _warmedHosts.Contains(host);

            bool isRobota = host.Contains("robota.ua", StringComparison.OrdinalIgnoreCase);

            // Для SSR-сайтів (Work.ua) після першого проходження Cloudflare завантажуємо наступні сторінки
            // через fetch() прямо у прогрітій сесії браузера (~100-250 мс замість повної навігації).
            if (hadWarmSession && !isRobota)
            {
                string? fastHtml = await TryFastFetchAsync(page, url, ct);
                if (fastHtml is not null)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("FastFetch через прогріту сесію: {Url}", url);
                    }

                    return fastHtml;
                }

                _warmedHosts.Remove(host);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Відкриваємо через headless браузер: {Url}", url);
            }

            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 35000
            });

            try
            {
                await page.WaitForFunctionAsync(BotCheckPage.PassedScript, new WaitForFunctionOptions { Timeout = 20000 });
            }
            catch (WaitTaskTimeoutException)
            {
                _warmedHosts.Remove(host);
                throw new TimeoutException($"Cloudflare не пропустив {url} за 20 секунд.");
            }

            _warmedHosts.Add(host);

            bool isRobotaList = isRobota && url.Contains("/zapros", StringComparison.OrdinalIgnoreCase);
            bool isRobotaJobPage = isRobota && url.Contains("/vacancy", StringComparison.OrdinalIgnoreCase);

            if (isRobotaList)
            {
                await page.Mouse.MoveAsync(200, 200);
                await page.Mouse.MoveAsync(400, 500);

                try
                {
                    await page.WaitForFunctionAsync(@"() => {
                        const links = document.querySelectorAll('main a[href*=""/company""], main a[href*=""/vacancy""]');
                        return links.length > 0;
                    }", new WaitForFunctionOptions { Timeout = 12000 });
                }
                catch (WaitTaskTimeoutException)
                {
                    _logger.LogWarning("Robota.ua (список): картки не з'явилися за 12 секунд.");
                }

                await Task.Delay(300, ct);
            }
            else if (isRobotaJobPage)
            {
                try
                {
                    await page.WaitForSelectorAsync("#description-wrap, h1[data-id='vacancy-title']", new WaitForSelectorOptions { Timeout = 12000 });
                }
                catch (WaitTaskTimeoutException)
                {
                    _logger.LogWarning("Robota.ua (вакансія): опис не з'явився за 12 секунд: {Url}", url);
                }

                await Task.Delay(200, ct);
            }

            return await page.GetContentAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Помилка під час завантаження через браузер URL: {Url}", url);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string?> TryFastFetchAsync(IPage page, string url, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            string? html = await page.EvaluateFunctionAsync<string?>(
                """
                async (targetUrl) => {
                    try {
                        const response = await fetch(targetUrl, {
                            credentials: 'include',
                            headers: { 'Accept': 'text/html,application/xhtml+xml,*/*' }
                        });
                        if (!response.ok) return null;
                        return await response.text();
                    } catch {
                        return null;
                    }
                }
                """,
                url);

            if (string.IsNullOrWhiteSpace(html) || !html.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (BotCheckPage.Is(Html.Load(html).DocumentNode))
            {
                return null;
            }

            return html;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(ex, "FastFetch не вдався для {Url}, переходимо на повну навігацію.", url);
            }

            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IPage page in _sessionPages.Values)
        {
            try
            {
                if (!page.IsClosed)
                {
                    await page.CloseAsync();
                }
            }
            catch
            {
            }
        }
        _sessionPages.Clear();

        if (_browser != null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
        }
        _lock.Dispose();
    }
}
