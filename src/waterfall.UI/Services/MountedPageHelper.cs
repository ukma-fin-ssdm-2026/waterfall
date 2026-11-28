using waterfall.UI.Views;
using Waterfall.Client.Interfaces;
using Waterfall.Client.Models.Auth;

namespace waterfall.UI.Services;

internal sealed record MountedPage(ContentPage Page, ContentView Wrapper);

/// <summary>
/// Shared page-mounting, role resolution, lifecycle dispatch, and horizontal slide animation helpers
/// used by <see cref="DesktopHostPage"/>, <see cref="PhoneHostPage"/>, and <see cref="PhoneSectionHostPage"/>.
/// </summary>
internal static class MountedPageHelper
{
    public static MountedPage Mount(ContentPage page)
    {
        View? body = page.Content;
        if (body is not null)
        {
            // Preserve BindingContext BEFORE detaching from page so two-way Picker/Switch bindings never see null.
            body.BindingContext = page.BindingContext;
        }

        page.Content = null;

        ContentView wrapper = new()
        {
            BindingContext = page.BindingContext,
            Resources = page.Resources,
            Content = body,
            IsVisible = false
        };

        return new MountedPage(page, wrapper);
    }

    public static void Activate(ContentPage page) => (page as IHostedPage)?.OnHostActivated();

    public static void ApplyStyle(VisualElement element, string resourceKey)
    {
        if (Application.Current?.Resources.TryGetValue(resourceKey, out object? value) == true && value is Style style)
        {
            element.Style = style;
        }
    }

    public static void Deactivate(ContentPage page) => (page as IHostedPage)?.OnHostDeactivated();

    public static bool HandleBack(ContentPage page) => (page as IHostedPage)?.OnHostBackPressed() == true;

    public static async Task<(bool IsLoggedIn, UserRole? Role, string RoleLabel)> ResolveRoleAsync(IAuthTokenStore? tokens)
    {
        if (tokens is null)
        {
            return (false, null, "Гостьовий режим");
        }

        try
        {
            string? token = await tokens.GetTokenAsync(CancellationToken.None);
            AuthSession? session = token is null ? null : AuthSession.FromToken(token);
            if (session is not null && !session.IsExpired(DateTimeOffset.UtcNow))
            {
                string roleLabel = session.Role switch
                {
                    UserRole.Analyst => "Аналітик ринку",
                    UserRole.Admin => "Адміністратор",
                    _ => "Шукач роботи"
                };
                return (true, session.Role, roleLabel);
            }
        }
        catch
        {
        }

        return (false, null, "Гостьовий режим");
    }


    public static async Task SlideSwitchAsync(View outgoing, View incoming, double hostWidth, int direction, uint durationMs = 200)
    {
        int dir = direction >= 0 ? 1 : -1;
        double dist = Math.Max(hostWidth, 320);

        outgoing.CancelAnimations();
        incoming.CancelAnimations();

        incoming.TranslationX = dir * dist;
        incoming.Opacity = 1;
        incoming.IsVisible = true;

        try
        {
            await Task.WhenAll(
                outgoing.TranslateToAsync(-dir * dist, 0, durationMs, Easing.CubicOut),
                incoming.TranslateToAsync(0, 0, durationMs, Easing.CubicOut));
        }
        finally
        {
            outgoing.IsVisible = false;
            outgoing.TranslationX = 0;
            incoming.TranslationX = 0;
        }
    }
}
