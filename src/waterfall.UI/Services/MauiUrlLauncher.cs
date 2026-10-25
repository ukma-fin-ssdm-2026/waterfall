using Waterfall.Client.Interfaces;

namespace waterfall.UI.Services;

/// <summary>
/// Відкриває зовнішнє посилання через кросплатформний сервіс MAUI (Launcher.Default).
/// </summary>
public class MauiUrlLauncher : IUrlLauncher
{
    public async Task<bool> OpenAsync(Uri uri)
    {
        try
        {
            return await Launcher.Default.OpenAsync(uri);
        }
        catch
        {
            return false;
        }
    }
}
