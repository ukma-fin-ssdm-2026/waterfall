// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Waterfall.Client.ViewModels;

namespace waterfall.UI.Controls;

/// <summary>
/// Стовпчикова діаграма середніх зарплат за грейдами (Junior / Middle / Senior).
/// </summary>
public sealed class GradeSalaryBarChartDrawable : IDrawable
{
    private static readonly Color[] GradeColors =
    [
        Color.FromArgb("#2ED573"),
        Color.FromArgb("#4F8EF7"),
        Color.FromArgb("#A55EEA")
    ];

    public IReadOnlyList<GradeSalaryItem> Items { get; set; } = [];

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
        {
            return;
        }

        if (Items.Count == 0)
        {
            DrawEmpty(canvas, dirtyRect, "Недостатньо даних про зарплати за досвідом");
            return;
        }

        const float leftPad = 14f;
        const float rightPad = 14f;
        const float topPad = 26f;
        const float bottomPad = 46f;

        float plotWidth = Math.Max(1f, dirtyRect.Width - leftPad - rightPad);
        float plotHeight = Math.Max(1f, dirtyRect.Height - topPad - bottomPad);
        decimal maxSalary = Math.Max(1m, Items.Max(i => i.AverageSalary));

        canvas.StrokeColor = Color.FromArgb("#2F3542");
        canvas.StrokeSize = 1;
        canvas.DrawLine(leftPad, topPad + plotHeight, leftPad + plotWidth, topPad + plotHeight);

        float slotWidth = plotWidth / Items.Count;
        float barWidth = Math.Min(64f, slotWidth * 0.45f);

        for (int i = 0; i < Items.Count; i++)
        {
            GradeSalaryItem item = Items[i];
            float ratio = (float)(item.AverageSalary / maxSalary);
            float barHeight = Math.Max(8f, plotHeight * ratio);
            float centerX = leftPad + slotWidth * (i + 0.5f);
            float x = centerX - barWidth / 2f;
            float y = topPad + plotHeight - barHeight;

            canvas.FillColor = GradeColors[i % GradeColors.Length];
            canvas.FillRoundedRectangle(x, y, barWidth, barHeight, 6, 6, 0, 0);

            canvas.FontColor = Colors.White;
            canvas.FontSize = 12;
            canvas.DrawString(item.FormattedAverage, centerX - slotWidth / 2f, y - 22, slotWidth, 20, HorizontalAlignment.Center, VerticalAlignment.Center);

            canvas.FontColor = Color.FromArgb("#DFE4EA");
            canvas.FontSize = 11;
            canvas.DrawString(item.Grade, centerX - slotWidth / 2f, topPad + plotHeight + 6, slotWidth, 16, HorizontalAlignment.Center, VerticalAlignment.Top);

            canvas.FontColor = Color.FromArgb("#A4B0BE");
            canvas.FontSize = 10;
            canvas.DrawString(item.RangeText, centerX - slotWidth / 2f, topPad + plotHeight + 24, slotWidth, 16, HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }

    internal static void DrawEmpty(ICanvas canvas, RectF rect, string message)
    {
        canvas.FontColor = Color.FromArgb("#A4B0BE");
        canvas.FontSize = 12;
        canvas.DrawString(message, 0, 0, rect.Width, rect.Height, HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}

public sealed record HorizontalBarEntry(string Label, double Value, string DisplayValue);

/// <summary>
/// Горизонтальна стовпчикова діаграма (використовується для найдорожчих технологій, міст, компаній).
/// </summary>
public sealed class HorizontalBarChartDrawable : IDrawable
{
    public IReadOnlyList<HorizontalBarEntry> Entries { get; set; } = [];

    public Color BarColor { get; set; } = Color.FromArgb("#4F8EF7");

    public string EmptyMessage { get; set; } = "Недостатньо даних";

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
        {
            return;
        }

        if (Entries.Count == 0)
        {
            GradeSalaryBarChartDrawable.DrawEmpty(canvas, dirtyRect, EmptyMessage);
            return;
        }

        const float labelWidth = 118f;
        const float valueWidth = 104f;
        const float gap = 8f;
        const float rowPad = 6f;

        float barAreaWidth = Math.Max(20f, dirtyRect.Width - labelWidth - valueWidth - gap * 2);
        double maxVal = Math.Max(1.0, Entries.Max(e => e.Value));
        float rowHeight = dirtyRect.Height / Entries.Count;
        float barHeight = Math.Min(16f, Math.Max(8f, rowHeight - rowPad * 2));

        Color trackColor = Color.FromArgb("#2F3542");
        Color textColor = Colors.White;
        Color mutedColor = Color.FromArgb("#A4B0BE");

        for (int i = 0; i < Entries.Count; i++)
        {
            HorizontalBarEntry entry = Entries[i];
            float yCenter = rowHeight * (i + 0.5f);
            float barY = yCenter - barHeight / 2f;

            canvas.FontColor = textColor;
            canvas.FontSize = 12;
            canvas.DrawString(entry.Label, 0, yCenter - 10, labelWidth, 20, HorizontalAlignment.Left, VerticalAlignment.Center);

            float barX = labelWidth + gap;
            canvas.FillColor = trackColor;
            canvas.FillRoundedRectangle(barX, barY, barAreaWidth, barHeight, 5);

            float fillWidth = Math.Max(6f, (float)(barAreaWidth * (entry.Value / maxVal)));
            canvas.FillColor = BarColor;
            canvas.FillRoundedRectangle(barX, barY, fillWidth, barHeight, 5);

            canvas.FontColor = mutedColor;
            canvas.FontSize = 11;
            canvas.DrawString(
                entry.DisplayValue,
                barX + barAreaWidth + gap,
                yCenter - 10,
                valueWidth,
                20,
                HorizontalAlignment.Right,
                VerticalAlignment.Center);
        }
    }
}

/// <summary>
/// Кільцева діаграма (Donut Chart) для часток форматів роботи та джерел.
/// </summary>
public sealed class DonutChartDrawable : IDrawable
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb("#2ED573"),
        Color.FromArgb("#4F8EF7"),
        Color.FromArgb("#FFA502"),
        Color.FromArgb("#A55EEA"),
        Color.FromArgb("#FF6B81")
    ];

