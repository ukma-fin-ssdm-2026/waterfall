using System.Collections;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Resources;
using waterfall.UI.Services;

namespace waterfall.UI.Controls;

/// <summary>
/// Shared search, responsive sort (Phone menu button / Desktop inline <see cref="Picker"/>), and filter bar
/// used by <c>CatalogPage</c> and <c>DatabasePage</c>, with an optional Desktop right-hand actions slot.
/// </summary>
[ContentProperty(nameof(DesktopActions))]
public sealed class VacancySearchToolbar : ContentView
{
    public static readonly BindableProperty SearchTextProperty =
        BindableProperty.Create(nameof(SearchText), typeof(string), typeof(VacancySearchToolbar), string.Empty, BindingMode.TwoWay);

    public static readonly BindableProperty SearchPlaceholderProperty =
        BindableProperty.Create(nameof(SearchPlaceholder), typeof(string), typeof(VacancySearchToolbar), string.Empty);

    public static readonly BindableProperty SearchCommandProperty =
        BindableProperty.Create(nameof(SearchCommand), typeof(ICommand), typeof(VacancySearchToolbar));

    public static readonly BindableProperty SortOptionsProperty =
        BindableProperty.Create(
            nameof(SortOptions),
            typeof(IEnumerable),
            typeof(VacancySearchToolbar),
            propertyChanged: static (bindable, _, newValue) => ((VacancySearchToolbar)bindable).OnSortOptionsChanged((IEnumerable?)newValue));

    public static readonly BindableProperty SelectedSortOptionProperty =
        BindableProperty.Create(
            nameof(SelectedSortOption),
            typeof(object),
            typeof(VacancySearchToolbar),
            defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: static (bindable, _, newValue) => ((VacancySearchToolbar)bindable).OnSelectedSortOptionChanged(newValue));

    public static readonly BindableProperty IsCustomSortActiveProperty =
        BindableProperty.Create(
            nameof(IsCustomSortActive),
            typeof(bool),
            typeof(VacancySearchToolbar),
            false,
            propertyChanged: static (bindable, _, newValue) =>
            {
                VacancySearchToolbar toolbar = (VacancySearchToolbar)bindable;
                if (toolbar._sortButton is not null)
                {
                    ApplyActiveHighlight(toolbar._sortButton, (bool)newValue);
                }
            });

    public static readonly BindableProperty FilterCommandProperty =
        BindableProperty.Create(nameof(FilterCommand), typeof(ICommand), typeof(VacancySearchToolbar));

    public static readonly BindableProperty FilterDescriptionProperty =
        BindableProperty.Create(
            nameof(FilterDescription),
            typeof(string),
            typeof(VacancySearchToolbar),
            "Фільтри",
            propertyChanged: static (bindable, _, newValue) =>
                SemanticProperties.SetDescription(((VacancySearchToolbar)bindable)._filterButton, (string?)newValue ?? "Фільтри"));

    public static readonly BindableProperty IsFilterActiveProperty =
        BindableProperty.Create(
            nameof(IsFilterActive),
            typeof(bool),
            typeof(VacancySearchToolbar),
            false,
            propertyChanged: static (bindable, _, newValue) =>
                ApplyActiveHighlight(((VacancySearchToolbar)bindable)._filterButton, (bool)newValue));

    public static readonly BindableProperty DesktopActionsProperty =
        BindableProperty.Create(
            nameof(DesktopActions),
            typeof(View),
            typeof(VacancySearchToolbar),
            propertyChanged: static (bindable, _, newValue) =>
            {
                VacancySearchToolbar toolbar = (VacancySearchToolbar)bindable;
                if (toolbar._desktopActionsHost is not null)
                {
                    toolbar._desktopActionsHost.Content = (View?)newValue;
                }
            });

    private readonly Button? _sortButton;
    private readonly Picker? _sortPicker;
    private readonly Button _filterButton;
    private readonly ContentView? _desktopActionsHost;
    private bool _isSyncingSort;

    public event EventHandler? SortMenuClicked;

    public VacancySearchToolbar()
    {
        bool isPhone = DeviceInfo.Idiom == DeviceIdiom.Phone;

        SearchBar searchBar = new()
        {
            FontSize = 12.5,
            MinimumHeightRequest = 34,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent
        };
        searchBar.SetBinding(SearchBar.TextProperty, static (VacancySearchToolbar v) => v.SearchText, BindingMode.TwoWay, source: this);
        searchBar.SetBinding(SearchBar.PlaceholderProperty, static (VacancySearchToolbar v) => v.SearchPlaceholder, BindingMode.OneWay, source: this);
        searchBar.SetBinding(SearchBar.SearchCommandProperty, static (VacancySearchToolbar v) => v.SearchCommand, BindingMode.OneWay, source: this);

        Border searchBorder = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 1,
            HeightRequest = 38,
            Padding = new Thickness(4, 0),
            Content = searchBar
        };
        searchBorder.SetDynamicResource(Border.StrokeProperty, "Secondary");
        searchBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");

