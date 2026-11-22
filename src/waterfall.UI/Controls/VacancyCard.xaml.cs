using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Resources;
using waterfall.UI.Services;
using Waterfall.Client.ViewModels;

namespace waterfall.UI.Controls;

public partial class VacancyCard : ContentView
{
    private readonly record struct CardPalette(
        Color BgSurface,
        Color BgRail,
        Color CardBorder,
        Color TonalSurface,
        Color Divider,
        Color TextPrimary,
        Color TextSecondary,
        Color TextMuted,
        Color Primary,
        Color PrimarySurface,
        Color Danger,
        Color DangerSurface)
    {
        private static readonly CardPalette Default = new(
            BgSurface: Color.FromArgb("#22201E"),
            BgRail: Color.FromArgb("#141312"),
            CardBorder: Color.FromArgb("#2D2A26"),
            TonalSurface: Color.FromArgb("#2C2926"),
            Divider: Color.FromArgb("#2E2B27"),
            TextPrimary: Color.FromArgb("#F2EFEA"),
            TextSecondary: Color.FromArgb("#ABA49A"),
            TextMuted: Color.FromArgb("#767068"),
            Primary: Color.FromArgb("#D4745E"),
            PrimarySurface: Color.FromArgb("#32221E"),
            Danger: Color.FromArgb("#E06C6C"),
            DangerSurface: Color.FromArgb("#331D1D"));

        /// <summary>
        /// Чи збігається завантажена палітра з дефолтною темною, уже зашитою у VacancyCard.xaml.
        /// </summary>
        public bool IsDefault =>
            IsSameColor(BgSurface, Default.BgSurface)
            && IsSameColor(BgRail, Default.BgRail)
            && IsSameColor(CardBorder, Default.CardBorder)
            && IsSameColor(TonalSurface, Default.TonalSurface)
            && IsSameColor(Divider, Default.Divider)
            && IsSameColor(TextPrimary, Default.TextPrimary)
            && IsSameColor(TextSecondary, Default.TextSecondary)
            && IsSameColor(TextMuted, Default.TextMuted)
            && IsSameColor(Primary, Default.Primary)
            && IsSameColor(PrimarySurface, Default.PrimarySurface)
            && IsSameColor(Danger, Default.Danger)
            && IsSameColor(DangerSurface, Default.DangerSurface);

        private static bool IsSameColor(Color left, Color right) =>
            left.Red == right.Red
            && left.Green == right.Green
            && left.Blue == right.Blue
            && left.Alpha == right.Alpha;

        public static CardPalette Load()
        {
            ResourceDictionary? res = Application.Current?.Resources;
            return new CardPalette(
                BgSurface: GetColor(res, "DarkBg1", "#22201E"),
                BgRail: GetColor(res, "DarkRail", "#141312"),
                CardBorder: GetColor(res, "CardBorder", "#2D2A26"),
                TonalSurface: GetColor(res, "TonalSurface", "#2C2926"),
                Divider: GetColor(res, "DividerColor", "#2E2B27"),
                TextPrimary: GetColor(res, "White", "#F2EFEA"),
                TextSecondary: GetColor(res, "TextSecondary", "#ABA49A"),
                TextMuted: GetColor(res, "TextMuted", "#767068"),
                Primary: GetColor(res, "Primary", "#D4745E"),
                PrimarySurface: GetColor(res, "PrimarySurface", "#32221E"),
                Danger: GetColor(res, "Danger", "#E06C6C"),
                DangerSurface: GetColor(res, "DangerSurface", "#331D1D"));
        }

        private static Color GetColor(ResourceDictionary? res, string key, string fallbackHex) =>
            res is not null && res.TryGetValue(key, out object? value) && value is Color color
                ? color
                : Color.FromArgb(fallbackHex);
    }

    private const int LiveCardsPruneThreshold = 96;
    private static readonly List<WeakReference<VacancyCard>> LiveCards = [];
    private static CardPalette s_palette = CardPalette.Load();
    private static bool s_paletteIsDefault;
    private static SolidColorBrush s_heartPrimaryBrush = new(Color.FromArgb("#D4745E"));
    private static SolidColorBrush s_heartMutedBrush = new(Color.FromArgb("#767068"));
    private static SolidColorBrush s_cardBorderBrush = new(Color.FromArgb("#2D2A26"));

