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
        Color.FromArgb("#6FB59B"),
        Color.FromArgb("#D4745E"),
        Color.FromArgb("#8E8A9E")
    ];

    internal static Color ThemeColor(string key, string fallbackHex)
    {
        if (Application.Current?.Resources.TryGetValue(key, out object? val) == true && val is Color c)
        {
            return c;
        }

        return Color.FromArgb(fallbackHex);
    }

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
        const float topPad = 28f;
        const float bottomPad = 54f;

        float plotWidth = Math.Max(1f, dirtyRect.Width - leftPad - rightPad);
        float plotHeight = Math.Max(1f, dirtyRect.Height - topPad - bottomPad);
        decimal maxSalary = Math.Max(1m, Items.Max(i => i.AverageSalary));

        Color primaryText = ThemeColor("White", "#F2EFEA");
        Color secondaryText = ThemeColor("TextSecondary", "#ABA49A");
        Color mutedText = ThemeColor("TextMuted", "#767068");
        Color axisColor = ThemeColor("DividerColor", "#2E2B27");

        canvas.StrokeColor = axisColor;
        canvas.StrokeSize = 1;
        canvas.DrawLine(leftPad, topPad + plotHeight, leftPad + plotWidth, topPad + plotHeight);

        float slotWidth = plotWidth / Items.Count;
        float barWidth = Math.Min(64f, slotWidth * 0.45f);
        float maxLabelWidth = Math.Max(24f, slotWidth - 6f);

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

            canvas.FontColor = primaryText;
            canvas.FontSize = 12;
            string avgText = FitSingleLine(canvas, item.FormattedAverage, maxLabelWidth, 12f);
            canvas.DrawString(avgText, centerX - slotWidth / 2f, y - 24, slotWidth, 22, HorizontalAlignment.Center, VerticalAlignment.Center);

            canvas.FontColor = secondaryText;
            canvas.FontSize = 11;
            string gradeText = FitSingleLine(canvas, item.Grade, maxLabelWidth, 11f);
            canvas.DrawString(gradeText, centerX - slotWidth / 2f, topPad + plotHeight + 6, slotWidth, 20, HorizontalAlignment.Center, VerticalAlignment.Center);

            canvas.FontColor = mutedText;
            canvas.FontSize = 10;
            string rangeText = FitSingleLine(canvas, item.RangeText, maxLabelWidth, 10f);
            canvas.DrawString(rangeText, centerX - slotWidth / 2f, topPad + plotHeight + 28, slotWidth, 20, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    internal static void DrawEmpty(ICanvas canvas, RectF rect, string message)
    {
        canvas.FontColor = ThemeColor("TextSecondary", "#ABA49A");
        canvas.FontSize = 12;
        canvas.DrawString(message, 0, 0, rect.Width, rect.Height, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    internal static string FitSingleLine(ICanvas canvas, string? text, float maxWidth, float fontSize)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 12f)
        {
            return text ?? string.Empty;
        }

        // Fast path: fits cleanly on one line
        SizeF measured = canvas.GetStringSize(text, Microsoft.Maui.Graphics.Font.Default, fontSize);
        if (measured.Width <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        int low = 1;
        int high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            string candidate = string.Concat(text.AsSpan(0, mid).TrimEnd(), ellipsis);
            if (canvas.GetStringSize(candidate, Microsoft.Maui.Graphics.Font.Default, fontSize).Width <= maxWidth)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return string.Concat(text.AsSpan(0, low).TrimEnd(), ellipsis);
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

        float labelWidth = Math.Clamp(dirtyRect.Width * 0.28f, 118f, 220f);
        float valueWidth = Math.Clamp(dirtyRect.Width * 0.16f, 84f, 108f);
        const float gap = 10f;
        const float rowPad = 6f;

        float barAreaWidth = Math.Max(20f, dirtyRect.Width - labelWidth - valueWidth - gap * 2);
        double maxVal = Math.Max(1.0, Entries.Max(e => e.Value));
        float rowHeight = dirtyRect.Height / Entries.Count;
        float barHeight = Math.Min(16f, Math.Max(8f, rowHeight - rowPad * 2));
        float textBoxHeight = Math.Max(24f, rowHeight);

        Color trackColor = GradeSalaryBarChartDrawable.ThemeColor("TonalSurface", "#2C2926");
        Color textColor = GradeSalaryBarChartDrawable.ThemeColor("White", "#F2EFEA");
        Color mutedColor = GradeSalaryBarChartDrawable.ThemeColor("TextSecondary", "#ABA49A");

        for (int i = 0; i < Entries.Count; i++)
        {
            HorizontalBarEntry entry = Entries[i];
            float yCenter = rowHeight * (i + 0.5f);
            float barY = yCenter - barHeight / 2f;

            canvas.FontColor = textColor;
            canvas.FontSize = 12;
            string fittedLabel = GradeSalaryBarChartDrawable.FitSingleLine(canvas, entry.Label, labelWidth - 4f, 12f);
            canvas.DrawString(fittedLabel, 0, yCenter - textBoxHeight / 2f, labelWidth, textBoxHeight, HorizontalAlignment.Left, VerticalAlignment.Center);

            float barX = labelWidth + gap;
            canvas.FillColor = trackColor;
            canvas.FillRoundedRectangle(barX, barY, barAreaWidth, barHeight, 5);

            float fillWidth = Math.Max(6f, (float)(barAreaWidth * (entry.Value / maxVal)));
            canvas.FillColor = BarColor;
            canvas.FillRoundedRectangle(barX, barY, fillWidth, barHeight, 5);

            canvas.FontColor = mutedColor;
            canvas.FontSize = 11;
            string fittedValue = GradeSalaryBarChartDrawable.FitSingleLine(canvas, entry.DisplayValue, valueWidth, 11f);
            canvas.DrawString(
                fittedValue,
                barX + barAreaWidth + gap,
                yCenter - textBoxHeight / 2f,
                valueWidth,
                textBoxHeight,
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
        Color.FromArgb("#6FB59B"),
        Color.FromArgb("#D4745E"),
        Color.FromArgb("#C9975B"),
        Color.FromArgb("#8E8A9E"),
        Color.FromArgb("#7A9EC2")
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

        Color primaryText = GradeSalaryBarChartDrawable.ThemeColor("White", "#F2EFEA");
        Color secondaryText = GradeSalaryBarChartDrawable.ThemeColor("TextSecondary", "#ABA49A");

        canvas.FontColor = primaryText;
        canvas.FontSize = 16;
        canvas.DrawString(((int)total).ToString(), cx - radius, cy - 18, radius * 2f, 24, HorizontalAlignment.Center, VerticalAlignment.Center);

        canvas.FontColor = secondaryText;
        canvas.FontSize = 10;
        canvas.DrawString(CenterSubtitle, cx - radius, cy + 4, radius * 2f, 18, HorizontalAlignment.Center, VerticalAlignment.Center);

        float legendX = cx + diameter / 2f + 20f;
        float legendWidth = Math.Max(60f, dirtyRect.Width - legendX - 4f);
        float rowHeight = Math.Min(30f, (dirtyRect.Height - 16f) / Items.Count);
        float startY = (dirtyRect.Height - rowHeight * Items.Count) / 2f;
        float nameMaxWidth = Math.Max(40f, legendWidth * 0.56f - 18f);

        for (int i = 0; i < Items.Count; i++)
        {
            AnalyticsShareItem item = Items[i];
            float rowY = startY + rowHeight * i + rowHeight / 2f;

            canvas.FillColor = Palette[i % Palette.Length];
            canvas.FillRoundedRectangle(legendX, rowY - 5, 10, 10, 3);

            canvas.FontColor = primaryText;
            canvas.FontSize = 12;
            string fittedName = GradeSalaryBarChartDrawable.FitSingleLine(canvas, item.Name, nameMaxWidth, 12f);
            canvas.DrawString(fittedName, legendX + 16, rowY - 12, nameMaxWidth + 4f, 24, HorizontalAlignment.Left, VerticalAlignment.Center);

            canvas.FontColor = secondaryText;
            canvas.FontSize = 11;
            canvas.DrawString(item.DisplayLabel, legendX + 16, rowY - 12, legendWidth - 16, 24, HorizontalAlignment.Right, VerticalAlignment.Center);
        }
    }
}
