// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.Interfaces;

namespace waterfall.UI.Services;

/// <summary>
/// Перемикання вкладок Shell за абсолютними маршрутами з AppShell.xaml.
/// </summary>
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToCatalogAsync() => GoToAsync("//catalog");

    public Task GoToAiConsultantAsync() => GoToAsync("//aiconsultant");

    /// <summary>
    /// Shell вимагає виклику з UI-потоку; якщо вікно ще не створене, навігація просто не потрібна.
    /// </summary>
    private static Task GoToAsync(string route) =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current is Shell shell ? shell.GoToAsync(route) : Task.CompletedTask);
}
