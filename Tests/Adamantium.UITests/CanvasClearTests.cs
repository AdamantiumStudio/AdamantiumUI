using System;
using System.Linq;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UITests.Graph;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>EMPTYING THE PLANE. The one action most worth being able to take back, and the one that touches every
/// bookkeeping there is at once: the scene, the graph, the objects, the wires and the history.</summary>
public class CanvasClearTests
{
    private static (NodeGraph Canvas, CanvasScene Scene, TrackingCollection<ICanvasNode> Nodes) Stage()
    {
        var canvas = Sized(new NodeGraph());
        var scene = new CanvasScene();
        var nodes = new TrackingCollection<ICanvasNode>();

        canvas.Scene = scene;
        canvas.Nodes = nodes;

        return (canvas, scene, nodes);
    }

    private static (DrawingBoard Canvas, CanvasScene Scene, TrackingCollection<ICanvasObject> Objects) Board()
    {
        var canvas = Sized(new DrawingBoard());
        var scene = new CanvasScene();
        var objects = new TrackingCollection<ICanvasObject>();

        canvas.Scene = scene;
        canvas.Objects = objects;

        return (canvas, scene, objects);
    }

    private static T Sized<T>(T canvas) where T : InfiniteCanvas
    {
        canvas.Measure(new Size<double>(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));

        return canvas;
    }

    private static GraphNode Node(TrackingCollection<ICanvasNode> nodes, double left)
    {
        var node = new GraphNode { Left = left, Width = 160 };

        node.InputCount = 1;
        node.OutputCount = 1;

        nodes.Add(node);

        return node;
    }

    // MANY AT ONCE is one change of the plane, not one per item: an item at a time was a walk of the scene each, and of
    // everything that follows it - deleting or pasting a large selection took quadratic time.
    private static (NodeGraph Canvas, CanvasScene Scene, TrackingCollection<ICanvasNode> Nodes, GraphNode Last) Chain(int count)
    {
        var (canvas, scene, nodes) = Stage();
        GraphNode last = null;
        for (var i = 0; i < count; i++)
        {
            var node = Node(nodes, i * 200);
            if (last != null) _ = new CanvasConnection(last.Outputs[0], node.Inputs[0]);
            last = node;
        }

        return (canvas, scene, nodes, last);
    }

    [Test]
    public void DeletingManyNodes_ChangesThePlaneOnce_AndCutsTheirWires()
    {
        var (canvas, scene, _, last) = Chain(30);
        var lastItem = canvas.ItemOf(last);
        var chosen = new System.Collections.Generic.List<ICanvasItem>();
        foreach (var item in scene.Items)
        {
            if (item is ElementItem && !ReferenceEquals(item, lastItem)) chosen.Add(item);
        }

        canvas.SelectMany(chosen, false);
        var changes = 0;
        scene.Changed += (_, _) => changes++;

        canvas.DeleteSelection();

        Assert.Multiple(() =>
        {
            Assert.That(scene.Items, Is.EqualTo(new ICanvasItem[] { lastItem }), "the nodes or their wires stayed");
            Assert.That(changes, Is.LessThanOrEqualTo(3), "the plane was changed once for each item");
            Assert.That(last.Inputs[0].Connections, Is.Empty, "the graph kept the wire to a deleted node");
            Assert.That(((CanvasNode)lastItem.Element).InputPins[0].IsConnected, Is.False, "the freed socket is taken");
        });
    }

    [Test]
    public void DuplicatingMany_ChangesThePlaneOnce_WiredAsTheyWere()
    {
        var (canvas, scene, _, _) = Chain(30);
        canvas.SelectMany(new System.Collections.Generic.List<ICanvasItem>(scene.Items), false);
        var changes = 0;
        scene.Changed += (_, _) => changes++;

        canvas.Duplicate();

        Assert.Multiple(() =>
        {
            Assert.That(scene.Items, Has.Count.EqualTo(2 * (30 + 29)), "the copies or their wires are missing");
            Assert.That(changes, Is.LessThanOrEqualTo(3), "the plane was changed once for each item");
        });
    }

