using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Resources;
using waterfall.UI.Services;

namespace waterfall.UI.Controls;

/// <summary>
/// Shared slide/fade bottom-sheet (Phone) / side-drawer (Desktop) chrome with scrim backdrop,
/// grabber handle, header with close button, scrollable body slot, and sticky footer slot.
/// </summary>
[ContentProperty(nameof(BodyContent))]
public sealed class OverlaySheetFrame : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(OverlaySheetFrame), string.Empty);

    public static readonly BindableProperty CloseCommandProperty =
        BindableProperty.Create(nameof(CloseCommand), typeof(ICommand), typeof(OverlaySheetFrame));

    public static readonly BindableProperty BodyContentProperty =
        BindableProperty.Create(
            nameof(BodyContent),
            typeof(View),
            typeof(OverlaySheetFrame),
            propertyChanged: static (bindable, _, newValue) =>
                ((OverlaySheetFrame)bindable)._bodyScroll.Content = (View?)newValue);

    public static readonly BindableProperty FooterContentProperty =
        BindableProperty.Create(
            nameof(FooterContent),
            typeof(View),
            typeof(OverlaySheetFrame),
            propertyChanged: static (bindable, _, newValue) =>
                ((OverlaySheetFrame)bindable)._footerHost.Content = (View?)newValue);

    public static readonly BindableProperty DesktopWidthProperty =
        BindableProperty.Create(
            nameof(DesktopWidth),
            typeof(double),
            typeof(OverlaySheetFrame),
            470d,
            propertyChanged: static (bindable, _, newValue) =>
            {
                if (DeviceInfo.Idiom != DeviceIdiom.Phone)
                {
                    ((OverlaySheetFrame)bindable).Card.WidthRequest = (double)newValue;
                }
            });

    private readonly ScrollView _bodyScroll;
    private readonly Border _footerHost;

    internal BoxView Backdrop { get; }
    internal Border Card { get; }

    public OverlaySheetFrame()
    {
        bool isPhone = DeviceInfo.Idiom == DeviceIdiom.Phone;

        Backdrop = new BoxView();
        Backdrop.SetDynamicResource(BoxView.ColorProperty, "Scrim");
        Backdrop.SetDynamicResource(VisualElement.BackgroundColorProperty, "Scrim");
        TapGestureRecognizer backdropTap = new();
        backdropTap.SetBinding(TapGestureRecognizer.CommandProperty, static (OverlaySheetFrame v) => v.CloseCommand, source: this);
        Backdrop.GestureRecognizers.Add(backdropTap);

        Label titleLabel = new()
        {
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "White");
        titleLabel.SetBinding(Label.TextProperty, static (OverlaySheetFrame v) => v.Title, source: this);

        Button closeButton = new()
        {
            Text = Icons.X
        };
        MountedPageHelper.ApplyStyle(closeButton, "GhostIconButton");
        closeButton.SetBinding(Button.CommandProperty, static (OverlaySheetFrame v) => v.CloseCommand, source: this);

        Grid headerRow = new()
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Padding = new Thickness(16, 12),
            ColumnSpacing = 10
        };
        Grid.SetColumn(titleLabel, 0);
        Grid.SetColumn(closeButton, 1);
        headerRow.Children.Add(titleLabel);
        headerRow.Children.Add(closeButton);

        BoxView divider = new() { HeightRequest = 1 };
        divider.SetDynamicResource(BoxView.ColorProperty, "DividerColor");

        VerticalStackLayout headerChrome = new() { Spacing = 0 };
        if (isPhone)
        {
            BoxView grabber = new()
            {
                WidthRequest = 36,
                HeightRequest = 4,
                CornerRadius = 2,
                Opacity = 0.4,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 8, 0, 2)
            };
            grabber.SetDynamicResource(BoxView.ColorProperty, "TextMuted");
            grabber.SetDynamicResource(VisualElement.BackgroundColorProperty, "TextMuted");
            headerChrome.Children.Add(grabber);
        }

        headerChrome.Children.Add(headerRow);
        headerChrome.Children.Add(divider);

        _bodyScroll = new ScrollView
        {
            Padding = new Thickness(14, 12)
        };

        _footerHost = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(14, 10)
        };
        _footerHost.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkRail");

        Grid cardGrid = new()
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)]
        };
        Grid.SetRow(headerChrome, 0);
        Grid.SetRow(_bodyScroll, 1);
        Grid.SetRow(_footerHost, 2);
        cardGrid.Children.Add(headerChrome);
        cardGrid.Children.Add(_bodyScroll);
        cardGrid.Children.Add(_footerHost);

        Card = new Border
        {
            StrokeThickness = 1,
            Padding = 0,
            HorizontalOptions = isPhone ? LayoutOptions.Fill : LayoutOptions.End,
            VerticalOptions = isPhone ? LayoutOptions.End : LayoutOptions.Fill,
            MaximumHeightRequest = isPhone ? 560 : 10000,
            StrokeShape = isPhone
                ? new RoundRectangle { CornerRadius = new CornerRadius(18, 18, 0, 0) }
                : new RoundRectangle { CornerRadius = 16 },
            Content = cardGrid
        };
        if (!isPhone)
        {
            Card.WidthRequest = DesktopWidth;
        }

        Card.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");
        Card.SetDynamicResource(Border.StrokeProperty, "CardBorderBrush");

        Content = new Grid
        {
            BackgroundColor = Colors.Transparent,
            Children = { Backdrop, Card }
        };
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public View? BodyContent
    {
        get => (View?)GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    public View? FooterContent
    {
        get => (View?)GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    public double DesktopWidth
    {
        get => (double)GetValue(DesktopWidthProperty);
        set => SetValue(DesktopWidthProperty, value);
    }
}