        View sortElement;
        if (isPhone)
        {
            _sortButton = CreateSquareIconButton(Icons.ArrowUpDown, "Сортування");
            _sortButton.Clicked += (_, _) => SortMenuClicked?.Invoke(this, EventArgs.Empty);
            sortElement = _sortButton;
        }
        else
        {
            Label sortIcon = new()
            {
                Text = Icons.ArrowUpDown,
                FontFamily = Icons.FontFamily,
                FontSize = 12.5,
                VerticalOptions = LayoutOptions.Center
            };
            sortIcon.SetDynamicResource(Label.TextColorProperty, "TextMuted");

            _sortPicker = new Picker
            {
                FontSize = 12,
                HeightRequest = 36,
                MinimumHeightRequest = 36,
                VerticalTextAlignment = TextAlignment.Center
            };
            MountedPageHelper.ApplyStyle(_sortPicker, "CompactPicker");
            _sortPicker.SelectedIndexChanged += OnPickerSelectedIndexChanged;

            Grid pickerGrid = new()
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
                ColumnSpacing = 6,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(sortIcon, 0);
            Grid.SetColumn(_sortPicker, 1);
            pickerGrid.Children.Add(sortIcon);
            pickerGrid.Children.Add(_sortPicker);

            Border pickerBorder = new()
            {
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                StrokeThickness = 1,
                HeightRequest = 38,
                WidthRequest = 215,
                Padding = new Thickness(10, 0),
                VerticalOptions = LayoutOptions.Center,
                Content = pickerGrid
            };
            pickerBorder.SetDynamicResource(Border.StrokeProperty, "Secondary");
            pickerBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");
            sortElement = pickerBorder;
        }

        _filterButton = CreateSquareIconButton(Icons.ListFilter, FilterDescription);
        _filterButton.SetBinding(Button.CommandProperty, static (VacancySearchToolbar v) => v.FilterCommand, BindingMode.OneWay, source: this);

        Grid root = new()
        {
            ColumnSpacing = 6,
            Padding = new Thickness(12, 4, 12, 6),
            ColumnDefinitions = isPhone
                ? [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)]
                : [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)]
        };

        Grid.SetColumn(searchBorder, 0);
        Grid.SetColumn(sortElement, 1);
        Grid.SetColumn(_filterButton, 2);
        root.Children.Add(searchBorder);
        root.Children.Add(sortElement);
        root.Children.Add(_filterButton);

        if (!isPhone)
        {
            _desktopActionsHost = new ContentView();
            Grid.SetColumn(_desktopActionsHost, 3);
            root.Children.Add(_desktopActionsHost);
        }

        Content = root;
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public string SearchPlaceholder
    {
        get => (string)GetValue(SearchPlaceholderProperty);
        set => SetValue(SearchPlaceholderProperty, value);
    }

    public ICommand? SearchCommand
    {
        get => (ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public IEnumerable? SortOptions
    {
        get => (IEnumerable?)GetValue(SortOptionsProperty);
        set => SetValue(SortOptionsProperty, value);
    }

    public object? SelectedSortOption
    {
        get => GetValue(SelectedSortOptionProperty);
        set => SetValue(SelectedSortOptionProperty, value);
    }

    public bool IsCustomSortActive
    {
        get => (bool)GetValue(IsCustomSortActiveProperty);
        set => SetValue(IsCustomSortActiveProperty, value);
    }

    public ICommand? FilterCommand
    {
        get => (ICommand?)GetValue(FilterCommandProperty);
        set => SetValue(FilterCommandProperty, value);
    }

    public string FilterDescription
    {
        get => (string)GetValue(FilterDescriptionProperty);
        set => SetValue(FilterDescriptionProperty, value);
    }

    public bool IsFilterActive
    {
        get => (bool)GetValue(IsFilterActiveProperty);
        set => SetValue(IsFilterActiveProperty, value);
    }

    public View? DesktopActions
    {
        get => (View?)GetValue(DesktopActionsProperty);
        set => SetValue(DesktopActionsProperty, value);
    }

    private static Button CreateSquareIconButton(string glyph, string description)
    {
        Button button = new()
        {
            Text = glyph,
            HeightRequest = 38,
            WidthRequest = 38,
            MinimumHeightRequest = 38,
            MinimumWidthRequest = 38,
            VerticalOptions = LayoutOptions.Center
        };
        MountedPageHelper.ApplyStyle(button, "IconButton");
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    private static void ApplyActiveHighlight(Button button, bool active)
    {
        if (active)
        {
            button.SetDynamicResource(VisualElement.BackgroundColorProperty, "InkPillBg");
            button.BorderColor = Colors.Transparent;
            button.SetDynamicResource(Button.TextColorProperty, "InkPillText");
        }
        else
        {
            button.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");
            button.SetDynamicResource(Button.BorderColorProperty, "Secondary");
            button.SetDynamicResource(Button.TextColorProperty, "TextSecondary");
        }
    }

    private void OnSortOptionsChanged(IEnumerable? options)
    {
        if (_sortPicker is null)
        {
            return;
        }

        _isSyncingSort = true;
        try
        {
            _sortPicker.ItemsSource = options as IList ?? options?.Cast<object?>().ToList();
            if (SelectedSortOption is not null)
            {
                _sortPicker.SelectedItem = SelectedSortOption;
            }
        }
        finally
        {
            _isSyncingSort = false;
        }
    }

    private void OnSelectedSortOptionChanged(object? selected)
    {
        if (_sortPicker is null)
        {
            return;
        }

        _isSyncingSort = true;
        try
        {
            _sortPicker.SelectedItem = selected;
        }
        finally
        {
            _isSyncingSort = false;
        }
    }

    private void OnPickerSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isSyncingSort || _sortPicker is null || _sortPicker.SelectedItem is null)
        {
            return;
        }

        if (!Equals(SelectedSortOption, _sortPicker.SelectedItem))
        {
            SelectedSortOption = _sortPicker.SelectedItem;
        }
    }
}
