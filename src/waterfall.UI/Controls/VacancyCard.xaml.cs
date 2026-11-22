using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Resources;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Controls;

public partial class VacancyCard : ContentView
{
    private VacancyItemViewModel? _currentViewModel;
    private WebView? _mapWebView;

    public VacancyCard()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_currentViewModel != null)
        {
            _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _currentViewModel = BindingContext as VacancyItemViewModel;

        if (_currentViewModel != null)
        {
            _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateSkillChips();
            UpdateMapSource();
            UpdateDetailsPanel();
        }
        else
        {
            UpdateSkillChips();
            ReleaseMapWebView();
            DetailsHost.Content = null;
        }
    }

    private void UpdateSkillChips()
    {
        if (_currentViewModel is null)
        {
            SkillsRow.IsVisible = false;
            SkillChip0.IsVisible = false;
            SkillChip1.IsVisible = false;
            SkillChip2.IsVisible = false;
            return;
        }

        IReadOnlyList<string> skills = _currentViewModel.VisibleSkills;
        SkillsRow.IsVisible = skills.Count > 0 || _currentViewModel.HasMoreSkills;
        SetSkillChip(SkillChip0, SkillLabel0, skills, 0);
        SetSkillChip(SkillChip1, SkillLabel1, skills, 1);
        SetSkillChip(SkillChip2, SkillLabel2, skills, 2);
    }

    private static void SetSkillChip(Border chip, Label label, IReadOnlyList<string> skills, int index)
    {
        if (index < skills.Count)
        {
            label.Text = skills[index];
            chip.IsVisible = true;
        }
        else
        {
            chip.IsVisible = false;
            label.Text = string.Empty;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VacancyItemViewModel.MapHtml) ||
            e.PropertyName == nameof(VacancyItemViewModel.IsMapExpanded))
        {
            UpdateMapSource();
        }
        else if (e.PropertyName == nameof(VacancyItemViewModel.IsDetailsExpanded))
        {
            UpdateDetailsPanel();
        }
    }

    private void UpdateMapSource()
    {
        if (_currentViewModel != null &&
            _currentViewModel.IsMapExpanded &&
            !string.IsNullOrEmpty(_currentViewModel.MapHtml))
        {
            if (_mapWebView is null)
            {
                _mapWebView = new WebView
                {
                    HeightRequest = 190,
                    HorizontalOptions = LayoutOptions.Fill
                };

                CardMapHost.Content = BuildMapContainer(_currentViewModel, _mapWebView);
            }

            _mapWebView.Source = new HtmlWebViewSource { Html = _currentViewModel.MapHtml };
        }
        else
        {
            ReleaseMapWebView();
        }
    }

    private static Border BuildMapContainer(VacancyItemViewModel vm, WebView webView)
    {
        Grid headerGrid = new()
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 6
        };

        Label pin = new()
        {
            Text = Icons.MapPin,
            FontFamily = Icons.FontFamily,
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center
        };
        pin.SetDynamicResource(Label.TextColorProperty, "Primary");

        Label addr = new()
        {
            Text = vm.AddressDisplay,
            FontSize = 11,
            FontFamily = "OpenSansSemibold",
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center
        };
        addr.SetDynamicResource(Label.TextColorProperty, "White");

        Label coords = new()
        {
            Text = vm.MapCoordinatesDisplay,
            FontSize = 10,
            VerticalOptions = LayoutOptions.Center
        };
        coords.SetDynamicResource(Label.TextColorProperty, "TextMuted");

        Button closeBtn = new()
        {
            Text = Icons.X,
            Command = vm.ToggleMapCommand,
            FontFamily = Icons.FontFamily,
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = 0,
            WidthRequest = 24,
            HeightRequest = 24,
            MinimumWidthRequest = 24,
            MinimumHeightRequest = 24
        };
        closeBtn.SetDynamicResource(Button.TextColorProperty, "TextMuted");

        Grid.SetColumn(pin, 0);
        Grid.SetColumn(addr, 1);
        Grid.SetColumn(coords, 2);
        Grid.SetColumn(closeBtn, 3);
        headerGrid.Children.Add(pin);
        headerGrid.Children.Add(addr);
        headerGrid.Children.Add(coords);
        headerGrid.Children.Add(closeBtn);

        Border headerBar = new()
        {
            StrokeThickness = 0,
            Padding = new Thickness(10, 6),
            Content = headerGrid
        };
        headerBar.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkRail");

        Border card = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            StrokeThickness = 1,
            Margin = new Thickness(0, 2, 0, 4),
            Padding = 0,
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children = { headerBar, webView }
            }
        };
        card.SetDynamicResource(Border.StrokeProperty, "CardBorderBrush");
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");
        card.GestureRecognizers.Add(new TapGestureRecognizer());
        return card;
    }

    private void ReleaseMapWebView()
    {
        if (_mapWebView is not null)
        {
            CardMapHost.Content = null;
            _mapWebView = null;
        }
    }

    private void UpdateDetailsPanel()
    {
        if (_currentViewModel is null || !_currentViewModel.IsDetailsExpanded)
        {
            if (DetailsHost.Content is not null)
            {
                DetailsHost.Content = null;
            }

            return;
        }

        VerticalStackLayout stack = new() { Spacing = 6 };

        Label header = new()
        {
            Text = "Повні характеристики вакансії",
            FontFamily = "OpenSansSemibold",
            FontSize = 13
        };
        header.SetDynamicResource(Label.TextColorProperty, "White");
        stack.Children.Add(header);

        AddMutedLine(stack, $"Категорія: {_currentViewModel.CategoryDisplay}");

        if (_currentViewModel.HasSchedule)
        {
            AddMutedLine(stack, $"Графік: {_currentViewModel.Schedule}");
        }

        if (_currentViewModel.HasExperience)
        {
            AddMutedLine(stack, $"Досвід: {_currentViewModel.Experience}");
        }

        if (_currentViewModel.HasFullAddress)
        {
            AddMutedLine(stack, $"Адреса: {_currentViewModel.FullAddress}");
        }

        BoxView divider = new()
        {
            HeightRequest = 1,
            Margin = new Thickness(0, 2)
        };
        divider.SetDynamicResource(BoxView.ColorProperty, "DividerColor");
        stack.Children.Add(divider);

        Label descTitle = new()
        {
            Text = "Повний опис",
            FontFamily = "OpenSansSemibold",
            FontSize = 12
        };
        descTitle.SetDynamicResource(Label.TextColorProperty, "White");
        stack.Children.Add(descTitle);

        Label descBody = new()
        {
            Text = _currentViewModel.Description,
            FontSize = 12
        };
        descBody.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
        stack.Children.Add(descBody);

        Border panel = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 0,
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 2, 0, 0),
            Content = stack
        };
        panel.SetDynamicResource(VisualElement.BackgroundColorProperty, "TonalSurface");

        DetailsHost.Content = panel;
    }

    private static void AddMutedLine(VerticalStackLayout stack, string text)
    {
        Label label = new()
        {
            Text = text,
            FontSize = 12
        };
        label.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
        stack.Children.Add(label);
    }
}
