using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A gallery is a grid of CHOICES: it rides the band's size ladder in columns, scrolls by whole rows, and
/// picking a cell is what states the selection.</summary>
[TestFixture]
public class RibbonGalleryTests
{
    private static RibbonGallery Gallery(int items, int columns = 4, int rows = 2)
    {
        var gallery = new RibbonGallery { Columns = columns, CompactColumns = 2, Rows = rows };
        for (var i = 0; i < items; i++) gallery.Items.Add($"item {i}");
        return gallery;
    }

    [Test]
    public void ItAnswersTheLadderInColumns()
    {
        var gallery = Gallery(12);

        Ribbon.SetSize(gallery, RibbonSize.Medium);

        Assert.Multiple(() =>
        {
            Assert.That(gallery.EffectiveColumns, Is.EqualTo(2));
            Assert.That(gallery.IsCollapsed, Is.False);
        });
    }

    [Test]
    public void AtSmallItIsTheChevronAlone()
    {
        var gallery = Gallery(12);

        Ribbon.SetSize(gallery, RibbonSize.Small);

        Assert.That(gallery.IsCollapsed, Is.True, "three thumbnails in a strip say nothing");
    }

    [Test]
    public void RowsComeFromTheColumnsCurrentlyDrawn()
    {
        var gallery = Gallery(12);

        Assert.That(gallery.RowCount, Is.EqualTo(3));

        Ribbon.SetSize(gallery, RibbonSize.Medium);

        Assert.That(gallery.RowCount, Is.EqualTo(6), "the same items, half as wide");
    }

    [Test]
    public void ARowsetThatFitsScrollsNowhere()
    {
        var gallery = Gallery(8);   // 4 columns, 2 rows shown, 2 rows of items

        Assert.Multiple(() =>
        {
            Assert.That(gallery.CanScrollUp, Is.False);
            Assert.That(gallery.CanScrollDown, Is.False);
        });
    }

    [Test]
    public void ItScrollsByWholeRowsAndStops()
    {
        var gallery = Gallery(12);   // 3 rows of items, 2 shown

        Assert.That(gallery.CanScrollDown, Is.True);

        gallery.ScrollDown();

        Assert.Multiple(() =>
        {
            Assert.That(gallery.FirstRow, Is.EqualTo(1));
            Assert.That(gallery.CanScrollUp, Is.True);
            Assert.That(gallery.CanScrollDown, Is.False, "the last row is on screen");
        });

        gallery.ScrollDown();

        Assert.That(gallery.FirstRow, Is.EqualTo(1), "there is nothing past the end to show");
    }

    [Test]
    public void NarrowingRemakesTheRowsAndPullsTheViewBack()
    {
        var gallery = Gallery(12);
        gallery.ScrollDown();

        // Wider cells, more rows - but the view must never be left past the end.
        Ribbon.SetSize(gallery, RibbonSize.Medium);

        Assert.That(gallery.FirstRow, Is.LessThanOrEqualTo(gallery.RowCount - gallery.Rows));
    }

    [Test]
    public void PickingACellStatesTheSelection()
    {
        var gallery = Gallery(12);
        var changed = 0;
        gallery.SelectionChanged += (_, _) => changed++;

        var cell = (RibbonGalleryItem)gallery.ItemContainerGenerator.Realize(2);
        gallery.PickFromContainer(cell);

        Assert.Multiple(() =>
        {
            Assert.That(gallery.SelectedItem, Is.EqualTo("item 2"));
            Assert.That(cell.IsSelected, Is.True);
            Assert.That(changed, Is.EqualTo(1));
        });
    }

