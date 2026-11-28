using Microsoft.Maui.Controls.Shapes;

namespace waterfall.UI.Controls;

/// <summary>
/// Modern rounded segmented pill switcher rendered by <see cref="Views.PhoneSectionHostPage"/>
/// for Phone sub-pages (Вакансії and Кабінет).
/// </summary>
public sealed class MobileSegmentedBar : ContentView
{
    public static readonly BindableProperty GroupProperty =
        BindableProperty.Create(nameof(Group), typeof(string), typeof(MobileSegmentedBar), "Vacancies", propertyChanged: OnPropChanged);

    public static readonly BindableProperty ActiveRouteProperty =
        BindableProperty.Create(nameof(ActiveRoute), typeof(string), typeof(MobileSegmentedBar), "catalog", propertyChanged: OnPropChanged);

    public string Group
    {
        get => (string)GetValue(GroupProperty);
        set => SetValue(GroupProperty, value);
    }

    public string ActiveRoute
    {
        get => (string)GetValue(ActiveRouteProperty);
        set => SetValue(ActiveRouteProperty, value);
    }

    /// <summary>
    /// Handles pill taps directly inside a <see cref="Views.PhoneSectionHostPage"/>.
    /// </summary>
    public Func<string, Task>? OnTabSelected { get; set; }

    /// <summary>
    /// Overrides the Analyst-role flag passed by the hosting section.
    /// </summary>
    public bool? IsAnalystOverride { get; set; }

    private string? _builtGroup;
    private string? _builtActiveRoute;
    private bool? _builtIsAnalyst;

    public MobileSegmentedBar()
    {
        IsVisible = DeviceInfo.Idiom == DeviceIdiom.Phone;
        if (IsVisible)
        {
            RebuildIfNeeded();
            Loaded += (_, _) => RebuildIfNeeded();
        }
    }

    public void Refresh()
    {
        if (DeviceInfo.Idiom == DeviceIdiom.Phone)
        {
            RebuildIfNeeded();
        }
    }

    public void SetActiveRoute(string route)
    {
        if (string.Equals(ActiveRoute, route, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ActiveRoute = route;
        RebuildIfNeeded();
    }

    private static void OnPropChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is MobileSegmentedBar bar && bar.IsVisible)
        {
            bar.RebuildIfNeeded();
        }
    }

    private void RebuildIfNeeded()
    {
        bool isAnalyst = IsAnalystOverride == true;
        if (Content is not null &&
            string.Equals(_builtGroup, Group, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_builtActiveRoute, ActiveRoute, StringComparison.OrdinalIgnoreCase) &&
            _builtIsAnalyst == isAnalyst)
        {
            return;
        }

        _builtGroup = Group;
        _builtActiveRoute = ActiveRoute;
        _builtIsAnalyst = isAnalyst;
        Rebuild(isAnalyst);
    }

    private void Rebuild(bool isAnalyst)
    {
        _ = isAnalyst;
        (string Title, string Route)[] tabs = string.Equals(Group, "Cabinet", StringComparison.OrdinalIgnoreCase)
            ? [("Профіль", "login"), ("Налаштування", "systemsettings")]
            : [("Каталог", "catalog"), ("База вакансій", "database")];

        Grid grid = new()
        {
            ColumnSpacing = 3
        };

        for (int i = 0; i < tabs.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            (string title, string route) = tabs[i];
            bool isActive = string.Equals(route, ActiveRoute, StringComparison.OrdinalIgnoreCase);

            Label label = new()
            {
                Text = title,
                FontSize = 12,
                FontFamily = isActive ? "OpenSansSemibold" : "OpenSansRegular",
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            label.SetDynamicResource(Label.TextColorProperty, isActive ? "White" : "TextMuted");

            Border pill = new()
            {
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 0,
                Padding = new Thickness(8, 7),
                Content = label
            };
            if (isActive)
            {
                pill.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");
            }
            else
            {
                pill.BackgroundColor = Colors.Transparent;
            }

            if (!isActive)
            {
                string targetRoute = route;
                TapGestureRecognizer tap = new();
                tap.Tapped += async (_, _) =>
                {
                    if (OnTabSelected is not null)
                    {
                        await OnTabSelected(targetRoute);
                    }
                };
                pill.GestureRecognizers.Add(tap);
            }

            Grid.SetColumn(pill, i);
            grid.Children.Add(pill);
        }

        Border container = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 11 },
            StrokeThickness = 0,
            Padding = new Thickness(3),
            Margin = new Thickness(12, 2, 12, 8),
            Content = grid
        };
        container.SetDynamicResource(VisualElement.BackgroundColorProperty, "TonalSurface");

        Content = container;
    }
}
