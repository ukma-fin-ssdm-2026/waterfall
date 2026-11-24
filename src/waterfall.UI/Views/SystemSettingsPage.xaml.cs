// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using waterfall.UI.Services;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Views;

/// <summary>
/// Сторінка глобальної вкладки «Налаштування».
/// Дозволяє налаштовувати тему оформлення, фоновий парсинг та періодичність збору з автозбереженням.
/// </summary>
public partial class SystemSettingsPage : ContentPage, IHostedPage
{
    private readonly SystemSettingsViewModel _viewModel;
    private readonly ThemeService _themeService;
    private bool _isSyncingThemeUi;
    private bool _isAutoSaveArmed;

    public SystemSettingsPage(SystemSettingsViewModel viewModel, ThemeService themeService)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _themeService = themeService;
        _themeService.ThemeChanged += OnThemeChanged;
        SyncThemeUi();
    }

    public void OnHostActivated() => OnAppearing();

    public void OnHostDeactivated() => OnDisappearing();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SyncThemeUi();
        _isAutoSaveArmed = true;
    }

    protected override void OnDisappearing()
    {
        _isAutoSaveArmed = false;
        base.OnDisappearing();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(SyncThemeUi);
    }

    private void SyncThemeUi()
    {
        _isSyncingThemeUi = true;
        try
        {
            Color inkBg = GetThemeColor("InkPillBg", "#ECE7DF");
            Color inkText = GetThemeColor("InkPillText", "#191817");
            Color mutedText = GetThemeColor("TextSecondary", "#ABA49A");

            ApplySegmentStyle(ThemeSystemPill, ThemeSystemLabel, _themeService.Mode == AppThemeMode.System, inkBg, inkText, mutedText);
            ApplySegmentStyle(ThemeLightPill, ThemeLightLabel, _themeService.Mode == AppThemeMode.Light, inkBg, inkText, mutedText);
            ApplySegmentStyle(ThemeDarkPill, ThemeDarkLabel, _themeService.Mode == AppThemeMode.Dark, inkBg, inkText, mutedText);
            AmoledSwitch.IsToggled = _themeService.UseAmoledDark;
        }
        finally
        {
            _isSyncingThemeUi = false;
        }
    }

    private static Color GetThemeColor(string key, string fallbackHex)
    {
        if (Application.Current?.Resources.TryGetValue(key, out object? val) == true && val is Color c)
        {
            return c;
        }

        return Color.FromArgb(fallbackHex);
    }

    private static void ApplySegmentStyle(Border pill, Label label, bool isSelected, Color inkBg, Color inkText, Color mutedText)
    {
        if (isSelected)
        {
            pill.Background = new SolidColorBrush(inkBg);
            label.TextColor = inkText;
            label.FontFamily = "OpenSansSemibold";
        }
        else
        {
            pill.Background = Brush.Transparent;
            label.TextColor = mutedText;
            label.FontFamily = "OpenSansRegular";
        }
    }

    private void OnThemeSystemTapped(object? sender, TappedEventArgs e) => _themeService.SetMode(AppThemeMode.System);

    private void OnThemeLightTapped(object? sender, TappedEventArgs e) => _themeService.SetMode(AppThemeMode.Light);

    private void OnThemeDarkTapped(object? sender, TappedEventArgs e) => _themeService.SetMode(AppThemeMode.Dark);

    private void OnAmoledToggled(object? sender, ToggledEventArgs e)
    {
        if (_isSyncingThemeUi)
        {
            return;
        }

        _themeService.SetAmoledDark(e.Value);
    }

    private void OnBackgroundParsingToggled(object? sender, ToggledEventArgs e)
    {
        if (!_isAutoSaveArmed)
        {
            return;
        }

        _viewModel.IsBackgroundParsingEnabled = e.Value;
        _viewModel.SaveSettingsCommand.Execute(null);
    }

    private void OnIntervalSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!_isAutoSaveArmed)
        {
            return;
        }

        Dispatcher.Dispatch(() => _viewModel.SaveSettingsCommand.Execute(null));
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _isAutoSaveArmed = false;
        try
        {
            _viewModel.ResetToDefaultsCommand.Execute(null);
            _viewModel.SaveSettingsCommand.Execute(null);
        }
        finally
        {
            _isAutoSaveArmed = true;
        }
    }

    private void OnDismissStatusClicked(object? sender, EventArgs e)
    {
        _viewModel.StatusMessage = string.Empty;
    }
}
