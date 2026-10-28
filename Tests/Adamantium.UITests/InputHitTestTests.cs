using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

[TestFixture]
public class InputHitTestTests
{
    // Hit-testing must descend THROUGH a non-input container (a Border / any Decorator) to reach the interactive
    // content it wraps. The hit-test used to filter to IInputComponent children, so anything inside a Border was dead
    // to the mouse - e.g. a ScrollBar whose template root is a Border had an unclickable thumb.
    [Test]
    public void HitTest_DescendsThroughBorder_ToReachInteractiveContent()
    {
        var inner = new Button { Width = 100, Height = 50 };
        var border = new Border { Child = inner };
        var root = new Grid();
        root.Children.Add(border);

        root.Measure(new Size<double>(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));

        var hit = ((IInputComponent)root).HitTest(new Vector2<double>(50, 25));

        Assert.That(hit, Is.SameAs(inner), "the button inside the Border must be the hit target, not the Grid");
    }

    // A DISABLED CONTROL TAKES THE PRESS AND DOES NOTHING WITH IT. It used to be skipped entirely, which made it a
    // HOLE: the press went through to whatever was behind, so a grayed button sitting on a row folded the row instead,
    // and turning a control off quietly changed what the thing under it does.
    [Test]
    public void HitTest_ADisabledControlTakesThePressRatherThanLettingItThrough()
    {
        var button = new Button { Width = 100, Height = 50, IsEnabled = false };
        var root = new Grid { Background = Brushes.White };

        root.Children.Add(button);

        root.Measure(new Size<double>(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));

        var hit = ((IInputComponent)root).HitTest(new Vector2<double>(50, 25));

        Assert.That(hit, Is.SameAs(button), "the press went through the disabled button to what was behind it");
    }

    // A RENDER TRANSFORM moves what you see, so it must move what you can hit. Scaled up, the element was still
    // hit-tested where it was LAID OUT - so an infinite canvas that scales the controls it hosts could be operated at
    // 1:1 and nowhere else, and the miss grew with the distance from the element's own origin.
    [Test]
    public void HitTest_FindsAScaledElementWhereItIsDrawn()
    {
        var inner = new Button { Width = 40, Height = 20 };
        var holder = new Grid { Width = 40, Height = 20 };
        holder.Children.Add(inner);

        var root = new Grid { Width = 400, Height = 300 };
        root.Children.Add(holder);

        root.Measure(new Size<double>(400, 300));
        root.Arrange(new Rect(0, 0, 400, 300));

        // Doubled about its own top-left corner, so what was drawn at (30,10) is now drawn at (60,20).
        holder.RenderTransformOrigin = Vector2<double>.Zero;
        holder.RenderTransform = new Transform { ScaleX = 2, ScaleY = 2 };

        var at = holder.Bounds.Location;

        Assert.Multiple(() =>
        {
            Assert.That(((IInputComponent)root).HitTest(new Vector2<double>(at.X + 60, at.Y + 20)), Is.SameAs(inner),
                "the scaled element was not found where it is drawn");
            Assert.That(((IInputComponent)root).HitTest(new Vector2<double>(at.X + 90, at.Y + 50)), Is.Not.SameAs(inner),
                "it was found outside the box it is drawn in");
        });
    }

    // Zoomed by a LAYOUT transform - a document view drawn at a scale - the content is hit, and the pointer read, where
    // it is drawn: a click at the middle of a zoomed page went to the text the unzoomed page has there.
    [Test]
    public void ALayoutTransformedElement_IsHitAndPointedAtWhereItIsDrawn()
    {
        var inner = new Button { Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top };
        var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            LayoutTransform = new Transform { ScaleX = 2, ScaleY = 2 } };
        holder.Children.Add(inner);
        var root = new Grid { Width = 400, Height = 300 };
        root.Children.Add(holder);
        root.Measure(new Size<double>(400, 300));
        root.Arrange(new Rect(0, 0, 400, 300));

        var local = Pointer.ToLocal(holder, new Vector2<double>(60, 30));

