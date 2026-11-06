using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Sandbox.Canvases.Models;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>The sandbox's graph file keeps the comment frames drawn on the plane, not only the nodes.</summary>
[TestFixture]
public class GraphFileTests
{
    [Test]
    public void FramesComeBackAsTheyWereWritten_BehindTheRest()
    {
        var scene = new CanvasScene();
        var titled = new CanvasFrameItem(new Rect(-10, -20, 300, 200), "Inputs",
            new SolidColorBrush(Color.FromRgba(10, 20, 30, 255)), new SolidColorBrush(Color.FromRgba(1, 2, 3, 40)))
        {
            Thickness = 3,
            TitleHeight = 18
        };
        var bare = new CanvasFrameItem(new Rect(400, 0, 120, 80), "Bare", null);
        var accented = new CanvasFrameItem(new Rect(0, 400, 50, 50), "Accent", new LinearGradientBrush());

        scene.Add(titled);
        scene.Add(bare);
        scene.Add(new ShapeItem(CanvasShape.Rectangle, new Rect(0, 0, 10, 10), Brushes.White, 1));
        scene.Add(accented);

        var text = GraphFile.Write(new TrackingCollection<ICanvasNode>(), scene, GraphNodeKind.Set);

        var back = new CanvasScene();
        var kept = new ShapeItem(CanvasShape.Ellipse, new Rect(0, 0, 5, 5), Brushes.White, 1);
        back.Add(new CanvasFrameItem(new Rect(0, 0, 9, 9), "Old", null));
        back.Add(kept);

        var accent = new SolidColorBrush(Color.FromRgba(200, 100, 50, 255));
        var said = GraphFile.Read(text, new TrackingCollection<ICanvasNode>(), back, GraphNodeKind.Set, accent);

        Assert.That(said, Is.Null);
        Assert.That(back.Items, Has.Count.EqualTo(4), "the old frame went, the three written came, the shape stayed");

        var first = (CanvasFrameItem)back.Items[0];
        var second = (CanvasFrameItem)back.Items[1];
        var third = (CanvasFrameItem)back.Items[2];

        Assert.Multiple(() =>
        {
            Assert.That(back.Items[3], Is.SameAs(kept), "frames go behind everything else");

            Assert.That(first.Title, Is.EqualTo("Inputs"));
            Assert.That(first.World, Is.EqualTo(new Rect(-10, -20, 300, 200)));
            Assert.That(((SolidColorBrush)first.Stroke).Color, Is.EqualTo(Color.FromRgba(10, 20, 30, 255)));
            Assert.That(((SolidColorBrush)first.Fill).Color, Is.EqualTo(Color.FromRgba(1, 2, 3, 40)));
            Assert.That(first.Thickness, Is.EqualTo(3));
            Assert.That(first.TitleHeight, Is.EqualTo(18));

            Assert.That(second.Title, Is.EqualTo("Bare"));
            Assert.That(second.Stroke, Is.Null, "no outline stays no outline");
            Assert.That(second.Fill, Is.Null);

            Assert.That(third.Title, Is.EqualTo("Accent"));
            Assert.That(((SolidColorBrush)third.Stroke).Color, Is.EqualTo(accent.Color), "other paint comes back in the accent");
            Assert.That(third.Stroke, Is.Not.SameAs(accent), "the accent is copied, not shared");
        });
    }

    [Test]
    public void AFrameMissingItsSizes_KeepsAFramesOwn()
    {
        var scene = new CanvasScene();
        var fresh = new CanvasFrameItem(new Rect(0, 0, 1, 1), string.Empty, null);

        var said = GraphFile.Read(File("""[{ "Title": "X", "Width": 100, "Height": 80 }]"""),
            new TrackingCollection<ICanvasNode>(), scene, GraphNodeKind.Set, null);

        Assert.That(said, Is.Null);
        var frame = (CanvasFrameItem)scene.Items[0];
        Assert.That(frame.Thickness, Is.EqualTo(fresh.Thickness));
        Assert.That(frame.TitleHeight, Is.EqualTo(fresh.TitleHeight));
    }

    [TestCase("""{ "Version": 4, "Set": "{set}", "Nodes": null, "Wires": [] }""")]
    [TestCase("""{ "Version": 4, "Set": "{set}", "Nodes": [null], "Wires": [] }""")]
    [TestCase("""{ "Version": 4, "Set": "{set}", "Nodes": [{ "Kind": "x", "In": null }], "Wires": [] }""")]
    [TestCase("""{ "Version": 4, "Set": "{set}", "Nodes": [], "Wires": [null] }""")]
    [TestCase("""{ "Version": 4, "Set": "{set}", "Nodes": [{ "Kind": "x", "Left": 1e400 }], "Wires": [] }""")]
    [TestCase("""{ "Version": 4, "Set": "{set}", "Nodes": [{ "Kind": "x", "Accent": [300, 0, 0, 255] }], "Wires": [] }""")]
    [TestCase("""{ "Version": 4, "Set": "{set}", "Nodes": [], "Wires": [], "Frames": [null] }""")]
    [TestCase("""{ "Version": 4, "Set": "{set}", "Nodes": [], "Wires": [], "Frames": [{ "Width": 1e400, "Height": 5 }] }""")]
    [TestCase("""{ "Version": 4, "Set": "{set}", "Nodes": [], "Wires": [], "Frames": [{ "Width": 5, "Height": 5, "Fill": [1, 2, -3, 4] }] }""")]
    public void ABrokenFile_ChangesNothing(string text)
    {
        var nodes = new TrackingCollection<ICanvasNode>();
        var node = new Canvases.ViewModels.CanvasNodeViewModel { Kind = "kept" };
        nodes.Add(node);

        var scene = new CanvasScene();
        var frame = new CanvasFrameItem(new Rect(0, 0, 9, 9), "Old", null);
        scene.Add(frame);

        var said = GraphFile.Read(text.Replace("{set}", GraphNodeKind.Set.Name), nodes, scene, GraphNodeKind.Set, null);

        Assert.That(said, Is.Not.Null);
        Assert.That(nodes, Is.EqualTo(new[] { node }));
        Assert.That(scene.Items, Is.EqualTo(new[] { frame }));
    }

    private static string File(string frames) =>
        $$"""{ "Version": 4, "Set": "{{GraphNodeKind.Set.Name}}", "Nodes": [], "Wires": [], "Frames": {{frames}} }""";

    [Test]
    public void AFileFromBeforeFrames_ReadsWithNone()
    {
        var scene = new CanvasScene();
        scene.Add(new CanvasFrameItem(new Rect(0, 0, 9, 9), "Old", null));

        var said = GraphFile.Read($$"""{ "Version": 3, "Set": "{{GraphNodeKind.Set.Name}}", "Nodes": [], "Wires": [] }""",
            new TrackingCollection<ICanvasNode>(), scene, GraphNodeKind.Set, null);

        Assert.That(said, Is.Null);
        Assert.That(scene.Items, Is.Empty);
    }
}
