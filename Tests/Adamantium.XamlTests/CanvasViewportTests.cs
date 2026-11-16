using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The part of a canvas's template the plane is looked at through: a template that stands something beside
/// the plane names it <c>PART_Viewport</c>, and the camera, the screen points and the chrome are all measured in it.
/// </summary>
[TestFixture]
public class CanvasViewportTests
{
    private const double WindowWidth = 1000;
    private const double WindowHeight = 700;
    private const double Beside = 200;

    private const string Namespaces =
        "xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\"";

    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void UseFluent()
    {
        Theme theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static InfiniteCanvas CreateCanvasWithStripBeside()
    {
        var result = AumlLoader.Load(
            $"<InfiniteCanvas {Namespaces}><InfiniteCanvas.Template><ControlTemplate TargetType=\"InfiniteCanvas\">" +
            $"<Grid ColumnDefinitions=\"{Beside},*\"><Border/>" +
            "<Grid x:Name=\"PART_Viewport\" Grid.Column=\"1\"><Grid x:Name=\"PART_Layers\"/>" +
            "<CanvasFrontLayer x:Name=\"PART_Front\"/><CanvasChromeLayer x:Name=\"PART_Chrome\"/></Grid>" +
            "</Grid></ControlTemplate></InfiniteCanvas.Template></InfiniteCanvas>");
        Assert.That(result.Root, Is.InstanceOf<InfiniteCanvas>(), string.Join(" | ", result.Diagnostics));
        return (InfiniteCanvas)result.Root;
    }

    [Test]
    public void AViewportPart_IsWhatThePlaneIsLookedAtThrough()
    {
        UseFluent();
        var canvas = CreateCanvasWithStripBeside();
        canvas.Scene = new CanvasScene();
        Lay(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.ViewportElement, Is.Not.SameAs(canvas), "the template's viewport was not taken");
            Assert.That(canvas.ViewportSize.Width, Is.EqualTo(WindowWidth - Beside).Within(0.5));
            Assert.That(canvas.ViewportSize.Height, Is.EqualTo(WindowHeight).Within(0.5));
            Assert.That(canvas.ViewportOrigin.X, Is.EqualTo(Beside).Within(0.5), "the viewport's place in the canvas");
            Assert.That(canvas.UsableBounds.Width, Is.EqualTo(WindowWidth - Beside).Within(0.5),
                "the usable room reached under the strip beside the plane");
            Assert.That(canvas.Offset.X, Is.EqualTo((WindowWidth - Beside) / 2).Within(0.5),
                "the world's origin did not start in the middle of the viewport");
        });
    }

    [Test]
    public void ACanvasWithoutAViewportPart_IsItsOwnViewport()
    {
        UseFluent();
        var canvas = new InfiniteCanvas { Scene = new CanvasScene() };
        canvas.ApplyCurrentTheme();
        Lay(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.ViewportElement, Is.SameAs(canvas));
            Assert.That(canvas.ViewportSize.Width, Is.EqualTo(WindowWidth).Within(0.5));
            Assert.That(canvas.ViewportOrigin, Is.EqualTo(Vector2<double>.Zero));
        });
    }

    // A toolbar docked along the top grows as wide as it needs; a pane in a top corner starts below it rather than
    // standing over it.
    [Test]
    public void APaneInATopCornerStartsBelowTheBarDockedAlongTheTop()
    {
        UseFluent();
        var canvas = new InfiniteCanvas { Scene = new CanvasScene() };
        canvas.ApplyCurrentTheme();
        var window = Lay(canvas);
        var corner = canvas.GetTemplateChild("PART_Inspector") as CanvasPane;
        var bar = canvas.GetTemplateChild("PART_MiniMap") as CanvasPane;
        Assert.That(corner, Is.Not.Null, "the chrome carries no inspector pane");
        Assert.That(bar, Is.Not.Null, "the chrome carries no map pane");
        corner.IsWanted = true;
        corner.IsNeeded = true;
        corner.Placement = CanvasPanePlacement.TopRight;
        bar.IsWanted = true;
        bar.IsNeeded = true;
        bar.Placement = CanvasPanePlacement.TopCenter;
        bar.IsDocked = true;
        Relay(window, canvas);

        var layer = corner.Layer;
        Assert.That(corner.Bounds.Y, Is.EqualTo(layer.PaneInset.Top + bar.RenderSize.Height + layer.PaneGap).Within(0.5),
            "the corner's pane stood over the bar along the top");
    }

    private static Window Lay(InfiniteCanvas canvas)
    {
        var window = new Window { Width = WindowWidth, Height = WindowHeight, Content = canvas };
        Relay(window, canvas);
        return window;
    }

    private static void Relay(Window window, InfiniteCanvas canvas)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
            canvas.Measure(new Size<double>(WindowWidth, WindowHeight), force: true);
            canvas.Arrange(new Rect(0, 0, WindowWidth, WindowHeight));
        }
    }
}