        Assert.Multiple(() =>
        {
            Assert.That(((IInputComponent)root).HitTest(new Vector2<double>(70, 30)), Is.SameAs(inner), "drawn at twice its size");
            Assert.That(local.X, Is.EqualTo(30).Within(0.5), "the pointer in the content's own units");
            Assert.That(local.Y, Is.EqualTo(15).Within(0.5));
        });
    }

    // The designer's pick and a drag's auto-scroll look-up walk as hit testing does: an authored element under a layout
    // transform is found where it is drawn, not where it was laid out.
    [Test]
    public void GetVisualsAt_FindsALayoutTransformedElementWhereItIsDrawn()
    {
        var inner = new Border { Width = 40, Height = 20, Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var holder = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            LayoutTransform = new Transform { ScaleX = 2, ScaleY = 2 } };
        holder.Children.Add(inner);
        var root = new Grid { Width = 400, Height = 300 };
        root.Children.Add(holder);
        root.Measure(new Size<double>(400, 300));
        root.Arrange(new Rect(0, 0, 400, 300));

        Assert.Multiple(() =>
        {
            Assert.That(((IUIComponent)root).GetVisualsAt(new Vector2<double>(70, 30)), Does.Contain(inner),
                "drawn at twice its size");
            Assert.That(((IUIComponent)root).GetVisualsAt(new Vector2<double>(90, 50)), Does.Not.Contain(inner),
                "and nowhere else");
        });
    }

    private sealed class Root : Grid, IRootVisualComponent
    {
        public Vector2<double> PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
        public PixelPoint PointToScreen(Vector2<double> point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    // An element's own point and the desktop's go through the transforms between it and its window both ways.
    [Test]
    public void PointToScreenAndBack_GoThroughALayoutTransform()
    {
        var inner = new Border { Width = 40, Height = 20,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var holder = new Grid { Margin = new Thickness(10, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            LayoutTransform = new Transform { ScaleX = 2, ScaleY = 2 } };
        holder.Children.Add(inner);
        var root = new Root { Width = 400, Height = 300 };
        root.Children.Add(holder);
        root.Measure(new Size<double>(400, 300));
        root.Arrange(new Rect(0, 0, 400, 300));

        var screen = inner.PointToScreen(new Vector2<double>(5, 5));
        var back = inner.PointToClient(screen);

        Assert.Multiple(() =>
        {
            Assert.That(screen.X, Is.EqualTo(20).Within(0.5), "10 in, then 5 at twice the size");
            Assert.That(screen.Y, Is.EqualTo(20).Within(0.5));
            Assert.That(back.X, Is.EqualTo(5).Within(0.5));
            Assert.That(back.Y, Is.EqualTo(5).Within(0.5));
        });
    }

    // A control painted ON TOP of a full-size background panel (overlapping siblings): clicking it must hit the control,
    // not the panel behind it. (Designer repro: selecting a control kept selecting the background panel.)
    [Test]
    public void HitTest_ReturnsForegroundControl_OverBackgroundPanel()
    {
        var background = new Border { Width = 200, Height = 200 };       // full-size background "panel"
        var foreground = new Button { Width = 100, Height = 50 };        // control on top (added later = painted over)
        var root = new Grid();
        root.Children.Add(background);
        root.Children.Add(foreground);

        root.Measure(new Size<double>(200, 200));
        root.Arrange(new Rect(0, 0, 200, 200));

        var hit = ((IInputComponent)root).HitTest(new Vector2<double>(50, 25));   // inside the foreground button

        Assert.That(hit, Is.SameAs(foreground), "the foreground control must be hit, not the background panel behind it");
    }

    // A control inside a panel that itself fills the window: clicking the control must hit it, not the containing panel.
    [Test]
    public void HitTest_ReturnsChildControl_NotContainingPanel()
    {
        var button = new Button { Width = 100, Height = 50 };
        var panel = new StackPanel();
        panel.Children.Add(button);
        var root = new Grid();
        root.Children.Add(panel);

        root.Measure(new Size<double>(300, 300));
        root.Arrange(new Rect(0, 0, 300, 300));

        var hit = ((IInputComponent)root).HitTest(new Vector2<double>(50, 25));   // inside the button

        Assert.That(hit, Is.SameAs(button), "the child control must be hit, not its containing panel");
    }

    // A CENTERED shape over a full-size background panel: clicking the shape must hit it, not the panel. (Designer repro:
    // a centered shape - a rounded rect "pill" - kept selecting the background panel instead of itself.)
    [Test]
    public void HitTest_ReturnsCenteredShape_OverBackgroundPanel()
    {
        var background = new Border { Width = 200, Height = 200 };
        var shape = new Adamantium.UI.Controls.Shapes.Rectangle
        {
            Width = 80, Height = 40, Fill = Brushes.Red,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var root = new Grid();
        root.Children.Add(background);
        root.Children.Add(shape);

        root.Measure(new Size<double>(200, 200));
        root.Arrange(new Rect(0, 0, 200, 200));

        var hit = ((IInputComponent)root).HitTest(new Vector2<double>(100, 100));   // the center = center of the centered shape

        Assert.That(hit, Is.SameAs(shape), "the centered shape must be hit, not the background panel behind it");
    }

    // A Border is itself an input control now, so the mouse HitTest lands ON it (no fall-through to the panel behind).
    // GetVisualsAt likewise returns it: the designer selects the authored Border under the pointer, not the background
    // panel. (HitTest returns the topmost IInputComponent; GetVisualsAt returns any visual on its geometry - here the same
    // Border, since it is both.)
    [Test]
    public void GetVisualsAt_AndHitTest_SelectTheAuthoredBorder()
    {
        var border = new Border
        {
            Width = 280, Height = 155,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var panel = new RenderTargetPanel();   // the user's root container (a Grid that composites a surface)
        panel.Children.Add(border);

        panel.Measure(new Size<double>(1280, 720));
        panel.Arrange(new Rect(0, 0, 1280, 720));

        var p = new Vector2<double>(640, 360);   // dead center = inside the centered Border
        var inputHit = ((IInputComponent)panel).HitTest(p);
        var visualHit = ((IUIComponent)panel).GetVisualsAt(p).FirstOrDefault();

        Assert.Multiple(() =>
        {
            Assert.That(inputHit, Is.SameAs(border), "the Border is an input control, so the mouse HitTest lands on it");
            Assert.That(visualHit, Is.SameAs(border), "the designer's visual hit-test also selects the Border");
        });
    }

    private static void PointAt(IInputComponent component) => PointerInput.Enter(component);

    // A cursor that changes while the pointer STANDS STILL has to reach the screen. Anything that decides its cursor
    // from where inside itself the pointer is - a column separator, a splitter, a resize grip - changes it without any
    // enter to carry it, and the pointer then lies about what a press will do: the grid's header showed the resize
    // cursor several pixels past the separator, and the press there reordered the column instead.
    [Test]
    public void ACursorChangedUnderAStillPointer_ReachesTheScreen()
    {
        var border = new Border { Width = 100, Height = 50 };
        PointAt(border);

        border.Cursor = Cursors.SizeEWE;
        Assert.That(Mouse.Cursor, Is.EqualTo(Cursors.SizeEWE), "the element under the pointer says what the pointer is");

        border.Cursor = Cursors.Arrow;
        Assert.That(Mouse.Cursor, Is.EqualTo(Cursors.Arrow), "and it goes back without waiting for the next enter");
    }

    // LEAVING a grip puts the pointer back over what held it, and that is not an enter - the parent never left. Without
    // somebody speaking for it, the double arrow a panel's resize edge put up stayed on the screen while the pointer
    // was already over the rows.
    [Test]
    public void LeavingAGrip_PutsBackTheCursorOfWhatHeldIt()
    {
        var panel = new Border { Width = 200, Height = 100, Cursor = Cursors.Arrow };
        var grip = new Border { Width = 7, Height = 100, Cursor = Cursors.SizeEWE };
        panel.Child = grip;

        panel.Measure(new Size<double>(200, 100), force: true);
        panel.Arrange(new Rect(0, 0, 200, 100));

        PointAt(grip);
        Assert.That(Mouse.Cursor, Is.EqualTo(Cursors.SizeEWE), "the grip never said what it was");

        PointerInput.Exit(grip);

        Assert.That(Mouse.Cursor, Is.EqualTo(Cursors.Arrow), "the pointer is over the panel and still wearing the grip");
    }

    // ...but only the element the pointer is ON may speak. Otherwise any control anywhere - a row realized off screen,
    // a template part being built - would grab the cursor while the pointer is nowhere near it.
    [Test]
    public void ACursorChangedAwayFromThePointer_ChangesNothing()
    {
        var hovered = new Border();
        PointAt(hovered);
        hovered.Cursor = Cursors.Hand;

        var elsewhere = new Border();
        elsewhere.Cursor = Cursors.SizeNS;

        Assert.That(Mouse.Cursor, Is.EqualTo(Cursors.Hand), "the pointer is still over the first one");
    }

    // A pointer coming over an element and its ancestors at once - from a side panel straight onto a view that fills
    // its document - shows the element's cursor: the ancestors entered with it spoke last and put up their arrow.
    [Test]
    public void EnteringAnElementWithItsAncestors_ShowsTheElementsCursor()
    {
        var root = new Grid();
        var side = new Border();
        var document = new Border { Cursor = Cursors.Arrow };
        var scene = new Border { Cursor = Cursors.Crosshair };
        document.Child = scene;
        root.Children.Add(side);
        root.Children.Add(document);

        AncestorState.Transition(null, side, PointerEvents.PointerEnteredEvent, PointerEvents.PointerExitedEvent,
            evt => new PointerEventArgs(Mouse.Pointer, InputModifiers.None, 0) { RoutedEvent = evt });
        AncestorState.Transition(side, scene, PointerEvents.PointerEnteredEvent, PointerEvents.PointerExitedEvent,
            evt => new PointerEventArgs(Mouse.Pointer, InputModifiers.None, 0) { RoutedEvent = evt });

        Assert.That(Mouse.Cursor, Is.EqualTo(Cursors.Crosshair));
    }

    // An ancestor can be told the same pointer entered twice: a hovered subtree taken out of the tree cuts the chain the
    // next move compares against. One exit must still clear it, or hover stays lit after the pointer has gone.
    [Test]
    public void AnEnterToldTwice_IsUndoneByOneExit()
    {
        var panel = new Border();

        PointerInput.Enter(panel);
        PointerInput.Enter(panel);
        PointerInput.Exit(panel);

        Assert.That(panel.IsPointerOver, Is.False);
    }
}
