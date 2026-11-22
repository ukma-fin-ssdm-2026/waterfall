using Microsoft.Maui.Controls;

namespace waterfall.UI.Services;

public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

/// <summary>
/// Manages Warm Editorial themes (Warm Dark, Extra-Muted AMOLED Black, Warm Paper Light)
/// with Follow-System support and an independent AMOLED Dark toggle persisted in Preferences.
/// </summary>
public sealed class ThemeService
{
    private const string ThemeModeKey = "waterfall_theme_mode";
    private const string AmoledDarkKey = "waterfall_amoled_dark";

    public event EventHandler? ThemeChanged;
    internal static event EventHandler? PaletteChanged;

    public AppThemeMode Mode { get; private set; } = AppThemeMode.System;

    public bool UseAmoledDark { get; private set; }

    public bool IsEffectiveDark =>
        Mode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => Application.Current?.RequestedTheme != AppTheme.Light
        };

    public void Initialize()
    {
        try
        {
            int savedMode = Preferences.Default.Get(ThemeModeKey, (int)AppThemeMode.System);
            Mode = Enum.IsDefined(typeof(AppThemeMode), savedMode) ? (AppThemeMode)savedMode : AppThemeMode.System;
            UseAmoledDark = Preferences.Default.Get(AmoledDarkKey, false);
        }
        catch
        {
            Mode = AppThemeMode.System;
            UseAmoledDark = false;
        }

        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += OnRequestedThemeChanged;
        }

        ApplyTheme();
    }

    public void SetMode(AppThemeMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        try
        {
            Preferences.Default.Set(ThemeModeKey, (int)mode);
        }
        catch
        {
        }

        ApplyTheme();
    }

    public void SetAmoledDark(bool enabled)
    {
        if (UseAmoledDark == enabled)
        {
            return;
        }

        UseAmoledDark = enabled;
        try
        {
            Preferences.Default.Set(AmoledDarkKey, enabled);
        }
        catch
        {
        }

        ApplyTheme();
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        if (Mode == AppThemeMode.System)
        {
            ApplyTheme();
        }
    }

    public void ApplyTheme()
    {
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.UserAppTheme = Mode switch
        {
            AppThemeMode.Light => AppTheme.Light,
            AppThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };

        bool isDark = IsEffectiveDark;
        bool isAmoled = isDark && UseAmoledDark;

        ResourceDictionary res = app.Resources;

        if (!isDark)
        {
            // Warm Paper Light
            SetPalette(res,
                bgCanvas: "#F7F5F0",
                bgRail: "#EFECE5",
                bgSurface: "#FFFFFF",
                surfaceElevated: "#F0EDE6",
                tonalSurface: "#EDE9E1",
                border: "#DED8CE",
                cardBorder: "#E7E2D8",
                divider: "#E5E0D6",
                textPrimary: "#1D1B18",
                textSecondary: "#5C574F",
                textMuted: "#8A8378",
                primary: "#BA5C47",
                primaryHover: "#A64E3A",
                primarySurface: "#F5E4DF",
                onPrimary: "#FFFFFF",
                inkPillBg: "#1F1D1A",
                inkPillText: "#FFFFFF",
                success: "#2F7D63",
                successSurface: "#E3F2EC",
                danger: "#C94444",
                dangerSurface: "#FAE8E8");
        }
        else if (isAmoled)
        {
            // Extra-Muted AMOLED Black (reduced contrast bloom on OLED #000000)
            SetPalette(res,
                bgCanvas: "#000000",
                bgRail: "#000000",
                bgSurface: "#0B0A09",
                surfaceElevated: "#151311",
                tonalSurface: "#181614",
                border: "#23201D",
                cardBorder: "#1B1917",
                divider: "#1C1A18",
                textPrimary: "#E5E2DC",
                textSecondary: "#979087",
                textMuted: "#655F58",
                primary: "#C26D5A",
                primaryHover: "#CE7A67",
                primarySurface: "#261916",
                onPrimary: "#FFFFFF",
                inkPillBg: "#DCD7CE",
                inkPillText: "#0A0908",
                success: "#5FA38A",
                successSurface: "#13221D",
                danger: "#D66565",
                dangerSurface: "#261414");
        }
        else
        {
            // Warm Charcoal Dark (Warm Editorial)
            SetPalette(res,
                bgCanvas: "#191817",
                bgRail: "#141312",
                bgSurface: "#22201E",
                surfaceElevated: "#2C2926",
                tonalSurface: "#2C2926",
                border: "#34302C",
                cardBorder: "#2D2A26",
                divider: "#2E2B27",
                textPrimary: "#F2EFEA",
                textSecondary: "#ABA49A",
                textMuted: "#767068",
                primary: "#D4745E",
                primaryHover: "#DF836E",
                primarySurface: "#32221E",
                onPrimary: "#FFFFFF",
                inkPillBg: "#ECE7DF",
                inkPillText: "#191817",
                success: "#6FB59B",
                successSurface: "#1B2E27",
                danger: "#E06C6C",
                dangerSurface: "#331D1D");
        }

        ApplyPlatformChrome(isDark, isAmoled);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        PaletteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies native window/titlebar/status-bar chrome without rewriting DynamicResource keys.
    /// </summary>
    public void ApplyWindowChrome()
    {
        bool isDark = IsEffectiveDark;
        ApplyPlatformChrome(isDark, isDark && UseAmoledDark);
    }

    private static void SetPalette(
        ResourceDictionary res,
        string bgCanvas,
        string bgRail,
        string bgSurface,
        string surfaceElevated,
        string tonalSurface,
        string border,
        string cardBorder,
        string divider,
        string textPrimary,
        string textSecondary,
        string textMuted,
        string primary,
        string primaryHover,
        string primarySurface,
        string onPrimary,
        string inkPillBg,
        string inkPillText,
        string success,
        string successSurface,
        string danger,
        string dangerSurface)
    {
        Color cBgCanvas = Color.FromArgb(bgCanvas);
        Color cBgRail = Color.FromArgb(bgRail);
        Color cBgSurface = Color.FromArgb(bgSurface);
        Color cSurfaceElevated = Color.FromArgb(surfaceElevated);
        Color cTonal = Color.FromArgb(tonalSurface);
        Color cBorder = Color.FromArgb(border);
        Color cCardBorder = Color.FromArgb(cardBorder);
        Color cDivider = Color.FromArgb(divider);
        Color cTextPrimary = Color.FromArgb(textPrimary);
        Color cTextSecondary = Color.FromArgb(textSecondary);
        Color cTextMuted = Color.FromArgb(textMuted);
        Color cPrimary = Color.FromArgb(primary);
        Color cPrimaryHover = Color.FromArgb(primaryHover);
        Color cPrimarySurface = Color.FromArgb(primarySurface);
        Color cOnPrimary = Color.FromArgb(onPrimary);
        Color cInkBg = Color.FromArgb(inkPillBg);
        Color cInkText = Color.FromArgb(inkPillText);
        Color cSuccess = Color.FromArgb(success);
        Color cSuccessSurface = Color.FromArgb(successSurface);
        Color cDanger = Color.FromArgb(danger);
        Color cDangerSurface = Color.FromArgb(dangerSurface);

        // Semantic tokens + legacy key aliases so ALL 8 pages theme automatically
        res["DarkBg2"] = cBgCanvas;
        res["DarkBg1"] = cBgSurface;
        res["DarkRail"] = cBgRail;
        res["SurfaceElevated"] = cSurfaceElevated;
        res["TonalSurface"] = cTonal;
        res["Secondary"] = cBorder;
        res["CardBorder"] = cCardBorder;
        res["DividerColor"] = cDivider;

        res["White"] = cTextPrimary;
        res["Text_Light"] = cTextPrimary;
        res["Text_Dark"] = cTextPrimary;
        res["TextSecondary"] = cTextSecondary;
        res["TextMuted"] = cTextMuted;
        res["Gray400"] = cTextMuted;
        res["Gray500"] = cTextSecondary;

        // Single unified brand identity (Accent aliases Primary so no competing amber!)
        res["Primary"] = cPrimary;
        res["PrimaryDark"] = cPrimary;
        res["PrimaryHover"] = cPrimaryHover;
        res["PrimarySurface"] = cPrimarySurface;
        res["OnPrimary"] = cOnPrimary;
        res["Accent"] = cPrimary;
        res["AccentSurface"] = cPrimarySurface;

        // 90 / 8 / 2 high-contrast ink active pills
        res["InkPillBg"] = cInkBg;
        res["InkPillText"] = cInkText;

        res["Success"] = cSuccess;
        res["SuccessSurface"] = cSuccessSurface;
        res["Danger"] = cDanger;
        res["DangerSurface"] = cDangerSurface;
        res["Black"] = cOnPrimary;

        // Brush tokens for Shape/Path/Border.Stroke controls (Brush properties require Brush, not Color)
        res["PrimaryBrush"] = new SolidColorBrush(cPrimary);
        res["TextMutedBrush"] = new SolidColorBrush(cTextMuted);
        res["CardBorderBrush"] = new SolidColorBrush(cCardBorder);
    }

    private static void ApplyPlatformChrome(bool isDark, bool isAmoled)
    {
#if ANDROID
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Android.App.Activity? activity = Platform.CurrentActivity;
            Android.Views.Window? window = activity?.Window;
            if (window is null)
            {
                return;
            }

            string barHex = !isDark ? "#F7F5F0" : (isAmoled ? "#000000" : "#191817");
            string navHex = !isDark ? "#EFECE5" : (isAmoled ? "#000000" : "#141312");
            Android.Graphics.Color statusColor = Android.Graphics.Color.ParseColor(barHex);
            Android.Graphics.Color navColor = Android.Graphics.Color.ParseColor(navHex);
#pragma warning disable CA1422
            window.SetStatusBarColor(statusColor);
            window.SetNavigationBarColor(navColor);
#pragma warning restore CA1422

            AndroidX.Core.View.WindowInsetsControllerCompat? controller =
                AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
            if (controller is not null)
            {
                controller.AppearanceLightStatusBars = !isDark;
                controller.AppearanceLightNavigationBars = !isDark;
            }
        });
