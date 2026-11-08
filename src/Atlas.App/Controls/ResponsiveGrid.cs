using Avalonia;
using Avalonia.Controls;

namespace Atlas.App.Controls;

/// <summary>Lays out items in as many equal columns as fit, so cards use the full width of the page at any window size.</summary>
public sealed class ResponsiveGrid : Panel
{
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<ResponsiveGrid, double>(nameof(MinColumnWidth), 280);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ResponsiveGrid, double>(nameof(Spacing), 12);

    static ResponsiveGrid() => AffectsMeasure<ResponsiveGrid>(MinColumnWidthProperty, SpacingProperty);

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    internal int ColumnsFor(double width) =>
        double.IsInfinity(width) ? 1 : Math.Max(1, (int)((width + Spacing) / (MinColumnWidth + Spacing)));

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = ColumnsFor(availableSize.Width);
        var columnWidth = ColumnWidth(availableSize.Width, columns);

        foreach (var child in Children)
            child.Measure(new Size(columnWidth, double.PositiveInfinity));

        var rows = RowHeights(columns);
        var height = rows.Sum() + (Spacing * Math.Max(0, rows.Count - 1));
        return new Size(double.IsInfinity(availableSize.Width) ? columnWidth : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnsFor(finalSize.Width);
        var columnWidth = ColumnWidth(finalSize.Width, columns);
        var rows = RowHeights(columns);

        var y = 0d;
        for (var index = 0; index < Children.Count; index++)
        {
            var row = index / columns;
            if (index > 0 && index % columns == 0)
                y += rows[row - 1] + Spacing;

            var x = (index % columns) * (columnWidth + Spacing);
            Children[index].Arrange(new Rect(x, y, columnWidth, rows[row]));
        }

        return finalSize;
    }

    private double ColumnWidth(double width, int columns) =>
        double.IsInfinity(width) ? MinColumnWidth : Math.Max(0, (width - (Spacing * (columns - 1))) / columns);

    private List<double> RowHeights(int columns)
    {
        var rows = new List<double>();
        for (var index = 0; index < Children.Count; index++)
        {
            if (index % columns == 0)
                rows.Add(0);

            rows[^1] = Math.Max(rows[^1], Children[index].DesiredSize.Height);
        }

        return rows;
    }
}
