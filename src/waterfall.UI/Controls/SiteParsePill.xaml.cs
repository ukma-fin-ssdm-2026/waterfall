using System.Windows.Input;

namespace waterfall.UI.Controls;

/// <summary>
/// Compact Desktop parse-site pill combining site enable toggle and settings drawer trigger.
/// </summary>
public partial class SiteParsePill : ContentView
{
    public static readonly BindableProperty SiteNameProperty =
        BindableProperty.Create(nameof(SiteName), typeof(string), typeof(SiteParsePill), string.Empty);

    public static readonly BindableProperty IsSiteEnabledProperty =
        BindableProperty.Create(nameof(IsSiteEnabled), typeof(bool), typeof(SiteParsePill), false);

    public static readonly BindableProperty ToggleCommandProperty =
        BindableProperty.Create(nameof(ToggleCommand), typeof(ICommand), typeof(SiteParsePill));

    public event EventHandler? SettingsClicked;

    public SiteParsePill()
    {
        InitializeComponent();
    }

    public string SiteName
    {
        get => (string)GetValue(SiteNameProperty);
        set => SetValue(SiteNameProperty, value);
    }

    public bool IsSiteEnabled
    {
        get => (bool)GetValue(IsSiteEnabledProperty);
        set => SetValue(IsSiteEnabledProperty, value);
    }

    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    private void OnSettingsButtonClicked(object? sender, EventArgs e) => SettingsClicked?.Invoke(this, EventArgs.Empty);
}
