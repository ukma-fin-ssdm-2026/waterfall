// AI: отримання сторінок, захищених перевіркою на ботів (Cloudflare), через справжній браузер; перевірено: юніт-тести JobParserServiceTests з підробленим браузером, самоперевірка автора (§8)
// AI: перевірка Cloudflare розпізнається незалежно від мови браузера, сторінка вакансії robota.ua чекає опису (issue #62); перевірено: ручна перевірка robota.ua 14.11.2026, самоперевірка автора (§8)

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

            // Даємо Cloudflare Turnstile час тихо підтвердити домашній IP. Заголовок перевірки залежить від мови
            // браузера («Just a moment...» / «Трохи зачекайте…»), тож чекаємо за умовою BotCheckPage.PassedScript
            try
            {
                await page.WaitForFunctionAsync(BotCheckPage.PassedScript, new WaitForFunctionOptions { Timeout = 20000 });
            }
            catch (WaitTaskTimeoutException)
            {
                // Вміст сторінки перевірки не має стати вакансією: обхід сприйме це як збій сайту
                throw new TimeoutException($"Cloudflare не пропустив {url} за 20 секунд.");
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
                // Для сторінки ОДНІЄЇ ВАКАНСІЇ: чекаємо опис (#description-wrap) або хоча б назву вакансії.
                // Будь-який h1 не годиться: він є і на сторінці перевірки Cloudflare («robota.ua»)
                try
                {
                    await page.WaitForSelectorAsync("#description-wrap, h1[data-id='vacancy-title']", new WaitForSelectorOptions { Timeout = 12000 });
                }
                catch (WaitTaskTimeoutException)
                {
                    _logger.LogWarning("Robota.ua (вакансія): опис не з'явився за 12 секунд: {Url}", url);
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
        _lock.Dispose();
    }
}