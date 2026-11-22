using waterfall.UI.Views;
using Waterfall.Client.Interfaces;

namespace waterfall.UI.Services;

/// <summary>
/// Navigates across platform root navigation hosts (<see cref="DesktopHostPage"/> on Windows, <see cref="PhoneHostPage"/> on mobile).
/// </summary>
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToCatalogAsync() => NavigateToAsync("catalog");

    public Task GoToAiConsultantAsync() => NavigateToAsync("aiconsultant");

    private static Task NavigateToAsync(string route) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            IAppNavigationHost? host = (IAppNavigationHost?)DesktopHostPage.Current ?? PhoneHostPage.Current;
            return host?.NavigateToAsync(route) ?? Task.CompletedTask;
        });
}
