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

    private readonly ILogger<PlaywrightBrowserFetcher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightBrowserFetcher(ILogger<PlaywrightBrowserFetcher> logger)
    {
        _logger = logger;
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
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = (float)PageTimeout.TotalMilliseconds
                });
                // Після NetworkIdle SPA встигає домалювати список; короткий запас не зашкодить
                await page.WaitForTimeoutAsync(500);
                return await page.ContentAsync();
            }
            finally
            {
                await page.CloseAsync();
            }
        }
        catch (PlaywrightException ex)
        {
            throw new HttpRequestException($"браузер не зміг відкрити {url}: {ex.Message}", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is not null)
        {
            return _browser;
        }

        _playwright = await Playwright.CreateAsync();
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