    static VacancyCard()
    {
        s_paletteIsDefault = s_palette.IsDefault;
        if (!s_paletteIsDefault)
        {
            CreatePaletteBrushes();
        }

        ThemeService.PaletteChanged += OnGlobalPaletteChanged;
    }

    private readonly TapGestureRecognizer _sourceTapRecognizer;
    private VacancyItemViewModel? _currentViewModel;
    private WebView? _mapWebView;

    public VacancyCard()
    {
        InitializeComponent();

        _sourceTapRecognizer = new TapGestureRecognizer();
        _sourceTapRecognizer.Tapped += OnOpenSourceTapped;

        RegisterLiveCard(this);
        if (!s_paletteIsDefault)
        {
            ApplyThemeColors();
        }

        BindingContextChanged += OnBindingContextChanged;
    }

    private static void CreatePaletteBrushes()
    {
        s_heartPrimaryBrush = new SolidColorBrush(s_palette.Primary);
        s_heartMutedBrush = new SolidColorBrush(s_palette.TextMuted);
        s_cardBorderBrush = new SolidColorBrush(s_palette.CardBorder);
    }

    private static void RegisterLiveCard(VacancyCard card)
    {
        if (LiveCards.Count > LiveCardsPruneThreshold)
        {
            PruneDeadCards();
        }

        LiveCards.Add(new WeakReference<VacancyCard>(card));
    }

    private static void PruneDeadCards()
    {
        for (int i = LiveCards.Count - 1; i >= 0; i--)
        {
            if (!LiveCards[i].TryGetTarget(out _))
            {
                LiveCards.RemoveAt(i);
            }
        }
    }