    public IReadOnlyList<AnalyticsShareItem> Items { get; set; } = [];

    public string CenterSubtitle { get; set; } = "вакансій";

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
        {
            return;
        }

        if (Items.Count == 0)
        {
            GradeSalaryBarChartDrawable.DrawEmpty(canvas, dirtyRect, "Немає даних");
            return;
        }

        double total = Items.Sum(i => i.Count);
        if (total <= 0)
        {
            return;
        }

        float diameter = Math.Min(dirtyRect.Height - 20f, Math.Min(150f, dirtyRect.Width * 0.42f));
        float cx = diameter / 2f + 12f;
        float cy = dirtyRect.Height / 2f;
        float strokeWidth = Math.Max(14f, diameter * 0.22f);
        float radius = (diameter - strokeWidth) / 2f;

        float startAngle = 90f;
        for (int i = 0; i < Items.Count; i++)
        {
            float sweep = (float)(360.0 * (Items[i].Count / total));
            if (sweep <= 0.1f)
            {
                continue;
            }

            canvas.StrokeColor = Palette[i % Palette.Length];
            canvas.StrokeSize = strokeWidth;
            canvas.StrokeLineCap = LineCap.Butt;

            if (Items.Count == 1 || sweep >= 359.5f)
            {
                canvas.DrawCircle(cx, cy, radius);
            }
            else
            {
                canvas.DrawArc(
                    cx - radius,
                    cy - radius,
                    radius * 2f,
                    radius * 2f,
                    startAngle,
                    startAngle - sweep,
                    clockwise: true,
                    closed: false);
            }

            startAngle -= sweep;
        }

        canvas.FontColor = Colors.White;
        canvas.FontSize = 16;
        canvas.DrawString(((int)total).ToString(), cx - radius, cy - 16, radius * 2f, 20, HorizontalAlignment.Center, VerticalAlignment.Center);

        canvas.FontColor = Color.FromArgb("#A4B0BE");
        canvas.FontSize = 10;
        canvas.DrawString(CenterSubtitle, cx - radius, cy + 4, radius * 2f, 14, HorizontalAlignment.Center, VerticalAlignment.Center);

        float legendX = cx + diameter / 2f + 20f;
        float legendWidth = Math.Max(60f, dirtyRect.Width - legendX - 4f);
        float rowHeight = Math.Min(28f, (dirtyRect.Height - 16f) / Items.Count);
        float startY = (dirtyRect.Height - rowHeight * Items.Count) / 2f;

        for (int i = 0; i < Items.Count; i++)
        {
            AnalyticsShareItem item = Items[i];
            float rowY = startY + rowHeight * i + rowHeight / 2f;

            canvas.FillColor = Palette[i % Palette.Length];
            canvas.FillRoundedRectangle(legendX, rowY - 5, 10, 10, 3);

            canvas.FontColor = Colors.White;
            canvas.FontSize = 12;
            canvas.DrawString(item.Name, legendX + 16, rowY - 10, legendWidth * 0.55f, 20, HorizontalAlignment.Left, VerticalAlignment.Center);

            canvas.FontColor = Color.FromArgb("#A4B0BE");
            canvas.FontSize = 11;
            canvas.DrawString(item.DisplayLabel, legendX + 16, rowY - 10, legendWidth - 16, 20, HorizontalAlignment.Right, VerticalAlignment.Center);
        }
    }
}
