using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Resources;

namespace waterfall.UI.Controls;

/// <summary>
/// Dismissible Warm Editorial toast banner with an animated countdown progress bar
/// along the bottom edge (auto-dismisses after DurationMs or immediately on × tap).
/// </summary>
public sealed class ToastBanner : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(ToastBanner), null, propertyChanged: OnTextChanged);

    public static readonly BindableProperty DurationMsProperty =
        BindableProperty.Create(nameof(DurationMs), typeof(uint), typeof(ToastBanner), 5000u);

    public event EventHandler? Dismissed;

    private readonly Label _messageLabel;
    private readonly BoxView _countdownBar;
    private CancellationTokenSource? _timerCts;

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public uint DurationMs
    {
        get => (uint)GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
    }

    public ToastBanner()
    {
        IsVisible = false;

        _messageLabel = new Label
        {
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Center
        };
        _messageLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");

        Button closeBtn = new()
        {
            Text = Icons.X,
            FontFamily = Icons.FontFamily,
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = 0,
            WidthRequest = 24,
            HeightRequest = 24,
            MinimumWidthRequest = 24,
            MinimumHeightRequest = 24,
            VerticalOptions = LayoutOptions.Center
        };
        closeBtn.SetDynamicResource(Button.TextColorProperty, "TextMuted");
        closeBtn.Clicked += (_, _) => DismissNow();

        Grid bodyGrid = new()
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 8,
            Padding = new Thickness(12, 8, 8, 8)
        };
        Grid.SetColumn(_messageLabel, 0);
        Grid.SetColumn(closeBtn, 1);
        bodyGrid.Children.Add(_messageLabel);
        bodyGrid.Children.Add(closeBtn);

        _countdownBar = new BoxView
        {
            HeightRequest = 2,
            AnchorX = 0,
            ScaleX = 1,
            HorizontalOptions = LayoutOptions.Fill
        };
        _countdownBar.SetDynamicResource(BoxView.ColorProperty, "Primary");

        VerticalStackLayout stack = new()
        {
            Spacing = 0,
            Children = { bodyGrid, _countdownBar }
        };

        Border container = new()
        {
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 1,
            Padding = 0,
            Content = stack
        };
        container.SetDynamicResource(Border.StrokeProperty, "CardBorderBrush");
        container.SetDynamicResource(VisualElement.BackgroundColorProperty, "DarkBg1");

        Content = container;
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ToastBanner banner)
        {
            banner.HandleTextChanged(newValue as string);
        }
    }

    private void HandleTextChanged(string? text)
    {
        CancelCountdown();

        if (string.IsNullOrWhiteSpace(text))
        {
            IsVisible = false;
            return;
        }

        _messageLabel.Text = text;
        _countdownBar.ScaleX = 1;
        IsVisible = true;
        _ = StartCountdownAsync();
    }

    private async Task StartCountdownAsync()
    {
        CancellationTokenSource cts = new();
        _timerCts = cts;
        long startedAt = Environment.TickCount64;

        try
        {
            bool aborted = await _countdownBar.ScaleXToAsync(0, DurationMs, Easing.Linear);
            if (aborted || cts.IsCancellationRequested)
            {
                return;
            }

            long remaining = DurationMs - (Environment.TickCount64 - startedAt);
            if (remaining > 150)
            {
                await Task.Delay((int)remaining, cts.Token);
            }

            if (!cts.IsCancellationRequested)
            {
                DismissNow();
            }
        }
        catch
        {
            // Cancelled when dismissed manually or replaced by a newer message
        }
    }

    private void DismissNow()
    {
        CancelCountdown();
        IsVisible = false;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void CancelCountdown()
    {
        if (_timerCts is not null)
        {
            _timerCts.Cancel();
            _timerCts.Dispose();
            _timerCts = null;
        }

        Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(_countdownBar);
    }
}
