using Microsoft.Maui.Controls.Shapes;
using waterfall.UI.Resources;

namespace waterfall.UI.Views;

/// <summary>
/// Builds the compact Warm Editorial sort dropdown with tap-to-flip direction toggling
/// shared by Catalog and Database pages on Phone.
/// </summary>
internal static class SortMenuHelper
{
    private sealed record SortCriterion(
        string Label,
        string PrimaryOption,
        string? FlippedOption,
        bool PrimaryIsDescending);

    private static readonly SortCriterion[] CatalogCriteria =
    [
        new("Дата", "За датою (спочатку нові)", "За датою (спочатку старі)", PrimaryIsDescending: true),
        new("Зарплата", "За зарплатою (від найвищої ↓)", "За зарплатою (від найнижчої ↑)", PrimaryIsDescending: true),
        new("Відстань", "За відстанню (спочатку ближчі)", "За відстанню (спочатку дальші)", PrimaryIsDescending: false),
        new("Назва", "За назвою посади (А → Я)", "За назвою посади (Я → А)", PrimaryIsDescending: false),
        new("Компанія", "За компанією (А → Я)", null, PrimaryIsDescending: false)
    ];

    private static readonly SortCriterion[] DatabaseCriteria =
    [
        new("Дата публікації", "За датою публікації (спочатку нові)", "За датою публікації (спочатку старі)", PrimaryIsDescending: true),
        new("Додано в БД", "За датою додавання в БД (спочатку нові)", null, PrimaryIsDescending: true),
        new("Зарплата", "За зарплатою (від найвищої ↓)", "За зарплатою (від найнижчої ↑)", PrimaryIsDescending: true),
        new("Досвід", "За досвідом (від меншого ↑)", "За досвідом (від більшого ↓)", PrimaryIsDescending: false),
        new("Відстань", "За відстанню (спочатку ближчі)", "За відстанню (спочатку дальші)", PrimaryIsDescending: false),
        new("Назва", "За назвою посади (А → Я)", "За назвою посади (Я → А)", PrimaryIsDescending: false),
        new("Компанія", "За компанією (А → Я)", null, PrimaryIsDescending: false),
        new("Категорія", "За категорією (А → Я)", null, PrimaryIsDescending: false)
    ];

    public static void PopulateCatalog(
        VerticalStackLayout stack,
        string selectedOption,
        Action<string> onSelected) =>
        Populate(stack, CatalogCriteria, selectedOption, onSelected);

    public static void PopulateDatabase(
        VerticalStackLayout stack,
        string selectedOption,
        Action<string> onSelected) =>
        Populate(stack, DatabaseCriteria, selectedOption, onSelected);

    private static void Populate(
        VerticalStackLayout stack,
        IReadOnlyList<SortCriterion> criteria,
        string selectedOption,
        Action<string> onSelected)
    {
        stack.Children.Clear();

        foreach (SortCriterion criterion in criteria)
        {
            bool isPrimary = string.Equals(selectedOption, criterion.PrimaryOption, StringComparison.Ordinal);
            bool isFlipped = criterion.FlippedOption is not null &&
                             string.Equals(selectedOption, criterion.FlippedOption, StringComparison.Ordinal);
            bool isSelected = isPrimary || isFlipped;

            bool isDescending = isPrimary ? criterion.PrimaryIsDescending : !criterion.PrimaryIsDescending;

            Label check = new()
            {
                Text = Icons.Check,
                FontFamily = Icons.FontFamily,
                FontSize = 13,
                WidthRequest = 16,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = isSelected ? 1 : 0
            };
            check.SetDynamicResource(Label.TextColorProperty, "Primary");

            Label label = new()
            {
                Text = criterion.Label,
                FontSize = 12.5,
                FontFamily = isSelected ? "OpenSansSemibold" : "OpenSansRegular",
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
                VerticalOptions = LayoutOptions.Center
            };
            label.SetDynamicResource(Label.TextColorProperty, isSelected ? "White" : "TextSecondary");

            Label directionBadge = new()
            {
                Text = isDescending ? "↓" : "↑",
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                WidthRequest = 16,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center,
                Opacity = isSelected ? 1 : 0
            };
            directionBadge.SetDynamicResource(Label.TextColorProperty, "Primary");

            Grid rowGrid = new()
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(16),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(16)
                ],
                ColumnSpacing = 8
            };
            Grid.SetColumn(check, 0);
            Grid.SetColumn(label, 1);
            Grid.SetColumn(directionBadge, 2);
            rowGrid.Children.Add(check);
            rowGrid.Children.Add(label);
            rowGrid.Children.Add(directionBadge);

            Border row = new()
            {
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 0,
                Padding = new Thickness(10, 9),
                Content = rowGrid
            };
            if (isSelected)
            {
                row.SetDynamicResource(VisualElement.BackgroundColorProperty, "TonalSurface");
            }
            else
            {
                row.BackgroundColor = Colors.Transparent;
            }

            string targetOption = isPrimary && criterion.FlippedOption is not null
                ? criterion.FlippedOption
                : criterion.PrimaryOption;

            TapGestureRecognizer tap = new();
            tap.Tapped += (_, _) => onSelected(targetOption);
            row.GestureRecognizers.Add(tap);

            stack.Children.Add(row);
        }
    }
}
