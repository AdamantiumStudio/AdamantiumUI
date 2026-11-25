using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Markup;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The inspector's panel beside the plane: open, folded and hidden, pulled wider, and replaced whole by an
/// application's own.</summary>
[TestFixture]
public class CanvasInspectorPanelTests
{
    private const double Width = 1000;
    private const double Height = 700;

    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private T Built<T>(string theme) where T : InfiniteCanvas, new()
    {
        Theme made = theme switch
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
        themes.AddTheme(made.Name, made);
        themes.SetTheme(made);

        var canvas = new T { Scene = new CanvasScene() };
        canvas.ApplyCurrentTheme();
        _window = new Window { Width = Width, Height = Height, Content = canvas };
        Settle(canvas);
        return canvas;
    }

    private Window _window;

    private void Settle(InfiniteCanvas canvas, double width = Width)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(_window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
            canvas.Measure(new Size<double>(width, Height), force: true);
            canvas.Arrange(new Rect(0, 0, width, Height));
        }
    }

    private static IUIComponent Part(InfiniteCanvas canvas, string name) => (IUIComponent)canvas.GetTemplateChild(name);

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ThePanelStandsBesideThePlane_FoldsToAStrip_AndOpensAsWideAsItWas(string theme)
    {
        foreach (var canvas in new InfiniteCanvas[] { Built<DrawingBoard>(theme), Built<NodeGraph>(theme) })
        {
            var offset = canvas.Offset;
            var open = Part(canvas, "PART_Inspector").RenderSize.Width;
            var splitter = Part(canvas, "PART_InspectorSplitter");

            Assert.That(open, Is.EqualTo(canvas.InspectorWidth).Within(0.5));
            Assert.That(canvas.ViewportSize.Width, Is.EqualTo(Width - open - splitter.RenderSize.Width).Within(0.5));

            canvas.FoldInspectorCommand.Execute(null);
            Settle(canvas);
            Assert.That(canvas.IsInspectorOpen, Is.False);
            Assert.That(Part(canvas, "PART_Inspector").RenderSize.Width, Is.InRange(20, 60), "a strip");
            Assert.That(splitter.Visibility, Is.EqualTo(Visibility.Collapsed), "a strip is not pulled");
            Assert.That(canvas.Offset, Is.EqualTo(offset), "the drawing moved");

            canvas.FoldInspectorCommand.Execute(null);
            Settle(canvas);
            Assert.That(Part(canvas, "PART_Inspector").RenderSize.Width, Is.EqualTo(open).Within(0.5));

            canvas.ShowsInspector = false;
            Settle(canvas);
            Assert.That(canvas.ViewportSize.Width, Is.EqualTo(Width).Within(0.5), "hidden, it takes nothing");
        }
    }

    [Test]
    public void FoldingThatHides_TakesThePanelAway()
    {
        var canvas = Built<InfiniteCanvas>("Fluent");
        canvas.HidesFoldedInspector = true;
        canvas.FoldInspectorCommand.Execute(null);

        Assert.That(canvas.ShowsInspector, Is.False);
        Assert.That(canvas.IsInspectorOpen, Is.True, "shown again, it is open");

        canvas.FoldInspectorCommand.Execute(null);
        Assert.That(canvas.IsInspectorUnfolded, Is.True);
    }

    [Test]
    public void AWidthThatIsNoNumber_IsTheLeast()
    {
        var canvas = Built<InfiniteCanvas>("Fluent");
        canvas.InspectorWidth = double.NaN;
        Settle(canvas);

        Assert.That(Part(canvas, "PART_Inspector").RenderSize.Width, Is.EqualTo(canvas.MinInspectorWidth).Within(0.5));
        Assert.That(() => canvas.RememberLayout(), Throws.Nothing);
    }

    [Test]
    public void ThePanelIsAsWideAsItsColumnIsPulled_WithinItsLeastAndThePlanes()
    {
        var canvas = Built<InfiniteCanvas>("Fluent");
        var column = ((Grid)Part(canvas, "PART_Inspector").VisualParent).ColumnDefinitions[2];

        column.Width = new GridLength(400);   // what the splitter writes
        Settle(canvas);
        Assert.That(canvas.InspectorWidth, Is.EqualTo(400).Within(0.5));
        Assert.That(Part(canvas, "PART_Inspector").RenderSize.Width, Is.EqualTo(400).Within(0.5));

        Assert.That(column.MinWidth, Is.EqualTo(canvas.MinInspectorWidth), "the splitter stops at the least");
        Assert.That(column.MaxWidth, Is.LessThan(Width - 100), "and leaves some of the plane");
    }

    // A narrow window keeps the plane in view, and gives the panel its width back when it is wide again.
    [Test]
    public void ANarrowWindow_DoesNotForgetTheWidth()
    {
        var canvas = Built<InfiniteCanvas>("Fluent");
        foreach (var narrow in new[] { 400, 250 })
        {
            Settle(canvas, narrow);
            Assert.That(canvas.ViewportSize.Width, Is.GreaterThan(100));
        }

        Assert.That(canvas.InspectorWidth, Is.EqualTo(340));

        Settle(canvas);
        Assert.That(Part(canvas, "PART_Inspector").RenderSize.Width, Is.EqualTo(340).Within(0.5));
    }

    [Test]
    public void AnApplicationsOwnPanel_TakesThePlaceOfTheThemes()
    {
        var template = (DrawingBoard)AumlLoader.Load(
            "<DrawingBoard xmlns=\"http://adamantium/ui\" xmlns:x=\"http://adamantium/ui/xaml/extensions\">" +
            "<DrawingBoard.InspectorPanelTemplate><DataTemplate><Grid><CanvasInspector x:Name=\"Mine\"/></Grid></DataTemplate>" +
            "</DrawingBoard.InspectorPanelTemplate></DrawingBoard>").Root;
        var canvas = Built<DrawingBoard>("Fluent");
        canvas.InspectorPanelTemplate = template.InspectorPanelTemplate;
        canvas.DataContext = "the page";
        Settle(canvas);

        var panel = Part(canvas, "PART_Inspector").VisualChildren.First();
        Assert.That(panel, Is.InstanceOf<Grid>(), "the theme's panel stayed");
        Assert.That(((Grid)panel).DataContext, Is.EqualTo("the page"), "the page's data does not reach it");
        Assert.That(((CanvasInspector)panel.VisualChildren.First()).Canvas, Is.SameAs(canvas), "its inspector has no canvas");
    }

}
