namespace waterfall.UI.Services;

/// <summary>
/// Platform root navigation host capable of routing across all app tabs/sub-pages.
/// </summary>
internal interface IAppNavigationHost
{
    Task NavigateToAsync(string rawRoute);
}