#elif WINDOWS
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Microsoft.UI.Xaml.ResourceDictionary? winRes = Microsoft.UI.Xaml.Application.Current?.Resources;
            if (winRes is not null)
            {
                Windows.UI.Color indicator = !isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xBA, 0x5C, 0x47)
                    : (isAmoled
                        ? Windows.UI.Color.FromArgb(0xFF, 0xC2, 0x6D, 0x5A)
                        : Windows.UI.Color.FromArgb(0xFF, 0xD4, 0x74, 0x5E));

                // Warm subtle pill tint: dark warm ink on Light rail, warm cream on Dark/AMOLED rail
                byte r = isDark ? (byte)0xFF : (byte)0x1E;
                byte g = isDark ? (byte)0xF8 : (byte)0x19;
                byte b = isDark ? (byte)0xF0 : (byte)0x14;

                SetWinBrush(winRes, "NavigationViewSelectionIndicatorForeground", indicator);
                SetWinBrush(winRes, "NavigationViewItemBackgroundSelected", Windows.UI.Color.FromArgb(0x14, r, g, b));
                SetWinBrush(winRes, "NavigationViewItemBackgroundSelectedPointerOver", Windows.UI.Color.FromArgb(0x1C, r, g, b));
                SetWinBrush(winRes, "NavigationViewItemBackgroundSelectedPressed", Windows.UI.Color.FromArgb(0x10, r, g, b));
                SetWinBrush(winRes, "NavigationViewItemBackgroundPointerOver", Windows.UI.Color.FromArgb(0x0E, r, g, b));
                SetWinBrush(winRes, "NavigationViewItemBackgroundPressed", Windows.UI.Color.FromArgb(0x0A, r, g, b));

                Windows.UI.Color captionBg = !isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0xF7, 0xF5, 0xF0)
                    : (isAmoled
                        ? Windows.UI.Color.FromArgb(0xFF, 0x00, 0x00, 0x00)
                        : Windows.UI.Color.FromArgb(0xFF, 0x19, 0x18, 0x17));
                Windows.UI.Color captionFg = !isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x1D, 0x1B, 0x18)
                    : (isAmoled
                        ? Windows.UI.Color.FromArgb(0xFF, 0xE5, 0xE2, 0xDC)
                        : Windows.UI.Color.FromArgb(0xFF, 0xF2, 0xEF, 0xEA));
                Windows.UI.Color captionMuted = !isDark
                    ? Windows.UI.Color.FromArgb(0xFF, 0x8A, 0x83, 0x78)
                    : Windows.UI.Color.FromArgb(0xFF, 0x8B, 0x84, 0x7B);

                SetWinBrush(winRes, "WindowCaptionBackground", captionBg);
                SetWinBrush(winRes, "WindowCaptionBackgroundDisabled", captionBg);
                SetWinBrush(winRes, "WindowCaptionForeground", captionFg);
                SetWinBrush(winRes, "WindowCaptionForegroundDisabled", captionMuted);

                // Apply neutral titlebar & native WinUI ElementTheme to all open windows
                if (Application.Current?.Windows is { Count: > 0 } windows)
                {
                    foreach (Window mauiWindow in windows)
                    {
                        if (mauiWindow.TitleBar is TitleBar mauiTitleBar)
                        {
                            mauiTitleBar.BackgroundColor = Color.FromRgb(captionBg.R, captionBg.G, captionBg.B);
                            mauiTitleBar.ForegroundColor = Color.FromRgb(captionFg.R, captionFg.G, captionFg.B);
                        }

                        if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window winWindow)
                        {
                            winWindow.Title = "Waterfall";
                            if (winWindow.AppWindow is not null)
                            {
                                winWindow.AppWindow.Title = "Waterfall";
                            }

                            if (winWindow.Content is Microsoft.UI.Xaml.FrameworkElement rootElement)
                            {
                                rootElement.RequestedTheme = isDark
                                    ? Microsoft.UI.Xaml.ElementTheme.Dark
                                    : Microsoft.UI.Xaml.ElementTheme.Light;
                            }

                            try
                            {
                                string iconPath = Path.Combine(AppContext.BaseDirectory, "appicon_win.ico");
                                if (File.Exists(iconPath))
                                {
                                    winWindow.AppWindow?.SetIcon(iconPath);
                                }

                                Microsoft.UI.Windowing.AppWindowTitleBar? titleBar = winWindow.AppWindow?.TitleBar;
                                if (titleBar is not null)
                                {
                                    titleBar.BackgroundColor = captionBg;
                                    titleBar.InactiveBackgroundColor = captionBg;
                                    titleBar.ButtonBackgroundColor = captionBg;
                                    titleBar.ButtonInactiveBackgroundColor = captionBg;
                                    titleBar.ForegroundColor = captionFg;
                                    titleBar.InactiveForegroundColor = captionMuted;
                                    titleBar.ButtonForegroundColor = captionFg;
                                    titleBar.ButtonInactiveForegroundColor = captionMuted;
                                    titleBar.ButtonHoverForegroundColor = captionFg;
                                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0x16, r, g, b);
                                    titleBar.ButtonPressedForegroundColor = captionFg;
                                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0x24, r, g, b);
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
        });
#endif
    }

#if WINDOWS
    private static void SetWinBrush(Microsoft.UI.Xaml.ResourceDictionary res, string key, Windows.UI.Color color)
    {
        if (res.TryGetValue(key, out object? existing) && existing is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            res[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
        }
    }
#endif
}
