using System.Linq;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core;
using Adamantium.UITests.Graph;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The control the canvas builds around an application's object is the canvas's own, so it goes with the
/// canvas. Only taken off the scene, it stayed alive with what it listens to - a localized label held it for good, and
/// through it the page the canvas was on. An object merely leaving the plane keeps its control: the canvas's history
/// may put that item back.</summary>
[TestFixture]
public class CanvasObjectReleaseTests
{
    private static (InfiniteCanvas Canvas, CanvasScene Scene, TrackingCollection<ICanvasObject> Objects) Stage()
    {
        var canvas = new InfiniteCanvas();
        canvas.Measure(new Size(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));

        var scene = new CanvasScene();
        var objects = new TrackingCollection<ICanvasObject>();
        canvas.Scene = scene;
        canvas.Objects = objects;

        return (canvas, scene, objects);
    }

    private static ContentPresenter PresenterOf(CanvasScene scene) =>
        scene.Items.OfType<ElementItem>().Select(item => item.Element).OfType<ContentPresenter>().Single();

    [Test]
    public void ADiscardedCanvas_DiscardsTheControlsBuiltForItsObjects()
    {
        var (canvas, scene, objects) = Stage();
        objects.Add(new PlacedObject { Left = 10, Top = 20, Width = 100, Height = 60, Content = "plain" });
        var presenter = PresenterOf(scene);

        DiscardedVisuals.Publish(canvas);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(presenter.IsDiscarded, Is.True);
    }

    [Test]
    public void AnObjectTakenOffThePlane_KeepsItsControlForTheHistory()
    {
        var (_, scene, objects) = Stage();
        var model = new PlacedObject { Left = 10, Top = 20, Width = 100, Height = 60, Content = "plain" };
        objects.Add(model);
        var presenter = PresenterOf(scene);

        objects.Remove(model);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.That(presenter.IsDiscarded, Is.False);
    }
}
