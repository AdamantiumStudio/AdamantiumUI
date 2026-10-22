using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The tiles a <see cref="TiledPanel"/> lays its children in: as the tree of its layout says, a tile by name
/// or in order, a flow for the rest, one alone when maximized; the gaps of the splits drag, those in line together.</summary>
[TestFixture]
public class TiledPanelTests
{
    [TearDown]
    public void LetGo() => Mouse.Pointer.Capture(null);

    [Test]
    public void FourTilesTwoByTwo_InTheChildrensOrder_TheGapsBetweenThem()
    {
        var (_, children) = Laid(TileLayouts.Four, 4);

        Assert.Multiple(() =>
        {
            Assert.That(children[0].Bounds, Is.EqualTo(new Rect(0, 0, 98, 48)));
            Assert.That(children[1].Bounds, Is.EqualTo(new Rect(102, 0, 98, 48)));
            Assert.That(children[2].Bounds, Is.EqualTo(new Rect(0, 52, 98, 48)));
            Assert.That(children[3].Bounds, Is.EqualTo(new Rect(102, 52, 98, 48)));
        });
    }

    [Test]
    public void AChildNoTileTakes_IsCollapsed()
    {
        var (_, children) = Laid(TileLayouts.TwoColumns, 3);

        Assert.Multiple(() =>
        {
            Assert.That(children[1].Bounds, Is.EqualTo(new Rect(102, 0, 98, 100)));
            Assert.That(children[2].Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public void TheSharesSizeTheParts()
    {
        var layout = new TileSplit(Orientation.Horizontal, new Tile(), new Tile()) { Shares = [25, 75] };
        var (_, children) = Laid(layout, 2);

        Assert.That(children[0].Bounds, Is.EqualTo(new Rect(0, 0, 49, 100)));
    }

    [Test]
    public void ANamedTile_TakesTheChildOfItsName_TheRestFlowInOrder()
    {
        var (panel, children) = Laid(TileLayouts.FeaturedBottom, 4);
        TiledPanel.SetTile(children[2], TileLayouts.FeaturedTile);
        Lay(panel);

        Assert.Multiple(() =>
        {
            Assert.That(children[2].Bounds.Width, Is.EqualTo(200));
            Assert.That(children[2].Bounds.Height, Is.EqualTo(76.8).Within(1e-9));
            Assert.That(children[0].Bounds.X, Is.EqualTo(0));
            Assert.That(children[1].Bounds.X, Is.EqualTo(68));
            Assert.That(children[3].Bounds.X, Is.EqualTo(136));
            Assert.That(children[3].Bounds.Y, Is.EqualTo(80.8).Within(1e-9));
        });
    }

    [Test]
    public void ANamedTileNoChildIsNamedFor_TakesTheNextInOrder()
    {
        var (_, children) = Laid(TileLayouts.FeaturedBottom, 3);

        Assert.That(children[0].Bounds.Height, Is.EqualTo(76.8).Within(1e-9));
    }

    [Test]
    public void AFlow_TakesTheColumnsThatGiveTheTilesTheMostRoom()
    {
        var (_, children) = Laid(TileLayouts.Gallery, 6);

        Assert.Multiple(() =>
        {
            Assert.That(children[0].Bounds, Is.EqualTo(new Rect(0, 0, 64, 48)));
            Assert.That(children[5].Bounds, Is.EqualTo(new Rect(136, 52, 64, 48)));
        });
    }

    [Test]
    public void AFlow_CollapsesTheChildrenPastItsLimits()
    {
        var (_, children) = Laid(new TileFlow { MaxColumns = 2, MaxRows = 1 }, 3);

        Assert.Multiple(() =>
        {
            Assert.That(children[1].Bounds, Is.EqualTo(new Rect(102, 0, 98, 100)));
            Assert.That(children[2].Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCase(HorizontalAlignment.Left, 0, 68, 64)]
    [TestCase(HorizontalAlignment.Center, 34, 102, 64)]
    [TestCase(HorizontalAlignment.Right, 68, 136, 64)]
    [TestCase(HorizontalAlignment.Stretch, 0, 102, 98)]
    public void AShortLastRow_StandsAsItsAlignmentSays(HorizontalAlignment alignment, double first, double second, double width)
    {
        var (_, children) = Laid(new TileFlow { MaxColumns = 3, LastRowAlignment = alignment }, 5);

        Assert.Multiple(() =>
        {
            Assert.That(children[3].Bounds.X, Is.EqualTo(first).Within(1e-9));
            Assert.That(children[4].Bounds.X, Is.EqualTo(second).Within(1e-9));
            Assert.That(children[4].Bounds.Width, Is.EqualTo(width).Within(1e-9));
        });
    }

    [Test]
    public void AFlowWithAnAspectRatio_KeepsItCentered()
    {
        var (_, children) = Laid(new TileFlow { AspectRatio = 1 }, 1);

        Assert.That(children[0].Bounds, Is.EqualTo(new Rect(50, 0, 100, 100)));
    }

    [Test]
    public void MeasuredWithoutBounds_AsInAScrollViewer_AFlowStillShowsItsTiles()
    {
        var (panel, children) = Made(TileLayouts.Gallery, 2);

        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity), force: true);
        panel.Arrange(new Rect(0, 0, 200, 100));

        Assert.Multiple(() =>
        {
            Assert.That(children[1].Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(children[1].Bounds, Is.EqualTo(new Rect(102, 0, 98, 100)));
        });
    }

    [Test]
    public void TheMaximizedItem_FillsThePanelAlone_ByItsData()
    {
        var (panel, children) = Laid(TileLayouts.Four, 4);
        children[2].DataContext = "front";

        panel.MaximizedItem = "front";
        Lay(panel);

        Assert.Multiple(() =>
        {
            Assert.That(children[2].Bounds, Is.EqualTo(new Rect(0, 0, 200, 100)));
            Assert.That(children[0].Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(children[3].Visibility, Is.EqualTo(Visibility.Collapsed));
        });

        panel.MaximizedItem = null;
        Lay(panel);

        Assert.That(children[0].Visibility, Is.EqualTo(Visibility.Visible), "and back to the four");
    }

    [Test]
    public void SharedEvenly_TheTilesAreAlikeAgain()
    {
        var layout = TileLayouts.FourLeft;
        var (panel, children) = Laid(layout, 4);

        panel.ShareEvenly();
        Lay(panel);

        Assert.That(children[0].Bounds, Is.EqualTo(new Rect(0, 0, 98, 100)));
    }

    [Test]
    public void DraggingAGap_MovesTheGapsInLineWithIt()
    {
        var (panel, children, window) = InWindow(TileLayouts.Four, 4);

        Press(panel, new Vector2(100, 20));
        MoveTo(panel, new Vector2(150, 20));
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(children[0].Bounds.Width, Is.EqualTo(148).Within(1e-9));
            Assert.That(children[2].Bounds.Width, Is.EqualTo(148).Within(1e-9), "the bottom row's gap went along");
            Assert.That(children[0].Bounds.Height, Is.EqualTo(48).Within(1e-9), "the other gap stayed");
        });
    }

    [Test]
    public void DoubleClickingAGap_SharesItsSplitsEvenly()
    {
        var layout = TileLayouts.FourLeft;
        var (panel, children, window) = InWindow(layout, 4);
        var gapX = children[0].Bounds.Right + 2;

        Press(panel, new Vector2(gapX, 50), clicks: 2);
        Update(window);

        Assert.That(children[0].Bounds.Width, Is.EqualTo(98).Within(1e-9));
    }

    [Test]
    public void DoubleClickingAGap_LeavesTheSplitsInsideAlone()
    {
        var (panel, children, window) = InWindow(TileLayouts.Four, 4);
        Press(panel, new Vector2(100, 20));
        MoveTo(panel, new Vector2(150, 20));
        Release(panel);
        Update(window);

        Press(panel, new Vector2(20, 50), clicks: 2);
        Update(window);

        Assert.That(children[0].Bounds.Width, Is.EqualTo(148).Within(1e-9));
    }

    [Test]
    public void AGapDraggedWhileThePanelGrows_FollowsThePointer()
    {
        var (panel, children, window) = InWindow(TileLayouts.TwoColumns, 2);

        Press(panel, new Vector2(100, 20));
        panel.Width = 400;
        Update(window);
        MoveTo(panel, new Vector2(150, 20));
        Release(panel);
        Update(window);

        Assert.That(children[0].Bounds.Width, Is.EqualTo(148).Within(1e-9));
    }

    [TestCase(new[] { 60.0 }, 117.6)]
    [TestCase(new[] { double.PositiveInfinity, 1 }, 194.04)]
    public void TheSharesReadAsPercents(double[] shares, double firstWidth)
    {
        var layout = new TileSplit(Orientation.Horizontal, new Tile(), new Tile()) { Shares = [.. shares] };
        var (_, children) = Laid(layout, 2);

        Assert.That(children[0].Bounds.Width, Is.EqualTo(firstWidth).Within(1e-9));
    }

    [Test]
    public void APartWithNoShare_LaysOutNothing()
    {
        var layout = new TileSplit(Orientation.Horizontal, new Tile(), new Tile()) { Shares = [0, 100] };
        var (_, children) = Laid(layout, 2);

        Assert.Multiple(() =>
        {
            Assert.That(children[0].Visibility, Is.EqualTo(Visibility.Collapsed), "nothing of it shows, its content neither");
            Assert.That(children[1].Bounds.Width, Is.EqualTo(196).Within(1e-9));
        });
    }

    [Test]
    public void APartRemoved_TakesItsShareAlong()
    {
        var layout = new TileSplit(Orientation.Horizontal, new Tile(), new Tile(), new Tile()) { Shares = [20, 30, 50] };
        layout.Children.RemoveAt(0);
        var (_, children) = Laid(layout, 2);

        Assert.That(children[0].Bounds.Width, Is.EqualTo(73.5).Within(1e-9));
    }

    [Test]
    public void AFlowWithLittleRoom_StillGivesItsTilesSome()
    {
        var (panel, children) = Made(new TileFlow(), 2);
        panel.Spacing = 25;
        panel.Measure(new Size(30, 5), force: true);
        panel.Arrange(new Rect(0, 0, 30, 5));

        Assert.That(children[1].Bounds, Is.EqualTo(new Rect(27.5, 0, 2.5, 5)));
    }

    [Test]
    public void AChildTakenOut_IsNoLongerCollapsed()
    {
        var (panel, children) = Laid(TileLayouts.One, 2);

        panel.Children.Remove(children[1]);

        Assert.That(children[1].Visibility, Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void ADropGap_TakesTheLiftedTile_AndTheOthersMoveAside()
    {
        var (panel, containers) = Hosted(TileLayouts.Four, "A", "B", "C", "D");

        panel.ShowDropGap(0, new List<object> { "C" });
        LayHosted(panel);

        Assert.Multiple(() =>
        {
            Assert.That(containers["C"].Visibility, Is.EqualTo(Visibility.Collapsed), "the dragged tile leaves its place");
            Assert.That(containers["A"].Bounds, Is.EqualTo(new Rect(102, 0, 98, 48)));
            Assert.That(containers["B"].Bounds, Is.EqualTo(new Rect(0, 52, 98, 48)));
            Assert.That(containers["D"].Bounds, Is.EqualTo(new Rect(102, 52, 98, 48)));
        });

        panel.ShowDropGap(-1, null);
        LayHosted(panel);

        Assert.That(containers["C"].Bounds, Is.EqualTo(new Rect(0, 52, 98, 48)), "closed, it is back");
    }

    [TestCase(20, 20, 0)]
    [TestCase(150, 20, 1)]
    [TestCase(150, 75, 4)]
    public void TheDropSlot_IsTheGapItself_OrPastTheGapTheTileAfter(double x, double y, int expected)
    {
        var (panel, _) = Hosted(TileLayouts.Four, "A", "B", "C", "D");
        panel.ShowDropGap(0, new List<object> { "C" });
        LayHosted(panel);

        Assert.That(panel.TryGetDropSlot(new Vector2(x, y), new List<object> { "C" }, out var index), Is.True);
        Assert.That(index, Is.EqualTo(expected));
    }

    [TestCase(150, 75, 4)]
    [TestCase(20, 75, 3)]
    [TestCase(20, 20, 1)]
    public void BeforeTheGapOpens_TheDropSlotAlreadyLeavesTheLiftedTileOut(double x, double y, int expected)
    {
        var (panel, _) = Hosted(TileLayouts.Four, "A", "B", "C", "D");

        Assert.That(panel.TryGetDropSlot(new Vector2(x, y), new List<object> { "A" }, out var index), Is.True);
        Assert.That(index, Is.EqualTo(expected));
    }

    [Test]
    public void ANamedTileWhoseChildIsLifted_StaysEmpty()
    {
        var (panel, containers) = Hosted(TileLayouts.FeaturedBottom, "F", "B", "C", "D");
        TiledPanel.SetTile(containers["F"], TileLayouts.FeaturedTile);
        LayHosted(panel);

        panel.ShowDropGap(3, new List<object> { "F" });
        LayHosted(panel);

        Assert.Multiple(() =>
        {
            Assert.That(containers["F"].Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(containers["B"].Bounds.Height, Is.EqualTo(19.2).Within(1e-9), "no strip tile takes the large one");
        });
    }

    [Test]
    public void DraggingACornerInward_SplitsANewTileOff_ForTheItemTheViewModelGives()
    {
        var items = new ObservableCollection<string> { "A" };
        var (panel, window) = InWindowList(TileLayouts.One, items,
            p => p.SplitTileCommand = new Act(at => items.Insert((int)at, "N")));

        PressCorner(panel, new Vector2(2, 2));
        MoveTo(panel, new Vector2(60, 50));
        MoveTo(panel, new Vector2(60, 50));
        Update(window);
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.EqualTo(new[] { "N", "A" }), "the new tile is on the side of the corner");
            Assert.That(ContainerOf(panel, "N").Bounds.Width, Is.EqualTo(58).Within(1e-9));
            Assert.That(ContainerOf(panel, "A").Bounds.Width, Is.EqualTo(138).Within(1e-9));
        });
    }

    [Test]
    public void OverATile_GripsMarkItsCorners_TheOneUnderThePointerHot()
    {
        var items = new ObservableCollection<string> { "A", "B" };
        var (panel, window) = InWindowList(TileLayouts.TwoColumns, items, p => p.SplitTileCommand = new Act(_ => { }));

        HoverAt(panel, new Vector2(195, 95));
        Update(window);

        var grips = panel.VisualChildren.OfType<TileGrip>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(grips, Has.Count.EqualTo(4));
            Assert.That(grips.Single(g => g.Corner == TileCorner.TopLeft).Bounds, Is.EqualTo(new Rect(102, 0, 16, 16)));
            Assert.That(grips.Single(g => g.Corner == TileCorner.BottomRight).Bounds, Is.EqualTo(new Rect(184, 84, 16, 16)));
            Assert.That(grips.Single(g => g.IsHot).Corner, Is.EqualTo(TileCorner.BottomRight));
        });

        HoverAt(panel, new Vector2(150, 50));
        Update(window);

        Assert.That(panel.VisualChildren.OfType<TileGrip>().Any(g => g.IsHot), Is.False, "away from the corners, none is hot");
    }

    [Test]
    public void ASplitNoItemArrivesFor_IsUndone()
    {
        var items = new ObservableCollection<string> { "A" };
        var (panel, window) = InWindowList(TileLayouts.One, items, p => p.SplitTileCommand = new Act(_ => { }));

        PressCorner(panel, new Vector2(2, 2));
        MoveTo(panel, new Vector2(60, 50));
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(((TileSplit)panel.Layout).Children, Has.Count.EqualTo(1));
            Assert.That(ContainerOf(panel, "A").Bounds.Width, Is.EqualTo(200));
        });
    }

    [Test]
    public void ASplitDraggedBackToItsEdge_IsGoneAtOnce_AndLetGoAsksTheViewModelNothing()
    {
        var items = new ObservableCollection<string> { "A" };
        var asked = false;
        var (panel, window) = InWindowList(TileLayouts.One, items, p => p.SplitTileCommand = new Act(_ => asked = true));

        PressCorner(panel, new Vector2(2, 2));
        MoveTo(panel, new Vector2(60, 50));
        MoveTo(panel, new Vector2(0, 50));
        Update(window);
        var heldWidth = ContainerOf(panel, "A").Bounds.Width;
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(heldWidth, Is.EqualTo(200), "while held, the old tile already has the room back");
            Assert.That(asked, Is.False);
            Assert.That(((TileSplit)panel.Layout).Children, Has.Count.EqualTo(1));
            Assert.That(ContainerOf(panel, "A").Bounds.Width, Is.EqualTo(200));
        });
    }

    [Test]
    public void ASplitDraggedBackToItsEdgeAndInAgain_IsMadeAfterAll()
    {
        var items = new ObservableCollection<string> { "A" };
        var (panel, window) = InWindowList(TileLayouts.One, items,
            p => p.SplitTileCommand = new Act(at => items.Insert((int)at, "N")));

        PressCorner(panel, new Vector2(2, 2));
        MoveTo(panel, new Vector2(60, 50));
        MoveTo(panel, new Vector2(0, 50));
        MoveTo(panel, new Vector2(60, 50));
        Update(window);
        Release(panel);
        Update(window);

        Assert.That(items, Is.EqualTo(new[] { "N", "A" }));
    }

    [Test]
    public void ASplitFromARightCornerDraggedBackToTheRightEdge_SplitsNothing()
    {
        var items = new ObservableCollection<string> { "A" };
        var asked = false;
        var (panel, window) = InWindowList(TileLayouts.One, items, p => p.SplitTileCommand = new Act(_ => asked = true));

        PressCorner(panel, new Vector2(198, 2));
        MoveTo(panel, new Vector2(140, 50));
        MoveTo(panel, new Vector2(200, 50));
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.False);
            Assert.That(((TileSplit)panel.Layout).Children, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ACornerFirstDraggedOutward_SplitsTheWayItIsThenDraggedIn()
    {
        var items = new ObservableCollection<string> { "A" };
        var (panel, window) = InWindowList(TileLayouts.One, items,
            p => p.SplitTileCommand = new Act(at => items.Insert((int)at, "N")));

        PressCorner(panel, new Vector2(2, 2));
        MoveTo(panel, new Vector2(-20, 2));
        MoveTo(panel, new Vector2(2, 60));
        Update(window);
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.EqualTo(new[] { "N", "A" }));
            Assert.That(ContainerOf(panel, "N").Bounds, Is.EqualTo(new Rect(0, 0, 200, 58)), "the new tile on top");
        });
    }

    [Test]
    public void AGapDraggedAllTheWayOverATile_MergesItAway()
    {
        var items = new ObservableCollection<string> { "A", "B" };
        var (panel, window) = InWindowList(TileLayouts.TwoColumns, items,
            p => p.MergeTileCommand = new Act(item => items.Remove((string)item)));

        Press(panel, new Vector2(100, 20));
        MoveTo(panel, new Vector2(199, 20));
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.EqualTo(new[] { "A" }));
            Assert.That(((TileSplit)panel.Layout).Children, Has.Count.EqualTo(1), "the root stays the tree it was");
            Assert.That(ContainerOf(panel, "A").Bounds.Width, Is.EqualTo(200));
        });
    }

    [Test]
    public void ASplitReleasedBeforeALayout_StillHandsTheRightIndex()
    {
        var items = new ObservableCollection<string> { "A", "B" };
        var handed = -1;
        var (panel, _) = InWindowList(TileLayouts.TwoColumns, items, p => p.SplitTileCommand = new Act(at =>
        {
            handed = (int)at;
            items.Insert((int)at, "N");
        }));

        PressCorner(panel, new Vector2(198, 2));
        MoveTo(panel, new Vector2(150, 50));
        Release(panel);

        Assert.Multiple(() =>
        {
            Assert.That(handed, Is.EqualTo(2), "the new tile right of B takes the place after it");
            Assert.That(items, Is.EqualTo(new[] { "A", "B", "N" }));
        });
    }

    [Test]
    public void AMergeTheViewModelAnswersWithAnotherItem_KeepsTheTile()
    {
        var items = new ObservableCollection<string> { "A", "B", "C" };
        var (panel, window) = InWindowList(TileLayouts.TwoColumns, items, p => p.MergeTileCommand = new Act(_ => items.Remove("C")));

        Press(panel, new Vector2(100, 20));
        MoveTo(panel, new Vector2(199, 20));
        Release(panel);
        Update(window);

        Assert.That(((TileSplit)panel.Layout).Children, Has.Count.EqualTo(2));
    }

    [Test]
    public void AMergeCutShort_GivesTheSharesBack()
    {
        var items = new ObservableCollection<string> { "A", "B" };
        var (panel, window) = InWindowList(TileLayouts.TwoColumns, items, p => p.MergeTileCommand = new Act(_ => { }));

        Press(panel, new Vector2(100, 20));
        MoveTo(panel, new Vector2(199, 20));
        Mouse.Pointer.Capture(null);
        Update(window);

        Assert.That(ContainerOf(panel, "B").Bounds.Width, Is.EqualTo(98).Within(1e-9));
    }

    [Test]
    public void EscapeDuringASplit_UndoesIt_AndAsksTheViewModelNothing()
    {
        var items = new ObservableCollection<string> { "A" };
        var asked = false;
        var (panel, window) = InWindowList(TileLayouts.One, items, p => p.SplitTileCommand = new Act(_ => asked = true));

        PressCorner(panel, new Vector2(2, 2));
        MoveTo(panel, new Vector2(60, 50));
        Update(window);
        PressEscape(window);
        MoveTo(panel, new Vector2(80, 50));
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.False);
            Assert.That(((TileSplit)panel.Layout).Children, Has.Count.EqualTo(1));
            Assert.That(ContainerOf(panel, "A").Bounds.Width, Is.EqualTo(200));
        });
    }

    [Test]
    public void EscapeDuringAMerge_GivesTheSharesBack_AndKeepsTheTile()
    {
        var items = new ObservableCollection<string> { "A", "B" };
        var (panel, window) = InWindowList(TileLayouts.TwoColumns, items,
            p => p.MergeTileCommand = new Act(item => items.Remove((string)item)));

        Press(panel, new Vector2(100, 20));
        MoveTo(panel, new Vector2(199, 20));
        PressEscape(window);
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(items, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(ContainerOf(panel, "B").Bounds.Width, Is.EqualTo(98).Within(1e-9));
        });
    }

    [Test]
    public void EscapeOutsideAGesture_IsLeftToOthers()
    {
        var items = new ObservableCollection<string> { "A", "B" };
        var (panel, window) = InWindowList(TileLayouts.TwoColumns, items, p => p.MergeTileCommand = new Act(_ => { }));

        Assert.That(PressEscape(window), Is.False, "before any drag");

        Press(panel, new Vector2(100, 20));
        MoveTo(panel, new Vector2(120, 20));
        Release(panel);

        Assert.That(PressEscape(window), Is.False, "after a drag let go");
    }

    [Test]
    public void AnEscapeSomethingElseTook_LeavesTheGestureGoing()
    {
        var items = new ObservableCollection<string> { "A", "B" };
        var (panel, window) = InWindowList(TileLayouts.TwoColumns, items,
            p => p.MergeTileCommand = new Act(item => items.Remove((string)item)));
        ((IObservableComponent)window).AddHandler(Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler((_, e) => e.Handled = true));

        Press(panel, new Vector2(100, 20));
        MoveTo(panel, new Vector2(199, 20));
        PressEscape(window);
        Release(panel);

        Assert.That(items, Is.EqualTo(new[] { "A" }), "the popup closed by that Escape is all it undoes");
    }

    [Test]
    public void WhereGapsCross_APressDragsThem_AndSplitsNothing()
    {
        var items = new ObservableCollection<string> { "A", "B", "C", "D" };
        var (panel, window) = InWindowList(TileLayouts.Four, items, p => p.SplitTileCommand = new Act(at => items.Insert((int)at, "N")));

        PressCorner(panel, new Vector2(97, 47));

        Assert.That(items, Has.Count.EqualTo(4));
        Press(panel, new Vector2(100, 50));
        MoveTo(panel, new Vector2(150, 60));
        Release(panel);
        Update(window);

        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(4), "no split");
            Assert.That(ContainerOf(panel, "A").Bounds.Width, Is.EqualTo(148).Within(1e-9));
            Assert.That(ContainerOf(panel, "A").Bounds.Height, Is.EqualTo(58).Within(1e-9));
        });
    }

    [Test]
    public void WithoutAMergeCommand_AGapStopsShortOfTheTile()
    {
        var items = new ObservableCollection<string> { "A", "B" };
        var (panel, window) = InWindowList(TileLayouts.TwoColumns, items, _ => { });

        Press(panel, new Vector2(100, 20));
        MoveTo(panel, new Vector2(199, 20));
        Release(panel);
        Update(window);

        Assert.That(ContainerOf(panel, "B").Bounds.Width, Is.EqualTo(32).Within(1e-9));
    }

    [Test]
    public void AChangeOfTheLayout_LaysTheTilesOutAgain()
    {
        var layout = new TileSplit(Orientation.Horizontal, new Tile(), new Tile());
        var (_, children, window) = InWindow(layout, 2);

        layout.Shares = [25, 75];
        Update(window);

        Assert.That(children[0].Bounds.Width, Is.EqualTo(49).Within(1e-9));
    }

    private static (TiledPanel Panel, Border[] Children) Laid(TileRegion layout, int count)
    {
        var (panel, children) = Made(layout, count);
        Lay(panel);
        return (panel, children);
    }

    private static (TiledPanel Panel, Border[] Children, Window Window) InWindow(TileRegion layout, int count)
    {
        var (panel, children) = Made(layout, count);
        panel.Width = 200;
        panel.Height = 100;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        panel.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = 600, Height = 300, Content = panel };
        Update(window);
        return (panel, children, window);
    }

    private static (TiledPanel Panel, Border[] Children) Made(TileRegion layout, int count)
    {
        var panel = new TiledPanel { Layout = layout };
        var children = new Border[count];
        for (var i = 0; i < count; i++)
        {
            children[i] = new Border();
            panel.Children.Add(children[i]);
        }

        return (panel, children);
    }

    private static (TiledPanel Panel, Dictionary<string, IUIComponent> Containers) Hosted(TileRegion layout,
        params string[] items)
    {
        var list = new ItemsControl
        {
            ItemsSource = items.ToList(),
            ItemsPanel = new ItemsPanelTemplate(() => new TemplateResult { RootComponent = new TiledPanel { Layout = layout } }),
            Template = new ControlTemplate(() =>
            {
                var presenter = new ItemsPresenter();
                var result = new TemplateResult { RootComponent = presenter };
                result.RegisterName("PART_ItemsPresenter", presenter);
                return result;
            })
        };
        list.Measure(new Size(200, 100));
        list.Arrange(new Rect(0, 0, 200, 100));
        var panel = (TiledPanel)list.ItemsHostPanel;
        var containers = panel.Children.Cast<IUIComponent>()
            .ToDictionary(c => (string)((IFundamentalUIComponent)c).DataContext);
        return (panel, containers);
    }

    private static (TiledPanel Panel, Window Window) InWindowList(TileRegion layout, ObservableCollection<string> items,
        Action<TiledPanel> setUp)
    {
        TiledPanel panel = null;
        var list = new ItemsControl
        {
            ItemsSource = items,
            Width = 200,
            Height = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ItemsPanel = new ItemsPanelTemplate(() =>
            {
                panel = new TiledPanel { Layout = layout };
                setUp(panel);
                return new TemplateResult { RootComponent = panel };
            }),
            Template = new ControlTemplate(() =>
            {
                var presenter = new ItemsPresenter();
                var result = new TemplateResult { RootComponent = presenter };
                result.RegisterName("PART_ItemsPresenter", presenter);
                return result;
            })
        };
        var window = new Window { Width = 600, Height = 300, Content = list };
        Update(window);
        return (panel, window);
    }

    private static IUIComponent ContainerOf(TiledPanel panel, string item) =>
        panel.Children.Cast<IUIComponent>().First(c => (string)((IFundamentalUIComponent)c).DataContext == item);

    private static void PressCorner(TiledPanel panel, Vector2 at)
    {
        PointerDevice.Current.SetExternalMousePosition(panel, new PixelPoint(at.X, at.Y));
        ((IObservableComponent)panel).RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left,
            InputModifiers.LeftMouseButton, 0)
        {
            RoutedEvent = PointerEvents.PreviewPointerPressedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.LeftMouseButton)
        });
    }

    private static void HoverAt(TiledPanel panel, Vector2 at)
    {
        PointerDevice.Current.SetExternalMousePosition(panel, new PixelPoint(at.X, at.Y));
        ((IObservableComponent)panel).RaiseEvent(new PointerEventArgs(Mouse.Pointer, InputModifiers.None, 0)
        {
            RoutedEvent = PointerEvents.PreviewPointerMovedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
    }

    private sealed class Act(Action<object> run) : Adamantium.Core.Commands.ICommand
    {
        public bool CanExecute(object parameter = null) => true;

        public void Execute(object parameter = null) => run(parameter);

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public void RaiseCanExecuteChanged()
        {
        }
    }

    private static void LayHosted(TiledPanel panel)
    {
        panel.Measure(new Size(200, 100), true);
        panel.Arrange(new Rect(0, 0, 200, 100), true);
    }

    private static void Lay(TiledPanel panel)
    {
        panel.Measure(new Size(200, 100), force: true);
        panel.Arrange(new Rect(0, 0, 200, 100));
    }

    private static void Update(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static void Press(TiledPanel panel, Vector2 at, int clicks = 1)
    {
        PointerDevice.Current.SetExternalMousePosition(panel, new PixelPoint(at.X, at.Y));
        ((IObservableComponent)panel).RaiseEvent(new PointerPressedEventArgs(Mouse.Pointer, MouseButtons.Left,
            InputModifiers.LeftMouseButton, 0)
        {
            RoutedEvent = PointerEvents.PointerPressedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.LeftMouseButton),
            ClickCount = clicks
        });
    }

    private static void MoveTo(TiledPanel panel, Vector2 at)
    {
        PointerDevice.Current.SetExternalMousePosition(panel, new PixelPoint(at.X, at.Y));
        ((IObservableComponent)panel).RaiseEvent(new PointerEventArgs(Mouse.Pointer, InputModifiers.LeftMouseButton, 0)
        {
            RoutedEvent = PointerEvents.PointerMovedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.LeftMouseButton)
        });
    }

    private static bool PressEscape(Window window)
    {
        var args = new KeyEventArgs(KeyboardDevice.CurrentDevice, Key.Escape, InputModifiers.None, 0)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        ((IObservableComponent)window).RaiseEvent(args);
        return args.Handled;
    }

    private static void Release(TiledPanel panel)
    {
        ((IObservableComponent)panel).RaiseEvent(new PointerReleasedEventArgs(Mouse.Pointer, MouseButtons.Left,
            InputModifiers.None, 0)
        {
            RoutedEvent = PointerEvents.PointerReleasedEvent,
            Properties = PointerPointProperties.FromMouse(InputModifiers.None)
        });
    }
}
