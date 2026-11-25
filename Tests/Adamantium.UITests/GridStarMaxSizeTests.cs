using Adamantium.Mathematics;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

// A star track stops at its definition's MaxWidth or MaxHeight; the stars not stopped share what is left.
[TestFixture]
public class GridStarMaxSizeTests
{
    private static Border Cell(Grid grid, int column, int row = 0)
    {
        var cell = new Border();
        Grid.SetColumn(cell, column);
        Grid.SetRow(cell, row);
        grid.Children.Add(cell);
        return cell;
    }

    private static void Lay(Grid grid, double width, double height)
    {
        grid.Measure(new Size<double>(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
    }

    [Test]
    public void AStarAlone_StopsAtItsMaxWidth()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 300 });
        var cell = Cell(grid, 0);

        Lay(grid, 1000, 40);

        Assert.That(cell.Bounds.Width, Is.EqualTo(300).Within(0.5));
    }

    [Test]
    public void AStarStopped_LeavesItsShareToTheOthers()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 200 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var stopped = Cell(grid, 0);
        var second = Cell(grid, 1);
        var third = Cell(grid, 2);

        Lay(grid, 900, 40);

        Assert.Multiple(() =>
        {
            Assert.That(stopped.Bounds.Width, Is.EqualTo(200).Within(0.5));
            Assert.That(second.Bounds.Width, Is.EqualTo(350).Within(0.5));
            Assert.That(third.Bounds.X, Is.EqualTo(550).Within(0.5));
            Assert.That(third.Bounds.Width, Is.EqualTo(350).Within(0.5));
        });
    }

    [Test]
    public void AStarUnderItsMax_TakesItsWholeShare()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 720 });
        var cell = Cell(grid, 0);

        Lay(grid, 500, 40);

        Assert.That(cell.Bounds.Width, Is.EqualTo(500).Within(0.5));
    }

    [Test]
    public void AStarRow_StopsAtItsMaxHeight()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MaxHeight = 100 });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var top = Cell(grid, 0, 0);
        var bottom = Cell(grid, 0, 1);

        Lay(grid, 50, 400);

        Assert.Multiple(() =>
        {
            Assert.That(top.Bounds.Height, Is.EqualTo(100).Within(0.5));
            Assert.That(bottom.Bounds.Y, Is.EqualTo(100).Within(0.5));
            Assert.That(bottom.Bounds.Height, Is.EqualTo(300).Within(0.5));
        });
    }
}
