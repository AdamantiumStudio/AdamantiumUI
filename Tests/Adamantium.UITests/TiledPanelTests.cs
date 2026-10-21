using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
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
    [TestCase(new[] { 0.0, 100 }, 0)]
    [TestCase(new[] { double.PositiveInfinity, 1 }, 194.04)]
    public void TheSharesReadAsPercents(double[] shares, double firstWidth)
    {
        var layout = new TileSplit(Orientation.Horizontal, new Tile(), new Tile()) { Shares = [.. shares] };
        var (_, children) = Laid(layout, 2);

        Assert.That(children[0].Bounds.Width, Is.EqualTo(firstWidth).Within(1e-9));
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
