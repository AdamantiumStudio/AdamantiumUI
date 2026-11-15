using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A pane along a whole side of the canvas: a side panel that splits the viewport, the plane and every other
/// pane beside it, rather than a plate standing over the plane.</summary>
[TestFixture]
public class CanvasSidePaneTests
{
    private const double WindowWidth = 1000;
    private const double WindowHeight = 700;

    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void Use(string name)
    {
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static InfiniteCanvas CreateCanvasWithSide(CanvasPanePlacement side, out CanvasPane pane, out Window window)
    {
        var canvas = new InfiniteCanvas { Scene = new CanvasScene() };
        canvas.ApplyCurrentTheme();
        window = new Window { Width = WindowWidth, Height = WindowHeight, Content = canvas };
        Lay(window, canvas);

        pane = canvas.GetTemplateChild("PART_Inspector") as CanvasPane;
        Assert.That(pane, Is.Not.Null, "the chrome carries no inspector pane");
        pane.IsWanted = true;
        pane.IsNeeded = true;
        pane.Placement = side;
        pane.Width = 300;
        Lay(window, canvas);
        return canvas;
    }

    [TestCase(CanvasPanePlacement.LeftSide)]
    [TestCase(CanvasPanePlacement.RightSide)]
    public void ASidePaneStandsFlushAlongTheWholeSide(CanvasPanePlacement side)
    {
        Use("Fluent");
        var canvas = CreateCanvasWithSide(side, out var pane, out _);

        var width = pane.RenderSize.Width;
        var expectedX = side == CanvasPanePlacement.LeftSide ? 0 : canvas.RenderSize.Width - width;
        Assert.Multiple(() =>
        {
            Assert.That(width, Is.EqualTo(300).Within(0.5), "the width it was given");
            Assert.That(pane.Bounds.X, Is.EqualTo(expectedX).Within(0.5), "it stood off its edge");
            Assert.That(pane.Bounds.Y, Is.EqualTo(0).Within(0.5), "it stood below the top");
            Assert.That(pane.RenderSize.Height, Is.EqualTo(canvas.RenderSize.Height).Within(0.5),
                "it was not as tall as the canvas");
        });
    }

    [TestCase(CanvasPanePlacement.LeftSide)]
    [TestCase(CanvasPanePlacement.RightSide)]
    public void ThePlaneIsLookedAtBesideASidePane(CanvasPanePlacement side)
    {
        Use("Fluent");
        var canvas = CreateCanvasWithSide(side, out var pane, out _);

        var usable = canvas.UsableBounds;
        var width = pane.RenderSize.Width;
        Assert.Multiple(() =>
        {
            if (side == CanvasPanePlacement.LeftSide)
            {
                Assert.That(usable.X, Is.EqualTo(width).Within(0.5), "the room seen through starts under the pane");
            }
            else
            {
                Assert.That(usable.X + usable.Width, Is.EqualTo(canvas.RenderSize.Width - width).Within(0.5),
                    "the room seen through runs under the pane");
            }
        });
    }

    // Every pane placed against an edge or a corner stands in the room left beside the side, as it would against the
    // canvas's own edge: the inset is kept from the side pane, not from under it.
    [Test]
    public void TheOtherPanesStandBesideASidePane()
    {
        Use("Fluent");
        var canvas = CreateCanvasWithSide(CanvasPanePlacement.LeftSide, out var side, out var window);
        var corner = canvas.GetTemplateChild("PART_MiniMap") as CanvasPane;
        Assert.That(corner, Is.Not.Null, "the chrome carries no map pane");
        corner.IsWanted = true;
        corner.IsNeeded = true;
        corner.Placement = CanvasPanePlacement.BottomLeft;
        Lay(window, canvas);

        var layer = side.Layer;
        Assert.That(corner.Visibility, Is.EqualTo(Visibility.Visible), "the map was not shown");
        Assert.That(corner.Bounds.X, Is.EqualTo(side.RenderSize.Width + layer.PaneInset.Left).Within(0.5),
            "a corner pane stood over the side pane, or not its inset off it");
    }

    // A toolbar docked along the top grows as wide as it needs; a pane in a top corner starts below it rather than
    // standing over it.
    [Test]
    public void APaneInATopCornerStartsBelowTheBarDockedAlongTheTop()
    {
        Use("Fluent");
        var canvas = CreateCanvasWithSide(CanvasPanePlacement.RightSide, out var side, out var window);
        side.Placement = CanvasPanePlacement.TopRight;
        var bar = canvas.GetTemplateChild("PART_MiniMap") as CanvasPane;
        Assert.That(bar, Is.Not.Null, "the chrome carries no map pane");
        bar.IsWanted = true;
        bar.IsNeeded = true;
        bar.Placement = CanvasPanePlacement.TopCenter;
        bar.IsDocked = true;
        Lay(window, canvas);

        var layer = side.Layer;
        Assert.That(side.Bounds.Y, Is.EqualTo(layer.PaneInset.Top + bar.RenderSize.Height + layer.PaneGap).Within(0.5),
            "the corner's pane stood over the bar along the top");
    }

    [TestCase(CanvasPanePlacement.LeftSide)]
    [TestCase(CanvasPanePlacement.RightSide)]
    public void ASidePaneThatIsNotWantedTakesNoRoom(CanvasPanePlacement side)
    {
        Use("Fluent");
        var canvas = CreateCanvasWithSide(side, out var pane, out var window);
        pane.IsWanted = false;
        Lay(window, canvas);

        var usable = canvas.UsableBounds;
        Assert.Multiple(() =>
        {
            Assert.That(usable.X, Is.EqualTo(0).Within(0.5), "a pane switched off walled off the left");
            Assert.That(usable.Width, Is.EqualTo(canvas.RenderSize.Width).Within(0.5), "a pane switched off took width");
        });
    }

    // A side of the canvas, not a plate on it: no handles to carry or fold it by, square, unlifted, bordered only where
    // it meets the plane, and its body the whole height.
    [TestCase("Fluent", CanvasPanePlacement.LeftSide)]
    [TestCase("Graphite", CanvasPanePlacement.LeftSide)]
    [TestCase("MacOs", CanvasPanePlacement.LeftSide)]
    [TestCase("Fluent", CanvasPanePlacement.RightSide)]
    [TestCase("Graphite", CanvasPanePlacement.RightSide)]
    [TestCase("MacOs", CanvasPanePlacement.RightSide)]
    public void EveryThemeDrawsASidePaneAsASide(string theme, CanvasPanePlacement side)
    {
        Use(theme);
        var canvas = CreateCanvasWithSide(side, out var pane, out _);

        var handles = pane.GetTemplateChild("PART_Handles") as IUIComponent;
        var plate = pane.GetTemplateChild("PART_Plate") as Border;
        var room = pane.GetTemplateChild("PART_BodyRoom") as IUIComponent;
        var inner = side == CanvasPanePlacement.LeftSide ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(handles?.Visibility, Is.EqualTo(Visibility.Collapsed), "a side pane offered its handles");
            Assert.That(plate?.CornerRadius, Is.EqualTo(new CornerRadius(0)), "a side pane was rounded");
            Assert.That(plate?.BorderThickness, Is.EqualTo(inner), "a side pane was bordered away from the plane");
            Assert.That(plate?.Shadow, Is.Null, "a side pane was lifted off the canvas");
            Assert.That(room?.RenderSize.Height, Is.GreaterThan(canvas.RenderSize.Height / 2),
                "the body did not run the height of the side");
        });
    }

    private static void Lay(Window window, InfiniteCanvas canvas)
    {
        Settle(window);

        for (var pass = 0; pass < 2; pass++)
        {
            canvas.Measure(new Size<double>(WindowWidth, WindowHeight), force: true);
            canvas.Arrange(new Rect(0, 0, WindowWidth, WindowHeight));
        }

        Settle(window);
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }
}
