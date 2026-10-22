namespace Waterfall.Client.Interfaces;

/// <summary>
/// Відкриває зовнішнє посилання у браузері користувача.
/// </summary>
public interface IUrlLauncher
{
    /// <summary>
    /// Відкриває зазначений URI у системному веб-браузері.
    /// </summary>
    /// <param name="uri">Абсолютний адрес посилання.</param>
    /// <returns>True, якщо запуск відбувся успішно, інакше False.</returns>
    Task<bool> OpenAsync(Uri uri);
}
