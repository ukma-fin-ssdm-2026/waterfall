using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Controls;

/// <summary>
/// Спільна панель навігації по сторінках списку вакансій для «Каталогу» та «Бази вакансій» (FR-011 / US-001 AC-2).
/// Відображає кнопки «Перша / Назад / Сторінка X з Y / Далі / Остання» та вибір розміру сторінки (20 / 50 / 100).
/// </summary>
public sealed class VacancyPagerBar : ContentView
{
    public static readonly BindableProperty PagerProperty =
        BindableProperty.Create(
            nameof(Pager),
            typeof(VacancyPager),
            typeof(VacancyPagerBar),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((VacancyPagerBar)bindable).OnPagerChanged(oldValue as VacancyPager, newValue as VacancyPager));

    public static readonly BindableProperty SummaryTextProperty =
        BindableProperty.Create(
            nameof(SummaryText),
            typeof(string),
            typeof(VacancyPagerBar),
            propertyChanged: static (bindable, _, _) => ((VacancyPagerBar)bindable).SyncSummary());

    private readonly Button _firstBtn;
    private readonly Button _prevBtn;
    private readonly Button _nextBtn;
    private readonly Button _lastBtn;
    private readonly Label _pageLabel;
    private readonly Label _summaryLabel;
    private readonly FilterChipControl _size20Chip;
    private readonly FilterChipControl _size50Chip;
    private readonly FilterChipControl _size100Chip;

    public VacancyPagerBar()
    {
        IsVisible = false;

        _firstBtn = CreateNavButton("«", "Перша сторінка", static p => p?.FirstPageCommand.Execute(null));
        _prevBtn = CreateNavButton("‹", "Попередня сторінка", static p => p?.PrevPageCommand.Execute(null));
        _nextBtn = CreateNavButton("›", "Наступна сторінка", static p => p?.NextPageCommand.Execute(null));
        _lastBtn = CreateNavButton("»", "Остання сторінка", static p => p?.LastPageCommand.Execute(null));

        _pageLabel = new Label
        {
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            FontFamily = "OpenSansSemibold",
            FontSize = 12
        };
        _pageLabel.SetDynamicResource(Label.TextColorProperty, "White");

        _summaryLabel = new Label
        {
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            FontSize = 10.5,
            IsVisible = false
        };
        _summaryLabel.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        VerticalStackLayout centerStack = new()
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children = { _pageLabel, _summaryLabel }
        };

        HorizontalStackLayout prevGroup = new()
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { _firstBtn, _prevBtn }
        };

        HorizontalStackLayout nextGroup = new()
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { _nextBtn, _lastBtn }
        };

        Grid navRow = new()
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(prevGroup, 0);
        Grid.SetColumn(centerStack, 1);
        Grid.SetColumn(nextGroup, 2);
        navRow.Children.Add(prevGroup);
        navRow.Children.Add(centerStack);
        navRow.Children.Add(nextGroup);

        Label sizeTitle = new()
        {
            Text = "На сторінці:",
            FontSize = 11,
            VerticalOptions = LayoutOptions.Center
        };
        sizeTitle.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        _size20Chip = CreateSizeChip(20);
        _size50Chip = CreateSizeChip(50);
        _size100Chip = CreateSizeChip(100);

        HorizontalStackLayout sizeRow = new()
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { sizeTitle, _size20Chip, _size50Chip, _size100Chip }
        };

        VerticalStackLayout root = new()
        {
            Spacing = 6,
            Children = { navRow, sizeRow }
        };

        Border container = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 1,
            Margin = new Thickness(12, 4, 12, 8),
            Padding = new Thickness(8, 7),
            Content = root
        };
        container.SetDynamicResource(Border.StrokeProperty, "CardBorderBrush");
        container.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");

        Content = container;
    }

    public VacancyPager? Pager
    {
        get => (VacancyPager?)GetValue(PagerProperty);
        set => SetValue(PagerProperty, value);
    }

    public string? SummaryText
    {
        get => (string?)GetValue(SummaryTextProperty);
        set => SetValue(SummaryTextProperty, value);
    }

    private Button CreateNavButton(string text, string accessibleDescription, Action<VacancyPager?> action)
    {
        Button button = new()
        {
            Text = text,
            FontSize = 13,
            FontFamily = "OpenSansSemibold",
            CornerRadius = 8,
            BorderWidth = 1,
            Padding = new Thickness(9, 2),
            HeightRequest = 32,
            MinimumHeightRequest = 32,
            MinimumWidthRequest = 34
        };
        if (Application.Current?.Resources.TryGetValue("CardAction", out object? styleObj) == true && styleObj is Style cardAction)
        {
            button.Style = cardAction;
            button.FontSize = 13;
            button.Padding = new Thickness(9, 2);
            button.HeightRequest = 32;
            button.MinimumHeightRequest = 32;
            button.MinimumWidthRequest = 34;
        }
        else
        {
            button.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");
            button.SetDynamicResource(Button.TextColorProperty, "White");
            button.SetDynamicResource(Button.BorderColorProperty, "Secondary");
        }

        SemanticProperties.SetDescription(button, accessibleDescription);
        button.Clicked += (_, _) => action(Pager);
        return button;
    }

    private FilterChipControl CreateSizeChip(int size)
    {
        FilterChipControl chip = new()
        {
            Text = size.ToString(),
            HeightRequest = 26,
            MinimumHeightRequest = 26,
            Command = new Command(() => Pager?.SetPageSizeCommand.Execute(size))
        };
        SemanticProperties.SetDescription(chip, $"Показувати по {size} вакансій на сторінці");
        return chip;
    }

    private void OnPagerChanged(VacancyPager? oldPager, VacancyPager? newPager)
    {
        if (oldPager is not null)
        {
            oldPager.PropertyChanged -= OnPagerPropertyChanged;
        }

        if (newPager is not null)
        {
            newPager.PropertyChanged += OnPagerPropertyChanged;
        }

        SyncAll();
    }

    private void OnPagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(SyncAll);
            return;
        }

        SyncAll();
    }

    private void SyncAll()
    {
        VacancyPager? pager = Pager;
        if (pager is null)
        {
            IsVisible = false;
            return;
        }

        IsVisible = pager.IsPagerVisible;
        _firstBtn.IsEnabled = pager.HasPreviousPage;
        _prevBtn.IsEnabled = pager.HasPreviousPage;
        _nextBtn.IsEnabled = pager.HasNextPage;
        _lastBtn.IsEnabled = pager.HasNextPage;
        _pageLabel.Text = pager.PageText;

        _size20Chip.IsActive = pager.IsPageSize20;
        _size50Chip.IsActive = pager.IsPageSize50;
        _size100Chip.IsActive = pager.IsPageSize100;

        SyncSummary();
    }

    private void SyncSummary()
    {
        string? summary = SummaryText;
        bool hasSummary = !string.IsNullOrWhiteSpace(summary);
        _summaryLabel.IsVisible = hasSummary;
        _summaryLabel.Text = summary ?? string.Empty;
    }
}
