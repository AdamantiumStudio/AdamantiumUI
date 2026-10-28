using System.Linq;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core;
using Adamantium.UITests.Graph;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The scene and its history are the page's and outlive the canvas showing them: a page left and opened again
/// is a new canvas over the same drawing. An undo there puts back the items the history holds, so those are the ones the
/// new canvas has to show - not a second set beside them, and not the old canvas's discarded controls.</summary>
[TestFixture]
public class CanvasRevisitHistoryTests
{
    private static InfiniteCanvas Show(CanvasScene scene, CanvasHistory history,
        TrackingCollection<ICanvasNode> nodes, TrackingCollection<ICanvasObject> objects)
    {
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, History = history };
        canvas.Measure(new Size<double>(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));

        canvas.Scene = scene;
        canvas.Nodes = nodes;
        canvas.Objects = objects;

        return canvas;
    }

    private static void Leave(InfiniteCanvas canvas)
    {
        DiscardedVisuals.Publish(canvas);
        while (DiscardedVisuals.Drain(64) > 0) { }
    }

    private static GraphNode Made(string kind, double x)
    {
        var node = new GraphNode { Kind = kind, Title = kind, Left = x, Top = 0 };
        node.Inputs.Add(new GraphSocket { Name = "In" });
        node.Outputs.Add(new GraphSocket(GraphNode.Branches) { Name = "Out" });

        return node;
    }

    private static ElementItem ItemOf(CanvasScene scene, ICanvasPlaced model) =>
        scene.Items.OfType<ElementItem>().Single(item => ReferenceEquals(item.Model, model));

    [Test]
    public void AClearUndoneOnTheNextCanvas_PutsEachNodeBackOnceAndLive()
    {
        var scene = new CanvasScene();
        var history = new CanvasHistory();
        var nodes = new TrackingCollection<ICanvasNode>();
        var objects = new TrackingCollection<ICanvasObject>();
        var left = Made("a", 0);
        var right = Made("b", 300);
        nodes.Add(left);
        nodes.Add(right);
        _ = new CanvasConnection(left.Outputs[0], right.Inputs[0]);

        var first = Show(scene, history, nodes, objects);
        history.Record(scene, "Clear", scene.Clear);
        Leave(first);

        Show(scene, history, nodes, objects);
        history.Undo(scene);

        var shown = scene.Items.OfType<ElementItem>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(shown, Has.Count.EqualTo(2), "a node is on the plane twice");
            Assert.That(shown.All(item => item.Element is CanvasNode { IsDiscarded: false }), Is.True,
                "a node came back wearing the old canvas's discarded control");
            Assert.That(left.Outputs[0].Connections, Has.Count.EqualTo(1), "the wire did not come back into the graph");
        });
    }

    [Test]
    public void AWireKeptByTheHistory_EndsOnTheNewControlsSockets()
    {
        var scene = new CanvasScene();
        var history = new CanvasHistory();
        var nodes = new TrackingCollection<ICanvasNode>();
        var objects = new TrackingCollection<ICanvasObject>();
        var left = Made("a", 0);
        var right = Made("b", 300);
        nodes.Add(left);
        nodes.Add(right);
        _ = new CanvasConnection(left.Outputs[0], right.Inputs[0]);

        var first = Show(scene, history, nodes, objects);
        history.Record(scene, "Clear", scene.Clear);
        Leave(first);

        var second = Show(scene, history, nodes, objects);
        history.Undo(scene);

        var wire = scene.Items.OfType<ConnectionItem>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(wire.FromPin, Is.SameAs(((CanvasNode)second.ItemOf(left).Element).OutputPins[0]));
            Assert.That(wire.ToPin, Is.SameAs(((CanvasNode)second.ItemOf(right).Element).InputPins[0]));
        });
    }

    // A canvas is torn down when its discard is drained, which can be after the page has been opened again: the new one
    // has the scene by then, and the old one must not take the nodes off it or the controls off them.
    [Test]
    public void ACanvasTornDownAfterTheNextTookItsScene_LeavesTheNextItsNodes()
    {
        var scene = new CanvasScene();
        var history = new CanvasHistory();
        var nodes = new TrackingCollection<ICanvasNode>();
        var objects = new TrackingCollection<ICanvasObject>();
        var left = Made("a", 0);
        var right = Made("b", 300);
        nodes.Add(left);
        nodes.Add(right);
        _ = new CanvasConnection(left.Outputs[0], right.Inputs[0]);

        var first = Show(scene, history, nodes, objects);
        var second = Show(scene, history, nodes, objects);
        Leave(first);

        var shown = scene.Items.OfType<ElementItem>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(shown, Has.Count.EqualTo(2), "a node is on the plane twice, or went with the old canvas");
            Assert.That(shown.All(item => item.Element is CanvasNode { IsDiscarded: false } &&
                                          ReferenceEquals(item, second.ItemOf((ICanvasNode)item.Model))), Is.True,
                "the new canvas lost its node controls to the old one");
            Assert.That(scene.Items.OfType<ConnectionItem>().Count(), Is.EqualTo(1), "the wire is drawn twice");
            Assert.That(left.Outputs[0].Connections, Has.Count.EqualTo(1), "the graph's wire was doubled or cut");
        });
    }

    // ...nor go on laying the scene out in its own layers: the controls there would be the new canvas's, pulled into a
    // tree that is on its way out.
    [Test]
    public void ACanvasWhoseSceneWasTaken_PutsNoneOfTheNextOnesControlsInItsLayers()
    {
        var scene = new CanvasScene();
        var history = new CanvasHistory();
        var nodes = new TrackingCollection<ICanvasNode>();
        nodes.Add(Made("a", 0));
        nodes.Add(Made("b", 300));

        var first = Templated(scene, history, nodes);
        var second = Templated(scene, history, nodes);
        scene.Touch();
        Lay(second);
        first.InvalidateArrange();
        Lay(first);

        Assert.That(scene.Items.OfType<ElementItem>().All(item =>
                ((IUIComponent)item.Element).VisualParent is CanvasElementLayer layer && ReferenceEquals(layer.Owner, second)),
            Is.True);
    }

    // ...nor come back to it when the application hands it another collection before it is torn down.
    [Test]
    public void ACanvasWhoseSceneWasTaken_TakesNothingBackWhenHandedNodes()
    {
        var scene = new CanvasScene();
        var history = new CanvasHistory();
        var nodes = new TrackingCollection<ICanvasNode>();
        var node = Made("a", 0);
        nodes.Add(node);

        var first = Show(scene, history, nodes, new TrackingCollection<ICanvasObject>());
        var second = Show(scene, history, nodes, new TrackingCollection<ICanvasObject>());
        first.Nodes = new TrackingCollection<ICanvasNode> { node };

        Assert.Multiple(() =>
        {
            Assert.That(scene.Items.OfType<ElementItem>().Count(), Is.EqualTo(1), "the node is on the plane twice");
            Assert.That(ItemOf(scene, node).Element, Is.SameAs(second.ItemOf(node).Element).And.Not.Null,
                "the old canvas took the node's control back");
        });
    }

    // A canvas moved to another drawing leaves the first one's items behind without its controls - and a canvas that
    // takes the first drawing later takes nothing from the second.
    [Test]
    public void ACanvasMovedToAnotherScene_LeavesTheFirstToTheNextCanvasAndKeepsTheSecond()
    {
        var first = new CanvasScene();
        var other = new CanvasScene();
        var history = new CanvasHistory();
        var nodes = new TrackingCollection<ICanvasNode>();
        var node = Made("a", 0);
        nodes.Add(node);
        var mine = new TrackingCollection<ICanvasNode>();
        var kept = Made("b", 0);
        mine.Add(kept);

        var moved = Show(first, history, nodes, new TrackingCollection<ICanvasObject>());
        moved.Scene = other;
        moved.Nodes = mine;
        var next = Show(first, history, nodes, new TrackingCollection<ICanvasObject>());

        Assert.Multiple(() =>
        {
            Assert.That(first.Items.OfType<ElementItem>().Count(), Is.EqualTo(1), "the first drawing holds a node twice");
            Assert.That(ItemOf(first, node).Element, Is.SameAs(next.ItemOf(node).Element).And.Not.Null);
            Assert.That(ItemOf(other, kept).Element, Is.InstanceOf<CanvasNode>().And.Property("IsDiscarded").False,
                "the moved canvas lost the drawing it shows now");
        });
    }

    // A canvas whose scene was taken, put on screen again, takes it back - the one on screen shows the drawing.
    [Test]
    public void ACanvasShownAgain_TakesItsSceneBack()
    {
        var scene = new CanvasScene();
        var history = new CanvasHistory();
        var nodes = new TrackingCollection<ICanvasNode>();
        var node = Made("a", 0);
        nodes.Add(node);

        var first = Show(scene, history, nodes, new TrackingCollection<ICanvasObject>());
        var window = new Window { Width = 800, Height = 600, Content = first };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        var second = Show(scene, history, nodes, new TrackingCollection<ICanvasObject>());

        window.Content = new Adamantium.UI.Controls.Decorators.Border();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        window.Content = first;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Items.OfType<ElementItem>().Count(), Is.EqualTo(1), "the node is on the plane twice");
            Assert.That(ItemOf(scene, node).Element, Is.SameAs(first.ItemOf(node).Element).And.Not.Null,
                "the canvas on screen does not show the node");
            Assert.That(second.ItemOf(node), Is.Null, "the other canvas still holds it");
        });
    }

    [Test]
    public void AnItemNoCanvasShows_IsNotSelected()
    {
        var canvas = new InfiniteCanvas();
        var bare = new ElementItem(null, new Rect(0, 0, 50, 50));
        var shown = new ElementItem(new Adamantium.UI.Controls.Buttons.Button(), new Rect(100, 0, 50, 50));

        canvas.SelectMany([bare, shown], false);
        canvas.Select(bare, true);

        Assert.That(canvas.Selection, Is.EqualTo(new[] { shown }));
    }

    [Test]
    public void AnItemNoCanvasShows_CannotBePicked()
    {
        var item = new ElementItem(null, new Rect(0, 0, 100, 50));

        Assert.Multiple(() =>
        {
            Assert.That(item.HitTest(new Vector2<double>(50, 25), 0), Is.False);
            Assert.That(item.Touches(new Rect(0, 0, 200, 200)), Is.False);
        });
    }

    private static InfiniteCanvas Templated(CanvasScene scene, CanvasHistory history, TrackingCollection<ICanvasNode> nodes)
    {
        var canvas = new InfiniteCanvas
        {
            Mode = CanvasMode.Nodes,
            Template = new Adamantium.UI.Core.Templates.ControlTemplate(() =>
            {
                var layers = new Adamantium.UI.Controls.Panels.Grid();
                var front = new CanvasFrontLayer();
                var root = new Adamantium.UI.Controls.Panels.Grid();
                root.Children.Add(layers);
                root.Children.Add(front);

                var result = new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = root };
                result.RegisterName("PART_Layers", layers);
                result.RegisterName("PART_Front", front);
                return result;
            }),
            History = history,
            Width = 800,
            Height = 600
        };

        canvas.Scene = scene;
        canvas.Nodes = nodes;
        Lay(canvas);

        return canvas;
    }

    private static void Lay(InfiniteCanvas canvas)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            canvas.Measure(new Size<double>(800, 600), force: true);
            canvas.Arrange(new Rect(0, 0, 800, 600));
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(canvas);
        }
    }

    [Test]
    public void ANodeLeftByItsCanvas_KeepsNoControl()
    {
        var scene = new CanvasScene();
        var nodes = new TrackingCollection<ICanvasNode>();
        var node = Made("a", 0);
        nodes.Add(node);

        var canvas = Show(scene, new CanvasHistory(), nodes, new TrackingCollection<ICanvasObject>());
        var item = ItemOf(scene, node);
        Leave(canvas);

        Assert.That(item.Element, Is.Null);
    }

    [Test]
    public void AClearUndoneOnTheNextCanvas_PutsEachObjectBackOnceAndLive()
    {
        var scene = new CanvasScene();
        var history = new CanvasHistory();
        var nodes = new TrackingCollection<ICanvasNode>();
        var objects = new TrackingCollection<ICanvasObject>();
        var model = new PlacedObject { Left = 10, Top = 20, Width = 100, Height = 60, Content = "plain" };
        objects.Add(model);

        var first = Show(scene, history, nodes, objects);
        history.Record(scene, "Clear", scene.Clear);
        Leave(first);

        Show(scene, history, nodes, objects);
        history.Undo(scene);

        var shown = scene.Items.OfType<ElementItem>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(shown, Has.Count.EqualTo(1), "the object is on the plane twice");
            Assert.That(shown[0].Element, Is.InstanceOf<ContentPresenter>().And.Property("IsDiscarded").False,
                "the object came back wearing the old canvas's discarded control");
        });
    }
}
