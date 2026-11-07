// AI: отримання сторінок, захищених перевіркою на ботів (Cloudflare), через справжній браузер; перевірено: юніт-тести JobParserServiceTests з підробленим браузером, самоперевірка автора (§8)

using Microsoft.Playwright;

namespace Waterfall.Api.Workers;

/// <summary>
/// Отримує сторінки, які звичайний <see cref="HttpClient"/> не може відкрити: сайт відповідає 403
/// з перевіркою Cloudflare або рендерить список вакансій лише в браузері. Працює на зразок браузера користувача,
/// тож сервер бачить запит від справжнього движка Chromium, а не від бота.
/// </summary>
public interface IBrowserFetcher
{
    /// <summary>Повністю завантажує сторінку (разом із JavaScript) і повертає її остаточну розмітку.</summary>
    Task<string> GetHtmlAsync(string url, CancellationToken ct);
}

/// <summary>
/// Браузерний завантажувач на Playwright (Chromium). Один екземпляр браузера на весь застосунок,
/// сторінки відкриває по черзі. Cookies між сторінками не зберігає: кожен збір починається з чистої сесії,
/// щоб не залежати від попереднього стану.
/// </summary>
public sealed class PlaywrightBrowserFetcher : IBrowserFetcher, IAsyncDisposable
{
    private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ContentTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<PlaywrightBrowserFetcher> _logger;
    private readonly Func<Task<IPlaywright>> _createPlaywright;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightBrowserFetcher(ILogger<PlaywrightBrowserFetcher> logger)
        : this(logger, Playwright.CreateAsync)
    {
    }

    internal PlaywrightBrowserFetcher(
        ILogger<PlaywrightBrowserFetcher> logger,
        Func<Task<IPlaywright>> createPlaywright)
    {
        _logger = logger;
        _createPlaywright = createPlaywright;
    }

    public async Task<string> GetHtmlAsync(string url, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            IBrowser browser = await GetBrowserAsync().WaitAsync(ct);
            IPage page = await browser.NewPageAsync().WaitAsync(ct);
            try
            {
                // Без підказок і кешу: збір — це безголовий обхід, а не робота користувача
                await page.Context.ClearCookiesAsync();
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = (float)PageTimeout.TotalMilliseconds
                });
                string? selector = GetContentSelector(url);
                if (selector is not null)
                {
                    try
                    {
                        await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
                        {
                            State = WaitForSelectorState.Visible,
                            Timeout = (float)ContentTimeout.TotalMilliseconds
                        });
                    }
                    catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                    {
                        await LogMissingContentAsync(page, url, selector, ex);
                        throw;
                    }
                }

                // Дайте сторінці трохи часу завершити рендер карток після появи першого елемента
                await page.WaitForTimeoutAsync(500);
                return await page.ContentAsync();
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            throw new HttpRequestException($"браузер не зміг відкрити {url}: {ex.Message}", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? GetContentSelector(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || uri.AbsolutePath.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (uri.Host.Equals("www.work.ua", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.TrimEnd('/') == "/jobs" ? "div.job-link" : null;
        }

        return uri.Host.Equals("robota.ua", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/zapros/", StringComparison.OrdinalIgnoreCase)
                ? "a[href*='/vacancy']"
                : null;
    }

    private async Task LogMissingContentAsync(IPage page, string url, string selector, Exception error)
    {
        try
        {
            string title = await page.TitleAsync();
            string body = await page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions
            {
                Timeout = 2_000
            });
            string preview = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (preview.Length > 300)
            {
                preview = preview[..300];
            }

            _logger.LogWarning(
                error,
                "Не з'явився селектор {Selector} для {Url}; сторінка {PageUrl}, заголовок {Title}, текст: {Text}",
                selector,
                url,
                page.Url,
                title,
                preview);
        }
        catch (Exception diagnosticError) when (diagnosticError is PlaywrightException or TimeoutException)
        {
            _logger.LogWarning(
                error,
                "Не з'явився селектор {Selector} для {Url}; не вдалося прочитати діагностику сторінки: {DiagnosticError}",
                selector,
                url,
                diagnosticError.Message);
        }
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is not null)
        {
            return _browser;
        }

        _playwright = await _createPlaywright();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _logger.LogInformation("Браузер для фонового збору запущено.");
        return _browser;
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
