using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A UniformGrid hosting a list realizes only the rows in view: its cells still share the width between the columns,
/// and a row is as tall as the tallest item realized so far - what the grid always did, over the items it has seen.
/// </summary>
[TestFixture]
public class UniformGridVirtualizationTests
{
    private const int Count = 200;
    private const double ViewWidth = 400, ViewHeight = 300;
    private const double ItemHeight = 30, RowGap = 4, ColumnGap = 6;
    private const double CellWidth = (ViewWidth - ColumnGap) / 2, RowPitch = ItemHeight + RowGap;

    private static ControlTemplate ItemsPresenterTemplate() => new(() =>
    {
        var presenter = new ItemsPresenter();
        var result = new TemplateResult { RootComponent = presenter };
        result.RegisterName("PART_ItemsPresenter", presenter);
        return result;
    });

    private static ItemsControl GridOfRows(int columns = 2, int rows = 0, bool virtualizing = true)
    {
        var ic = new ItemsControl
        {
            ItemsSource = Enumerable.Range(0, Count).Cast<object>().ToList(),
            ItemsPanel = new ItemsPanelTemplate(() => new TemplateResult
            {
                RootComponent = new UniformGrid
                {
                    Columns = columns,
                    Rows = rows,
                    RowSpacing = RowGap,
                    ColumnSpacing = ColumnGap,
                    IsVirtualizing = virtualizing
                }
            }),
            ItemTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Border { Height = ItemHeight } }),
            Template = ItemsPresenterTemplate()
        };

        Settle(ic);
        return ic;
    }

    private static void SetHeight(ItemsControl ic, int index, double height)
    {
        if (ic.ItemContainerGenerator.ContainerFromIndex(index) is ContentPresenter presenter)
        {
            presenter.Height = height;
        }
    }

    private static void Settle(ItemsControl ic)
    {
        for (var i = 0; i < 3; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(ic);
            ic.Measure(new Size(ViewWidth, ViewHeight));
            ic.Arrange(new Rect(0, 0, ViewWidth, ViewHeight));
        }
    }

    private static UniformGrid PanelOf(ItemsControl ic) => (UniformGrid)ic.ItemsHostPanel;

    private static Rect BoundsOf(ItemsControl ic, int index) => ic.ItemContainerGenerator.ContainerFromIndex(index).Bounds;

    [Test]
    public void AsAnItemsHost_ItRealizesOnlyTheRowsInView()
    {
        var ic = GridOfRows();
        var realized = ic.ItemContainerGenerator.RealizedIndices.Count;

        Assert.Multiple(() =>
        {
            Assert.That(realized, Is.GreaterThan(0));
            Assert.That(realized, Is.LessThan(Count / 4), "a screenful of rows, not the whole list");
            Assert.That(realized % 2, Is.Zero, "whole rows");
        });
    }

    [Test]
    public void EachRealizedItemSitsInItsCell()
    {
        var ic = GridOfRows();

        Assert.Multiple(() =>
        {
            foreach (var index in ic.ItemContainerGenerator.RealizedIndices)
            {
                var expected = new Rect(index % 2 * (CellWidth + ColumnGap), index / 2 * RowPitch, CellWidth, ItemHeight);
                var actual = BoundsOf(ic, index);
                Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.01), $"item {index}");
                Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.01), $"item {index}");
                Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(0.01), $"item {index}");
                Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(0.01), $"item {index}");
            }
        });
    }

    [Test]
    public void TheExtentCoversEveryRow()
    {
        var panel = PanelOf(GridOfRows());

        Assert.Multiple(() =>
        {
            Assert.That(panel.Extent.Height, Is.EqualTo(100 * ItemHeight + 99 * RowGap).Within(0.01));
            Assert.That(panel.Extent.Width, Is.EqualTo(ViewWidth).Within(0.01));
            Assert.That(panel.EffectiveColumns, Is.EqualTo(2));
            Assert.That(panel.EffectiveRows, Is.EqualTo(100));
        });
    }

    [Test]
    public void ScrollingRealizesTheRowsAtTheOffset()
    {
        var ic = GridOfRows();
        var panel = PanelOf(ic);

        panel.SetOffset(new Vector2(0, 40 * RowPitch));
        Settle(ic);

        var realized = ic.ItemContainerGenerator.RealizedIndices.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(realized, Does.Contain(80), "the first row at the offset");
            Assert.That(realized, Does.Not.Contain(0), "the top of the list is let go");
            Assert.That(BoundsOf(ic, 80).Y, Is.EqualTo(40 * RowPitch).Within(0.01));
        });
    }

    [Test]
    public void TheTallestRealizedItemSetsTheRowHeight()
    {
        var ic = GridOfRows();
        SetHeight(ic, 3, 50);
        Settle(ic);

        Assert.Multiple(() =>
        {
            Assert.That(PanelOf(ic).CellSize.Height, Is.EqualTo(50).Within(0.01));
            Assert.That(BoundsOf(ic, 2).Y, Is.EqualTo(50 + RowGap).Within(0.01), "every row takes the cell's height");
        });
    }

    [Test]
    public void AnItemNotRealized_StillHasItsCell()
    {
        var ic = GridOfRows();

        Assert.That(PanelOf(ic).TryGetItemRect(151, out var rect), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(rect.X, Is.EqualTo(CellWidth + ColumnGap).Within(0.01));
            Assert.That(rect.Y, Is.EqualTo(75 * RowPitch).Within(0.01));
        });
    }

    [Test]
    public void TheArrowsStepByCells()
    {
        var ic = GridOfRows();
        var panel = PanelOf(ic);
        var first = ic.ItemContainerGenerator.ContainerFromIndex(0);
        var second = ic.ItemContainerGenerator.ContainerFromIndex(1);

        Assert.Multiple(() =>
        {
            Assert.That(panel.Navigate(first, FocusNavigationDirection.Down),
                Is.SameAs(ic.ItemContainerGenerator.ContainerFromIndex(2)), "one row down is one whole line of cells");
            Assert.That(panel.Navigate(first, FocusNavigationDirection.Right), Is.SameAs(second));
            Assert.That(panel.Navigate(second, FocusNavigationDirection.Right), Is.Null, "the end of its line");
        });
    }

    [Test]
    public void NotVirtualizing_ItRealizesEveryItem()
    {
        var ic = GridOfRows(virtualizing: false);

        Assert.Multiple(() =>
        {
            Assert.That(ic.ItemContainerGenerator.RealizedIndices.Count, Is.EqualTo(Count));
            Assert.That(BoundsOf(ic, 199).X, Is.EqualTo(CellWidth + ColumnGap).Within(0.01));
        });
    }

    // Rows alone make the grid grow sideways, laid out a row at a time; a window of columns is not a run of items then,
    // so every item is realized.
    [Test]
    public void GrowingSideways_ItRealizesEveryItem()
    {
        var ic = GridOfRows(columns: 0, rows: 4);

        Assert.Multiple(() =>
        {
            Assert.That(ic.ItemContainerGenerator.RealizedIndices.Count, Is.EqualTo(Count));
            Assert.That(PanelOf(ic).EffectiveRows, Is.EqualTo(4));
            Assert.That(PanelOf(ic).EffectiveColumns, Is.EqualTo(50));
        });
    }

    [Test]
    public void UsedPlain_ItStillSharesTheSpaceBetweenItsCells()
    {
        var grid = new UniformGrid { Columns = 2 };
        for (var i = 0; i < 4; i++) grid.Children.Add(new Border());

        grid.Measure(new Size(200, 100));
        grid.Arrange(new Rect(0, 0, 200, 100));

        Assert.Multiple(() =>
        {
            Assert.That(grid.Children[3].Bounds, Is.EqualTo(new Rect(100, 50, 100, 50)));
            Assert.That(grid.CellSize, Is.EqualTo(new Size(100, 50)));
        });
    }
}