    [Test]
    public void AGalleryWithAnItemCommand_RunsThePickedItem_EveryTime_AndHoldsNoChoice()
    {
        var gallery = Gallery(12);
        var ran = new List<object>();
        gallery.ItemCommand = new AdamantiumCommand<object>(ran.Add);
        gallery.IsDropDownOpen = true;

        var cell = (RibbonGalleryItem)gallery.ItemContainerGenerator.Realize(2);
        gallery.PickFromContainer(cell);
        gallery.PickFromContainer(cell);

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.EqualTo(new[] { "item 2", "item 2" }), "a second pick of the same item runs it again");
            Assert.That(gallery.SelectedItem, Is.Null);
            Assert.That(cell.IsSelected, Is.False);
            Assert.That(gallery.IsDropDownOpen, Is.False, "the choice is made");
        });
    }

    [Test]
    public void AChoiceTheItemCommandCannotRun_IsDisabled_AndNotRun()
    {
        var gallery = Gallery(12);
        var ran = 0;
        var command = new AdamantiumCommand<object>(_ => ran++, item => (string)item != "item 2");
        var cell = (RibbonGalleryItem)gallery.ItemContainerGenerator.Realize(2);
        var other = (RibbonGalleryItem)gallery.ItemContainerGenerator.Realize(3);
        gallery.ItemCommand = command;
        gallery.IsDropDownOpen = true;

        gallery.PickFromContainer(cell);

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.Zero);
            Assert.That(gallery.SelectedItem, Is.Null);
            Assert.That(cell.IsEnabled, Is.False);
            Assert.That(other.IsEnabled, Is.True);
            Assert.That(gallery.IsDropDownOpen, Is.True, "nothing was chosen");
        });
    }

    [Test]
    public void AGalleryWithAnItemCommand_DropsASelectionSetOnIt()
    {
        var gallery = Gallery(12);
        gallery.SelectedIndex = 1;

        gallery.ItemCommand = new AdamantiumCommand<object>(_ => { });
        var dropped = gallery.SelectedIndex;
        gallery.SelectedIndex = 4;
        gallery.SelectedItem = "item 5";

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.EqualTo(-1), "a selection held before the command came");
            Assert.That(gallery.SelectedIndex, Is.EqualTo(-1));
            Assert.That(gallery.SelectedItem, Is.Null);
        });
    }

    [Test]
    public void ASelectionNamedBeforeTheItemsCame_IsDroppedToo()
    {
        var gallery = new RibbonGallery { SelectedIndex = 0 };
        gallery.ItemCommand = new AdamantiumCommand<object>(_ => { });

        gallery.Items.Add("item 0");
        gallery.Items.Add("item 1");

        Assert.That(gallery.SelectedIndex, Is.EqualTo(-1));
    }

    [Test]
    public void ToAutomation_AChoiceOfAGalleryWithAnItemCommand_IsInvoked_NotSelected()
    {
        var gallery = Gallery(12);
        var ran = new List<object>();
        gallery.ItemCommand = new AdamantiumCommand<object>(ran.Add);
        var cell = (RibbonGalleryItem)gallery.ItemContainerGenerator.Realize(2);
        var peer = cell.GetAutomationPeer();

        ((IInvokeProvider)peer.GetPattern(PatternId.Invoke)).Invoke();

        Assert.Multiple(() =>
        {
            Assert.That(ran, Is.EqualTo(new[] { "item 2" }));
            Assert.That(peer.GetPattern(PatternId.SelectionItem), Is.Null);
            Assert.That(gallery.GetAutomationPeer().GetPattern(PatternId.Selection), Is.Null, "it holds no choice");
        });
    }
}

/// <summary>The panel under a gallery: equal cells in a grid, and a scroll that moves whole rows.</summary>
[TestFixture]
public class RibbonGalleryPanelTests
{
    private static RibbonGalleryPanel Panel(int cells, int columns, int rows)
    {
        var panel = new RibbonGalleryPanel { Columns = columns, Rows = rows };
        for (var i = 0; i < cells; i++) panel.Children.Add(new Border { Width = 20, Height = 10 });
        return panel;
    }

    [Test]
    public void ItAsksForTheShownCellsOnly()
    {
        var panel = Panel(12, 4, 2);

        ((IMeasurableComponent)panel).Measure(Size.Infinity);

        Assert.That(panel.DesiredSize, Is.EqualTo(new Size(80, 20)), "4 x 20 wide, 2 x 10 tall - not all three rows");
    }

    [Test]
    public void ItLaysTheCellsOutAsAGrid()
    {
        var panel = Panel(12, 4, 2);

        ((IMeasurableComponent)panel).Measure(Size.Infinity);
        ((IMeasurableComponent)panel).Arrange(new Rect(0, 0, 80, 20));

        Assert.Multiple(() =>
        {
            Assert.That(panel.Children[0].Bounds.Location, Is.EqualTo(new Vector2(0, 0)));
            Assert.That(panel.Children[3].Bounds.Location, Is.EqualTo(new Vector2(60, 0)));
            Assert.That(panel.Children[4].Bounds.Location, Is.EqualTo(new Vector2(0, 10)), "row two starts a new line");
        });
    }

    [Test]
    public void ScrollingMovesTheWholeGridUpByARow()
    {
        var panel = Panel(12, 4, 2);
        panel.FirstRow = 1;

        ((IMeasurableComponent)panel).Measure(Size.Infinity);
        ((IMeasurableComponent)panel).Arrange(new Rect(0, 0, 80, 20));

        Assert.Multiple(() =>
        {
            Assert.That(panel.Children[0].Bounds.Location, Is.EqualTo(new Vector2(0, -10)), "row one is clipped away");
            Assert.That(panel.Children[4].Bounds.Location, Is.EqualTo(new Vector2(0, 0)));
        });
    }

    [Test]
    public void RowCountFollowsTheColumns()
    {
        var panel = Panel(12, 4, 2);

        Assert.That(panel.RowCount, Is.EqualTo(3));

        panel.Columns = 3;

        Assert.That(panel.RowCount, Is.EqualTo(4));
    }
}
