using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A cell across star rows and an Auto row, in a star column, is measured at the size its tracks give it only:
/// the size a star has before it is shared out is read for neither the row nor the column.</summary>
[TestFixture]
public class GridStarCellMeasureTests
{
    private sealed class MeasureRecorder : Panel
    {
        public List<Size<double>> Offered { get; } = [];

        protected override Size<double> MeasureOverride(Size<double> availableSize)
        {
            Offered.Add(availableSize);
            return new Size<double>(10, 10);
        }
    }

    private static Grid Sides(out MeasureRecorder cell)
    {
        var grid = new Grid();
        foreach (var length in new[] { GridLength.Star, GridLength.Auto, GridLength.Star })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
            grid.RowDefinitions.Add(new RowDefinition { Height = length });
        }

        cell = new MeasureRecorder();
        Grid.SetColumn(cell, 2);
        Grid.SetRowSpan(cell, 3);
        grid.Children.Add(cell);
        return grid;
    }

    [Test]
    public void ACellAcrossStarRowsAndAnAutoRow_IsMeasuredAtItsColumnsWidthOnly()
    {
        var grid = Sides(out var cell);

        grid.Measure(new Size<double>(1000, 600));

        Assert.That(cell.Offered, Is.EqualTo(new[] { new Size<double>(500, 600) }));
    }

    [Test]
    public void ACellAcrossStarColumnsAndAnAutoColumn_IsMeasuredAtItsRowsHeightOnly()
    {
        var grid = Sides(out var cell);
        Grid.SetColumn(cell, 0);
        Grid.SetColumnSpan(cell, 3);
        Grid.SetRow(cell, 2);
        Grid.SetRowSpan(cell, 1);

        grid.Measure(new Size<double>(1000, 600));

        Assert.That(cell.Offered, Is.EqualTo(new[] { new Size<double>(1000, 300) }));
    }

    [Test]
    public void TheGrid_StillSharesItsRoom_AsBefore()
    {
        var grid = Sides(out var cell);

        grid.Measure(new Size<double>(1000, 600));
        grid.Arrange(new Rect(0, 0, 1000, 600));

        Assert.That((cell.Bounds.X, cell.Bounds.Width, cell.Bounds.Height), Is.EqualTo((500.0, 500.0, 600.0)));
    }
}
