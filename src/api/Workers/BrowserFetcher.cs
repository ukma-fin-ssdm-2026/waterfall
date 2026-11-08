// AI: отримання сторінок, захищених перевіркою на ботів (Cloudflare), через справжній браузер; перевірено: юніт-тести JobParserServiceTests з підробленим браузером, самоперевірка автора (§8)

using PuppeteerSharp;

namespace Waterfall.Api.Workers;

public interface IBrowserFetcher
{
    Task<string> GetPageAsync(string url, CancellationToken ct = default);
}

public sealed class BrowserFetcher : IBrowserFetcher, IAsyncDisposable
{
    private readonly ILogger<BrowserFetcher> _logger;
    private IBrowser? _browser;
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

        _logger.LogInformation("Ініціалізація Chromium для обходу перевірок Cloudflare...");

        var fetcher = new PuppeteerSharp.BrowserFetcher();
        await fetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = false,
            Args =
            [
                // "--headless=new",
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-blink-features=AutomationControlled",
                "--window-size=1920,1080"
            ],
            IgnoredDefaultArgs = ["--enable-automation"]
        });

        return _browser;
    }

    public async Task<string> GetPageAsync(string url, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            IBrowser browser = await EnsureBrowserAsync();
            await using IPage page = await browser.NewPageAsync();

            await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
            await page.SetUserAgentAsync(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            _logger.LogDebug("Відкриваємо через браузер: {Url}", url);

            // Завжди використовуємо швидкий DOMContentLoaded, щоб не висіти на фонових трекерах
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.DOMContentLoaded],
                Timeout = 35000
            });

            // Даємо Cloudflare Turnstile час (1.5-3 сек) тихо підтвердити домашній IP
            int retries = 0;
            while (retries < 10)
            {
                string title = await page.GetTitleAsync();
                if (!title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                _logger.LogDebug("Очікування проходження Cloudflare перевірки...");
                await Task.Delay(1500, ct);
                retries++;
            }

            // Розділяємо: сторінка списку чи сторінка конкретної вакансії
            bool isRobotaList = url.Contains("robota.ua", StringComparison.OrdinalIgnoreCase) 
                                && url.Contains("/zapros", StringComparison.OrdinalIgnoreCase);

            bool isRobotaJobPage = url.Contains("robota.ua", StringComparison.OrdinalIgnoreCase) 
                                   && url.Contains("/vacancy", StringComparison.OrdinalIgnoreCase);

            if (isRobotaList)
            {
                // Для сторінки СПИСКУ: чекаємо появи карток
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

                await Task.Delay(1000, ct);
            }
            else if (isRobotaJobPage)
            {
                // Для сторінки ОДНІЄЇ ВАКАНСІЇ: чекаємо лише заголовок h1 (вантажиться за 1-2 сек!)
                try
                {
                    await page.WaitForSelectorAsync("h1", new WaitForSelectorOptions { Timeout = 6000 });
                }
                catch
                {
                    // Якщо h1 не з'явився вчасно, просто продовжуємо
                }

                await Task.Delay(500, ct);
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

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
        }
    }
}