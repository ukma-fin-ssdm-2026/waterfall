using System.Diagnostics;
using Waterfall.Client.Interfaces;

namespace Waterfall.Client.Services;

/// <summary>
/// Стандартна реалізація відкриття зовнішнього URL через системну оболонку (Process.Start).
/// </summary>
public class DefaultUrlLauncher : IUrlLauncher
{
    public Task<bool> OpenAsync(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