    private static void OnGlobalPaletteChanged(object? sender, EventArgs e)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(RefreshAllLiveCards);
            return;
        }

        RefreshAllLiveCards();
    }

    private static void RefreshAllLiveCards()
    {
        s_palette = CardPalette.Load();
        s_paletteIsDefault = s_palette.IsDefault;
        CreatePaletteBrushes();
        for (int i = LiveCards.Count - 1; i >= 0; i--)
        {
            if (LiveCards[i].TryGetTarget(out VacancyCard? card))
            {
                card.ApplyThemeColors();
            }
            else
            {
                LiveCards.RemoveAt(i);
            }
        }
    }

    private void ApplyThemeColors()
    {
        CardPalette p = s_palette;
        CardBorder.BackgroundColor = p.BgSurface;
        CardBorder.Stroke = s_cardBorderBrush;

        TitleLabel.TextColor = p.TextPrimary;
        CompanyLabel.TextColor = p.TextSecondary;
        ArchivedBadge.BackgroundColor = p.TonalSurface;
        ArchivedLabel.TextColor = p.TextMuted;
        AddressPinLabel.TextColor = p.TextMuted;
        AddressLabel.TextColor = p.TextSecondary;
        MapToggleLabel.TextColor = p.Primary;
        DescriptionLabel.TextColor = p.TextSecondary;

        SkillChip0.BackgroundColor = p.TonalSurface;
        SkillLabel0.TextColor = p.TextSecondary;
        SkillChip1.BackgroundColor = p.TonalSurface;
        SkillLabel1.TextColor = p.TextSecondary;
        SkillChip2.BackgroundColor = p.TonalSurface;
        SkillLabel2.TextColor = p.TextSecondary;
        MoreSkillsChip.BackgroundColor = p.TonalSurface;
        MoreSkillsLabel.TextColor = p.TextMuted;

        DividerBox.Color = p.Divider;
        SourceLabel.TextColor = p.TextMuted;
        SourceExternalIcon.TextColor = p.TextMuted;
        AiButtonBorder.BackgroundColor = p.TonalSurface;
        AiSparklesLabel.TextColor = p.Primary;
        AiTextLabel.TextColor = p.TextSecondary;

        if (_currentViewModel is not null)
        {
            ApplySalaryState(_currentViewModel);
            ApplySavedState(_currentViewModel);
            UpdateMetaRow(_currentViewModel);
            UpdateDetailsToggle(_currentViewModel);
            if (_currentViewModel.IsMapExpanded)
            {
                UpdateMapSection();
            }

            if (_currentViewModel.IsDetailsExpanded)
            {
                UpdateDetailsPanel();
            }
        }
        else
        {
            SalaryLabel.TextColor = p.TextPrimary;
            MetaLabel.TextColor = p.TextMuted;
            HeartPath.Fill = Brush.Transparent;
            HeartPath.Stroke = s_heartMutedBrush;
            DetailsChevronLabel.TextColor = p.TextMuted;
        }
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        VacancyItemViewModel? next = BindingContext as VacancyItemViewModel;
        if (ReferenceEquals(_currentViewModel, next))
        {
            return;
        }

        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _currentViewModel = next;

        if (_currentViewModel is not null)
        {
            _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
            BindToViewModel(_currentViewModel);
            _ = _currentViewModel.EnsureDistanceLoadedAsync();
        }
        else
        {
            ResetUnboundState();
        }
    }

    private void BindToViewModel(VacancyItemViewModel vm)
    {
        CardBorder.Opacity = vm.IsActive ? 1.0 : 0.74;
        if (HeartPath.Scale != 1.0)
        {
            HeartPath.CancelAnimations();
            HeartPath.Scale = 1.0;
        }

        TitleLabel.Text = vm.Title;
        SalaryLabel.Text = vm.SalaryDisplay;
        ApplySalaryState(vm);
        ApplySavedState(vm);

        CompanyLabel.Text = vm.Company;
        UpdateMetaRow(vm);

        ArchivedBadge.IsVisible = !vm.IsActive;
        if (!vm.IsActive)
        {
            ArchivedLabel.Text = vm.StatusText;
        }

        AddressRow.IsVisible = vm.HasAddress;
        if (vm.HasAddress)
        {
            AddressLabel.Text = vm.AddressDisplay;
            MapToggleLabel.IsVisible = vm.CanShowOnMap;
            MapToggleLabel.Text = vm.MapToggleText;
        }
        else
        {
            MapToggleLabel.IsVisible = false;
        }

        DescriptionLabel.Text = vm.Description;
        UpdateSkillChips(vm);
        UpdateSourceRow(vm);
        UpdateDetailsToggle(vm);
        UpdateMapSection();
        UpdateDetailsPanel();
    }

    private void ResetUnboundState()
    {
        CardBorder.Opacity = 1.0;
        TitleLabel.Text = string.Empty;
        SalaryLabel.Text = string.Empty;
        CompanyLabel.Text = string.Empty;
        MetaLabel.FormattedText = null;
        MetaLabel.Text = string.Empty;
        ArchivedBadge.IsVisible = false;
        AddressRow.IsVisible = false;
        MapToggleLabel.IsVisible = false;
        DescriptionLabel.Text = string.Empty;
        SkillsRow.IsVisible = false;
        SkillChip0.IsVisible = false;
        SkillChip1.IsVisible = false;
        SkillChip2.IsVisible = false;
        MoreSkillsChip.IsVisible = false;
        SourceLabel.Text = string.Empty;
        SourceExternalIcon.IsVisible = false;
        SourceRow.GestureRecognizers.Remove(_sourceTapRecognizer);
        HeartPath.CancelAnimations();
        HeartPath.Scale = 1.0;
        HeartPath.Fill = Brush.Transparent;
        HeartPath.Stroke = s_heartMutedBrush;
        ReleaseMapHost();
        DetailsHost.Content = null;
        DetailsHost.IsVisible = false;
    }

    private void ApplySalaryState(VacancyItemViewModel vm)
    {
        CardPalette p = s_palette;
        if (vm.HasSalary)
        {
            SalaryLabel.TextColor = p.TextPrimary;
            SalaryLabel.FontFamily = "OpenSansSemibold";
            SalaryLabel.FontSize = 14.5;
        }
        else
        {
            SalaryLabel.TextColor = p.TextMuted;
            SalaryLabel.FontFamily = "OpenSansRegular";
            SalaryLabel.FontSize = 12;
        }
    }

    private void ApplySavedState(VacancyItemViewModel vm)
    {
        if (vm.IsSaved)
        {
            HeartPath.Fill = s_heartPrimaryBrush;
            HeartPath.Stroke = s_heartPrimaryBrush;
        }
        else
        {
            HeartPath.Fill = Brush.Transparent;
            HeartPath.Stroke = s_heartMutedBrush;
        }

        SemanticProperties.SetDescription(HeartBorder, vm.SaveButtonText);
    }

    private void UpdateMetaRow(VacancyItemViewModel vm)
    {
        CardPalette p = s_palette;
        if (vm.HasDistanceText && !string.IsNullOrWhiteSpace(vm.DistanceText))
        {
            MetaLabel.Text = null;
            MetaLabel.FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = $"{vm.CategoryDisplay} · ", TextColor = p.TextMuted },
                    new Span { Text = vm.DistanceText, TextColor = p.TextSecondary }
                }
            };
        }
        else
        {
            MetaLabel.FormattedText = null;
            MetaLabel.TextColor = p.TextMuted;
            MetaLabel.Text = vm.CategoryDisplay;
        }
    }

    private void UpdateSkillChips(VacancyItemViewModel vm)
    {
        IReadOnlyList<string> skills = vm.VisibleSkills;
        bool hasMore = vm.HasMoreSkills;
        SkillsRow.IsVisible = skills.Count > 0 || hasMore;

        if (!SkillsRow.IsVisible)
        {
            SkillChip0.IsVisible = false;
            SkillChip1.IsVisible = false;
            SkillChip2.IsVisible = false;
            MoreSkillsChip.IsVisible = false;
            return;
        }

        double maxChipWidth = skills.Count >= 3 ? 86 : 112;
        SetSkillChip(SkillChip0, SkillLabel0, skills, 0, maxChipWidth);
        SetSkillChip(SkillChip1, SkillLabel1, skills, 1, maxChipWidth);
        SetSkillChip(SkillChip2, SkillLabel2, skills, 2, maxChipWidth);

        if (hasMore && !string.IsNullOrEmpty(vm.MoreSkillsText))
        {
            MoreSkillsLabel.Text = vm.MoreSkillsText;
            MoreSkillsChip.IsVisible = true;
        }
        else
        {
            MoreSkillsChip.IsVisible = false;
            MoreSkillsLabel.Text = string.Empty;
        }
    }

    private static void SetSkillChip(Border chip, Label label, IReadOnlyList<string> skills, int index, double maxWidth)
    {
        if (index < skills.Count)
        {
            label.MaximumWidthRequest = maxWidth;
            label.Text = skills[index];
            chip.IsVisible = true;
        }
        else
        {
            chip.IsVisible = false;
            label.Text = string.Empty;
        }
    }

    private void UpdateSourceRow(VacancyItemViewModel vm)
    {
        SourceLabel.Text = vm.SourceText ?? string.Empty;
        if (vm.HasSourceUrl)
        {
            SourceLabel.FontFamily = "OpenSansSemibold";
            SourceExternalIcon.IsVisible = true;
            if (!SourceRow.GestureRecognizers.Contains(_sourceTapRecognizer))
            {
                SourceRow.GestureRecognizers.Add(_sourceTapRecognizer);
            }
        }
        else
        {
            SourceLabel.FontFamily = "OpenSansRegular";
            SourceExternalIcon.IsVisible = false;
            SourceRow.GestureRecognizers.Remove(_sourceTapRecognizer);
        }
    }

    private void UpdateDetailsToggle(VacancyItemViewModel vm)
    {
        CardPalette p = s_palette;
        if (vm.IsDetailsExpanded)
        {
            DetailsChevronLabel.Text = Icons.ChevronUp;
            DetailsChevronLabel.TextColor = p.Primary;
        }
        else
        {
            DetailsChevronLabel.Text = Icons.ChevronDown;
            DetailsChevronLabel.TextColor = p.TextMuted;
        }

        SemanticProperties.SetDescription(DetailsToggleBorder, vm.DetailsToggleText);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => HandleViewModelPropertyChanged(e.PropertyName));
            return;
        }

        HandleViewModelPropertyChanged(e.PropertyName);
    }

    private void HandleViewModelPropertyChanged(string? propertyName)
    {
        if (_currentViewModel is null)
        {
            return;
        }

        switch (propertyName)
        {
            case nameof(VacancyItemViewModel.IsSaved):
                ApplySavedState(_currentViewModel);
                break;
            case nameof(VacancyItemViewModel.DistanceText):
            case nameof(VacancyItemViewModel.HasDistanceText):
                UpdateMetaRow(_currentViewModel);
                break;
            case nameof(VacancyItemViewModel.IsMapExpanded):
            case nameof(VacancyItemViewModel.IsLoadingMap):
            case nameof(VacancyItemViewModel.MapHtml):
            case nameof(VacancyItemViewModel.MapErrorMessage):
            case nameof(VacancyItemViewModel.HasMapError):
                MapToggleLabel.Text = _currentViewModel.MapToggleText;
                UpdateMapSection();
                break;
            case nameof(VacancyItemViewModel.IsDetailsExpanded):
                UpdateDetailsToggle(_currentViewModel);
                UpdateDetailsPanel();
                break;
        }
    }

    private void UpdateMapSection()
    {
        if (_currentViewModel is not { HasAddress: true, IsMapExpanded: true } vm)
        {
            if (CardMapHost.Content is null && !CardMapHost.IsVisible && _mapWebView is null)
            {
                return;
            }

            ReleaseMapHost();
            return;
        }

        CardPalette p = s_palette;

        if (!string.IsNullOrEmpty(vm.MapHtml))
        {
            if (_mapWebView is null)
            {
                _mapWebView = new WebView
                {
                    HeightRequest = 190,
                    HorizontalOptions = LayoutOptions.Fill
                };
            }

            CardMapHost.Content = BuildMapContainer(vm, _mapWebView, p);
            _mapWebView.Source = new HtmlWebViewSource { Html = vm.MapHtml };
            CardMapHost.IsVisible = true;
            return;
        }

        ReleaseMapWebViewOnly();

        if (vm.IsLoadingMap)
        {
            CardMapHost.Content = BuildMapLoadingBanner(p);
            CardMapHost.IsVisible = true;
            return;
        }

        if (vm.HasMapError && !string.IsNullOrWhiteSpace(vm.MapErrorMessage))
        {
            CardMapHost.Content = BuildMapErrorBanner(vm.MapErrorMessage, p);
            CardMapHost.IsVisible = true;
            return;
        }

        CardMapHost.Content = null;
        CardMapHost.IsVisible = false;
    }

    private static Border BuildMapLoadingBanner(CardPalette p) =>
        new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            StrokeThickness = 0,
            BackgroundColor = p.TonalSurface,
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 2),
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new ActivityIndicator
                    {
                        IsRunning = true,
                        Color = p.Primary,
                        HeightRequest = 14,
                        WidthRequest = 14,
                        VerticalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = "Завантаження мапи...",
                        FontSize = 11,
                        TextColor = p.TextSecondary,
                        BackgroundColor = Colors.Transparent,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };

    private static Border BuildMapErrorBanner(string message, CardPalette p) =>
        new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            StrokeThickness = 1,
            Stroke = p.Danger,
            BackgroundColor = p.DangerSurface,
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 2),
            Content = new Label
            {
                Text = message,
                TextColor = p.Danger,
                BackgroundColor = Colors.Transparent,
                FontSize = 11
            }
        };

    private static Border BuildMapContainer(VacancyItemViewModel vm, WebView webView, CardPalette p)
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
            TextColor = p.Primary,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center
        };

        Label addr = new()
        {
            Text = vm.AddressDisplay,
            FontSize = 11,
            FontFamily = "OpenSansSemibold",
            TextColor = p.TextPrimary,
            BackgroundColor = Colors.Transparent,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center
        };

        Label coords = new()
        {
            Text = vm.MapCoordinatesDisplay,
            FontSize = 10,
            TextColor = p.TextMuted,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center
        };

        Border closeBtn = new()
        {
            WidthRequest = 24,
            HeightRequest = 24,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Content = new Label
            {
                Text = Icons.X,
                FontFamily = Icons.FontFamily,
                FontSize = 13,
                TextColor = p.TextMuted,
                BackgroundColor = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        TapGestureRecognizer closeTap = new();
        closeTap.Tapped += (_, _) =>
        {
            if (vm.ToggleMapCommand.CanExecute(null))
            {
                vm.ToggleMapCommand.Execute(null);
            }
        };
        closeBtn.GestureRecognizers.Add(closeTap);

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
            BackgroundColor = p.BgRail,
            Padding = new Thickness(10, 6),
            Content = headerGrid
        };

        Border card = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(p.CardBorder),
            BackgroundColor = p.BgSurface,
            Margin = new Thickness(0, 2, 0, 4),
            Padding = 0,
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children = { headerBar, webView }
            }
        };
        card.GestureRecognizers.Add(new TapGestureRecognizer());
        return card;
    }

    private void ReleaseMapHost()
    {
        ReleaseMapWebViewOnly();
        CardMapHost.Content = null;
        CardMapHost.IsVisible = false;
    }

    private void ReleaseMapWebViewOnly()
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
            if (DetailsHost.Content is null && !DetailsHost.IsVisible)
            {
                return;
            }

            if (DetailsHost.Content is not null)
            {
                DetailsHost.Content = null;
            }

            DetailsHost.IsVisible = false;
            return;
        }

        CardPalette p = s_palette;
        VerticalStackLayout stack = new() { Spacing = 6 };

        stack.Children.Add(new Label
        {
            Text = "Повні характеристики вакансії",
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            TextColor = p.TextPrimary,
            BackgroundColor = Colors.Transparent
        });

        AddMutedLine(stack, $"Категорія: {_currentViewModel.CategoryDisplay}", p);

        if (_currentViewModel.HasSchedule)
        {
            AddMutedLine(stack, $"Графік: {_currentViewModel.Schedule}", p);
        }

        if (_currentViewModel.HasExperience)
        {
            AddMutedLine(stack, $"Досвід: {_currentViewModel.Experience}", p);
        }

        if (_currentViewModel.HasFullAddress)
        {
            AddMutedLine(stack, $"Адреса: {_currentViewModel.FullAddress}", p);
        }

        stack.Children.Add(new BoxView
        {
            HeightRequest = 1,
            Color = p.Divider,
            BackgroundColor = Colors.Transparent,
            Margin = new Thickness(0, 2)
        });

        stack.Children.Add(new Label
        {
            Text = "Повний опис",
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            TextColor = p.TextPrimary,
            BackgroundColor = Colors.Transparent
        });

        stack.Children.Add(new Label
        {
            Text = _currentViewModel.Description,
            FontSize = 12,
            TextColor = p.TextSecondary,
            BackgroundColor = Colors.Transparent
        });

        Border panel = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 0,
            BackgroundColor = p.TonalSurface,
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 2, 0, 0),
            Content = stack
        };

        DetailsHost.Content = panel;
        DetailsHost.IsVisible = true;
    }

    private static void AddMutedLine(VerticalStackLayout stack, string text, CardPalette p) =>
        stack.Children.Add(new Label
        {
            Text = text,
            FontSize = 12,
            TextColor = p.TextSecondary,
            BackgroundColor = Colors.Transparent
        });

    private void OnToggleDetailsTapped(object? sender, TappedEventArgs e)
    {
        if (_currentViewModel is { ToggleDetailsCommand: { } cmd } && cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    private void OnToggleSaveTapped(object? sender, TappedEventArgs e)
    {
        if (_currentViewModel is { ToggleSaveCommand: { } cmd } && cmd.CanExecute(null))
        {
            cmd.Execute(null);
            _ = AnimateHeartPopAsync();
        }
    }

    private async Task AnimateHeartPopAsync()
    {
        HeartPath.CancelAnimations();
        if (await HeartPath.ScaleToAsync(1.22, 80, Easing.CubicOut))
        {
            return;
        }

        await HeartPath.ScaleToAsync(1.0, 90, Easing.CubicIn);
    }

    private void OnToggleMapTapped(object? sender, TappedEventArgs e)
    {
        if (_currentViewModel is { ToggleMapCommand: { } cmd } && cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    private void OnOpenSourceTapped(object? sender, TappedEventArgs e)
    {
        if (_currentViewModel is { HasSourceUrl: true, OpenSourceCommand: { } cmd } && cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    private void OnAnalyzeWithAiTapped(object? sender, TappedEventArgs e)
    {
        if (_currentViewModel is { AnalyzeWithAiCommand: { } cmd } && cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }
}
