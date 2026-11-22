using waterfall.UI.Controls;

namespace waterfall.UI.Views;

/// <summary>
/// Shared slide/fade overlay sheet animator for Catalog and Database pages.
/// </summary>
internal static class OverlaySheetAnimator
{
    internal sealed class SheetState
    {
        public int Version;
    }

    public static Task AnimateAsync(
        ContentView host,
        ResourceDictionary resources,
        string templateKey,
        object bindingContext,
        bool open,
        SheetState state)
    {
        if (!MainThread.IsMainThread)
        {
            return MainThread.InvokeOnMainThreadAsync(() =>
                AnimateOnMainThreadAsync(host, resources, templateKey, bindingContext, open, state));
        }

        return AnimateOnMainThreadAsync(host, resources, templateKey, bindingContext, open, state);
    }

    private static async Task AnimateOnMainThreadAsync(
        ContentView host,
        ResourceDictionary resources,
        string templateKey,
        object bindingContext,
        bool open,
        SheetState state)
    {
        int token = ++state.Version;
        bool isPhone = DeviceInfo.Idiom == DeviceIdiom.Phone;

        if (open)
        {
            if (host.Content is null &&
                resources.TryGetValue(templateKey, out object? tpl) &&
                tpl is DataTemplate template &&
                template.CreateContent() is View sheet)
            {
                sheet.BindingContext = bindingContext;
                host.Content = sheet;
            }

            if (host.Content is not View root)
            {
                host.IsVisible = true;
                return;
            }

            (BoxView? backdrop, Border? card) = ResolveChrome(root);

            backdrop?.CancelAnimations();
            card?.CancelAnimations();

            if (backdrop is not null)
            {
                backdrop.Opacity = 0;
            }

            if (card is not null)
            {
                if (isPhone)
                {
                    card.TranslationX = 0;
                    card.TranslationY = 440;
                }
                else
                {
                    card.TranslationY = 0;
                    card.TranslationX = 460;
                }
            }

            host.IsVisible = true;

            List<Task> animations = [];
            if (backdrop is not null)
            {
                animations.Add(backdrop.FadeToAsync(1, 180, Easing.CubicOut));
            }

            if (card is not null)
            {
                animations.Add(isPhone
                    ? card.TranslateToAsync(0, 0, 220, Easing.CubicOut)
                    : card.TranslateToAsync(0, 0, 200, Easing.CubicOut));
            }

            if (animations.Count > 0)
            {
                await Task.WhenAll(animations);
            }
        }
        else
        {
            if (!host.IsVisible)
            {
                return;
            }

            if (host.Content is View root)
            {
                (BoxView? backdrop, Border? card) = ResolveChrome(root);

                backdrop?.CancelAnimations();
                card?.CancelAnimations();

                List<Task> animations = [];
                if (backdrop is not null)
                {
                    animations.Add(backdrop.FadeToAsync(0, 160, Easing.CubicIn));
                }

                if (card is not null)
                {
                    animations.Add(isPhone
                        ? card.TranslateToAsync(0, 440, 180, Easing.CubicIn)
                        : card.TranslateToAsync(460, 0, 180, Easing.CubicIn));
                }

                if (animations.Count > 0)
                {
                    await Task.WhenAll(animations);
                }

                if (state.Version == token)
                {
                    host.IsVisible = false;
                    if (card is not null)
                    {
                        card.TranslationX = 0;
                        card.TranslationY = 0;
                    }

                    if (backdrop is not null)
                    {
                        backdrop.Opacity = 1;
                    }
                }
            }
            else if (state.Version == token)
            {
                host.IsVisible = false;
            }
        }
    }

    private static (BoxView? Backdrop, Border? Card) ResolveChrome(View root) =>
        root is OverlaySheetFrame frame
            ? (frame.Backdrop, frame.Card)
            : (root.FindByName<BoxView>("SheetBackdrop"), root.FindByName<Border>("SheetCard"));
}