    // A CLEAR reported as one removal of every object takes them off in one change too.
    [Test]
    public void ACollectionCleared_TakesItsObjectsOffAtOnce()
    {
        var (_, scene, objects) = Board();
        for (var i = 0; i < 30; i++) objects.Add(new PlacedObject { Left = i * 50, Width = 40, Height = 30 });
        var changes = 0;
        scene.Changed += (_, _) => changes++;

        objects.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(scene.Items, Is.Empty);
            Assert.That(changes, Is.EqualTo(1), "the plane was changed once for each object");
        });
    }

    // MANY INSIDE A GROUP, deleted at once: out of the group in one walk, and the group gone with its last.
    [Test]
    public void DeletingAllOfAGroupFromInside_TakesTheGroupToo()
    {
        var (canvas, scene, _) = Board();
        var members = new System.Collections.Generic.List<ICanvasItem>();
        for (var i = 0; i < 10; i++)
        {
            members.Add(new ShapeItem(CanvasShape.Rectangle, new Rect(i * 50, 0, 40, 30), Brushes.White, 1));
        }

        var group = new GroupItem(members);
        scene.Add(group);
        canvas.SelectMany(members, false);

        canvas.DeleteSelection();

        Assert.Multiple(() =>
        {
            Assert.That(group.Children, Is.Empty);
            Assert.That(scene.Items, Is.Empty, "the empty group stayed on the plane");
        });
    }

    // A GROUP OF NODES deleted takes the wires of the nodes inside it.
    [Test]
    public void DeletingAGroupOfNodes_TakesTheirWires()
    {
        var (canvas, scene, nodes) = Stage();
        var a = Node(nodes, 0);
        var b = Node(nodes, 200);
        var c = Node(nodes, 400);
        _ = new CanvasConnection(a.Outputs[0], c.Inputs[0]);
        canvas.SelectMany([canvas.ItemOf(a), canvas.ItemOf(b)], false);
        var group = canvas.GroupSelection();
        canvas.SelectMany([group], false);

        canvas.DeleteSelection();

        Assert.Multiple(() =>
        {
            Assert.That(scene.Items.OfType<ConnectionItem>(), Is.Empty, "the wire of a node in the group stayed");
            Assert.That(c.Inputs[0].Connections, Is.Empty);
        });
    }

    // A GROUP INSIDE A GROUP left empty by a delete goes too, not an invisible box in the paint order.
    [Test]
    public void DeletingAllOfAGroupInsideAGroup_TakesThatGroupToo()
    {
        var (canvas, scene, _) = Board();
        var inner = new ShapeItem(CanvasShape.Rectangle, new Rect(0, 0, 40, 30), Brushes.White, 1);
        var kept = new ShapeItem(CanvasShape.Rectangle, new Rect(100, 0, 40, 30), Brushes.White, 1);
        var nested = new GroupItem([inner]);
        var outer = new GroupItem([nested, kept]);
        scene.Add(outer);
        canvas.SelectMany([inner], false);

        canvas.DeleteSelection();

        Assert.That(outer.Children, Is.EqualTo(new ICanvasItem[] { kept }), "the empty group stayed inside");
    }

    // AN OBJECT PUT BACK whose item an undo has put back on the plane already stands there once.
    [Test]
    public void AnObjectPutBack_WithItsItemOnThePlane_StandsOnce()
    {
        var (_, scene, objects) = Board();
        var model = new PlacedObject { Left = 0, Width = 40, Height = 30 };
        objects.Add(model);
        var item = scene.Items.Single();

        objects.Remove(model);
        scene.Add(item);
        objects.Add(model);

        Assert.That(scene.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void ACollectionReset_TakesItsObjectsOffAtOnce()
    {
        var (canvas, scene, _) = Board();
        var objects = new System.Collections.ObjectModel.ObservableCollection<ICanvasObject>();
        canvas.Objects = objects;
        for (var i = 0; i < 30; i++) objects.Add(new PlacedObject { Left = i * 50, Width = 40, Height = 30 });
        var changes = 0;
        scene.Changed += (_, _) => changes++;

        objects.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(scene.Items, Is.Empty);
            Assert.That(changes, Is.EqualTo(1), "the plane was changed once for each object");
        });
    }

    /// <summary>A GRAPH WITH WIRES ON IT, emptied in one go. Every wire is cut by the same sweep that takes the nodes
    /// away, so this is where the scene answering back about its own bookkeeping would go round for ever.</summary>
    [Test]
    [Timeout(15000)]
    public void ClearingAGraphEndsInsteadOfHanging()
    {
        var (canvas, scene, nodes) = Stage();

        var first = Node(nodes, 0);
        var second = Node(nodes, 300);

        _ = new CanvasConnection(first.Outputs[0], second.Inputs[0]);

        Assert.That(scene.Items.Count, Is.GreaterThan(0), "nothing was put on the plane at all");

        scene.Clear();

        Assert.That(scene.Items.Count, Is.EqualTo(0));
    }

    /// <summary>...and through the HISTORY, which is how the page actually does it: a snapshot before, the change, a
    /// snapshot after.</summary>
    [Test]
    [Timeout(15000)]
    public void ClearingThroughTheHistoryEndsInsteadOfHanging()
    {
        var (canvas, scene, nodes) = Stage();

        var first = Node(nodes, 0);
        var second = Node(nodes, 300);

        _ = new CanvasConnection(first.Outputs[0], second.Inputs[0]);

        canvas.History.Record(scene, "Clear", scene.Clear);

        Assert.That(scene.Items.Count, Is.EqualTo(0));
    }

    /// <summary>A BOARD HOLDING OBJECTS the application holds as data, which is the state the page is actually in. Their
    /// containers go with the same sweep while the application still holds the objects.</summary>
    [Test]
    [Timeout(15000)]
    public void ClearingAPlaneHoldingObjectsEndsInsteadOfHanging()
    {
        var (canvas, scene, objects) = Board();

        objects.Add(new PlacedObject
        {
            Left = 10, Top = 20, Width = 100, Height = 60,
            Content = new PlacedShape(CanvasShape.Rectangle)
        });

        objects.Add(new PlacedObject { Left = 200, Top = 20, Width = 100, Height = 60, Content = "plain" });

        canvas.History.Record(scene, "Clear", scene.Clear);

        Assert.That(scene.Items.Count, Is.EqualTo(0));
    }

    /// <summary>CLEARED WHILE SOMETHING IS SELECTED - which is the ordinary way it happens, because a person selects,
    /// looks at what they have, and then decides to empty the plane. The frame, its grips and the selection itself all
    /// point at things that are no longer there.</summary>
    [Test]
    [Timeout(15000)]
    public void ClearingWithASelectionEndsInsteadOfHanging()
    {
        var (canvas, scene, nodes) = Stage();

        var first = Node(nodes, 0);
        var second = Node(nodes, 300);

        _ = new CanvasConnection(first.Outputs[0], second.Inputs[0]);

        canvas.SelectMany(new System.Collections.Generic.List<ICanvasItem>(scene.Items), false);

        Assert.That(canvas.Selection.Count, Is.GreaterThan(0), "nothing got selected, so this proves nothing");

        canvas.History.Record(scene, "Clear", scene.Clear);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Items.Count, Is.EqualTo(0));
            Assert.That(canvas.Selection.Count, Is.EqualTo(0), "the selection still holds things that are gone");
        });
    }

    /// <summary>A SELECTION SURVIVES AN EDIT. Letting go of what has left the plane must not let go of what is still on
    /// it - an inspector writes a color, the scene is told something changed, and if the selection evaporated there
    /// the row it was writing through has nothing left to write to.</summary>
    [Test]
    [Timeout(15000)]
    public void AnEditDoesNotDropTheSelection()
    {
        var (canvas, scene, objects) = Board();

        objects.Add(new PlacedObject
        {
            Left = 10, Top = 20, Width = 100, Height = 60,
            Content = new PlacedShape(CanvasShape.Rectangle)
        });

        canvas.SelectMany(new System.Collections.Generic.List<ICanvasItem>(scene.Items), false);

        var held = canvas.Selection.Count;

        Assert.That(held, Is.GreaterThan(0), "nothing got selected, so this proves nothing");

        // WHAT AN INSPECTOR DOES when a value is written: the drawing changed, so the plane is told.
        scene.Touch();

        Assert.That(canvas.Selection.Count, Is.EqualTo(held), "the selection was dropped by an ordinary edit");
    }

    /// <summary>...and a child INSIDE A GROUP is still selected. A group's children leave the scene's own list, so
    /// anything asking the scene "is this still there" is told no about something that plainly is.</summary>
    [Test]
    [Timeout(15000)]
    public void ASelectedChildOfAGroupIsNotForgotten()
    {
        var (canvas, scene, _) = Board();

        var first = new ShapeItem(CanvasShape.Rectangle, new Rect(0, 0, 60, 40), Brushes.White, 2);
        var second = new ShapeItem(CanvasShape.Rectangle, new Rect(100, 0, 60, 40), Brushes.White, 2);

        scene.Add(first);
        scene.Add(second);

        canvas.SelectMany(new System.Collections.Generic.List<ICanvasItem> { first, second }, false);
        canvas.GroupSelection();

        canvas.SelectMany(new System.Collections.Generic.List<ICanvasItem> { first }, false);

        Assert.That(canvas.Selection.Count, Is.EqualTo(1), "a child of a group could not be selected at all");

        scene.Touch();

        Assert.That(canvas.Selection.Count, Is.EqualTo(1), "the child was forgotten because the scene does not hold it");
    }

    /// <summary>And what the graph says afterwards: a plane with nothing on it is a graph with no wires. A node whose
    /// wire was cut by the sweep must not be left saying it still has one.</summary>
    [Test]
    [Timeout(15000)]
    public void ClearingCutsTheWiresOutOfTheGraph()
    {
        var (canvas, scene, nodes) = Stage();

        var first = Node(nodes, 0);
        var second = Node(nodes, 300);

        _ = new CanvasConnection(first.Outputs[0], second.Inputs[0]);

        scene.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(first.Outputs[0].Connections.Count, Is.EqualTo(0));
            Assert.That(second.Inputs[0].Connections.Count, Is.EqualTo(0));
        });
    }
}
