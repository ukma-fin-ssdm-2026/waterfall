namespace waterfall.UI.Controls;

/// <summary>
/// Theme-aware vector logo mark ("04 · Terrace Bubbles" outline) synced dynamically
/// with the active Primary accent color across Warm Dark, AMOLED, and Light modes.
/// </summary>
public sealed class WaterfallLogo : GraphicsView
{
    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(
            nameof(AccentColor),
            typeof(Color),
            typeof(WaterfallLogo),
            Color.FromArgb("#D4745E"),
            propertyChanged: (bindable, _, _) => ((WaterfallLogo)bindable).Invalidate());

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public WaterfallLogo()
    {
        BackgroundColor = Colors.Transparent;
        Drawable = new LogoDrawable(this);
        this.SetDynamicResource(AccentColorProperty, "Primary");
    }

    private sealed class LogoDrawable(WaterfallLogo owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            float size = Math.Min(dirtyRect.Width, dirtyRect.Height);
            float scale = size / 64f;
            float offsetX = dirtyRect.X + (dirtyRect.Width - size) / 2f;
            float offsetY = dirtyRect.Y + (dirtyRect.Height - size) / 2f;

            Color color = owner.AccentColor ?? Color.FromArgb("#D4745E");

            canvas.SaveState();
            canvas.Translate(offsetX, offsetY);
            canvas.Scale(scale, scale);

            canvas.StrokeColor = color;
            canvas.StrokeSize = 8.8f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            PathF path = new();
            path.MoveTo(15f, 16f);
            path.LineTo(31f, 16f);
            path.QuadTo(36f, 16f, 36f, 21f);
            path.LineTo(36f, 30f);
            path.QuadTo(36f, 35f, 41f, 35f);
            path.LineTo(44f, 35f);
            path.QuadTo(49f, 35f, 49f, 40f);
            path.LineTo(49f, 48.5f);
            canvas.DrawPath(path);

            canvas.StrokeColor = color.WithAlpha(0.90f);
            canvas.StrokeSize = 3.4f;
            canvas.DrawCircle(21f, 34.5f, 4.5f);

            canvas.StrokeColor = color.WithAlpha(0.82f);
            canvas.StrokeSize = 3.2f;
            canvas.DrawCircle(30.5f, 47.5f, 3.1f);

            canvas.StrokeColor = color.WithAlpha(0.76f);
            canvas.StrokeSize = 2.6f;
            canvas.DrawCircle(17.5f, 48.5f, 2.1f);

            canvas.RestoreState();
        }
    }
}
