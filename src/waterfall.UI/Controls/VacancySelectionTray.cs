using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Controls;

/// <summary>
/// Спільна панель вибору 1–3 вакансій для групового ШІ-аналізу (FR-010) у вкладках «Каталог» та «База вакансій».
/// </summary>
public sealed class VacancySelectionTray : ContentView
{
    public static readonly BindableProperty CoordinatorProperty =
        BindableProperty.Create(
            nameof(Coordinator),
            typeof(VacancySelectionCoordinator),
            typeof(VacancySelectionTray),
            propertyChanged: static (bindable, oldValue, newValue) =>
                ((VacancySelectionTray)bindable).OnCoordinatorChanged(
                    oldValue as VacancySelectionCoordinator,
                    newValue as VacancySelectionCoordinator));

    private readonly Label _countLabel;
    private readonly Button _cancelButton;
    private readonly Button _analyzeButton;

    public VacancySelectionTray()
    {
        IsVisible = false;

        _countLabel = new Label
        {
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center
        };
        _countLabel.SetDynamicResource(Label.TextColorProperty, "White");

        _cancelButton = CreateActionButton(
            text: "Скасувати",
            styleKey: "CardAction",
            isPrimaryFallback: false,
            onClicked: () => Coordinator?.CancelSelectionModeCommand.Execute(null));

        _analyzeButton = CreateActionButton(
            text: "Порівняти з ШІ",
            styleKey: "CardActionPrimary",
            isPrimaryFallback: true,
            onClicked: () => Coordinator?.AnalyzeSelectionCommand.Execute(null));

        Grid row = new()
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 8,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(_countLabel, 0);
        Grid.SetColumn(_cancelButton, 1);
        Grid.SetColumn(_analyzeButton, 2);
        row.Children.Add(_countLabel);
        row.Children.Add(_cancelButton);
        row.Children.Add(_analyzeButton);

        Border container = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 1,
            Margin = new Thickness(12, 4, 12, 8),
            Padding = new Thickness(10, 6),
            Content = row
        };
        container.SetDynamicResource(Border.StrokeProperty, "CardBorderBrush");
        container.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");

        Content = container;
    }

    public VacancySelectionCoordinator? Coordinator
    {
        get => (VacancySelectionCoordinator?)GetValue(CoordinatorProperty);
        set => SetValue(CoordinatorProperty, value);
    }

    private static Button CreateActionButton(
        string text,
        string styleKey,
        bool isPrimaryFallback,
        Action onClicked)
    {
        Button button = new()
        {
            Text = text,
            FontSize = 11.5,
            Padding = new Thickness(10, 4),
            HeightRequest = 32,
            MinimumHeightRequest = 32
        };

        if (Application.Current?.Resources.TryGetValue(styleKey, out object? styleObj) == true && styleObj is Style style)
        {
            button.Style = style;
            button.FontSize = 11.5;
            button.Padding = new Thickness(10, 4);
            button.HeightRequest = 32;
            button.MinimumHeightRequest = 32;
        }
        else if (isPrimaryFallback)
        {
            button.FontFamily = "OpenSansSemibold";
            button.CornerRadius = 9;
            button.BorderWidth = 0;
            button.SetDynamicResource(VisualElement.BackgroundColorProperty, "Primary");
            button.SetDynamicResource(Button.TextColorProperty, "OnPrimary");
        }
        else
        {
            button.FontFamily = "OpenSansSemibold";
            button.CornerRadius = 9;
            button.BorderWidth = 1;
            button.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");
            button.SetDynamicResource(Button.TextColorProperty, "White");
            button.SetDynamicResource(Button.BorderColorProperty, "Secondary");
        }

        button.Clicked += (_, _) => onClicked();
        return button;
    }

    private void OnCoordinatorChanged(VacancySelectionCoordinator? oldCoordinator, VacancySelectionCoordinator? newCoordinator)
    {
        if (oldCoordinator is not null)
        {
            oldCoordinator.PropertyChanged -= OnCoordinatorPropertyChanged;
        }

        if (newCoordinator is not null)
        {
            newCoordinator.PropertyChanged += OnCoordinatorPropertyChanged;
        }

        SyncAll();
    }

    private void OnCoordinatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
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
        VacancySelectionCoordinator? coordinator = Coordinator;
        if (coordinator is null)
        {
            IsVisible = false;
            _countLabel.Text = string.Empty;
            _analyzeButton.IsEnabled = false;
            return;
        }

        IsVisible = coordinator.IsSelectionMode;
        _countLabel.Text = coordinator.SelectionTrayText;
        _analyzeButton.IsEnabled = coordinator.HasSelection;
    }
}
