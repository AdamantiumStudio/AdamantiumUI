using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>The selection only ever holds what the scene holds, however a thing leaves it.</summary>
public class CanvasSelectionTests
{
    [Test]
    public void AnItemTakenOutOfTheScene_LeavesTheSelection()
    {
        var canvas = new InfiniteCanvas();
        canvas.Measure(new Size<double>(400, 300), force: true);
        canvas.Arrange(new Rect(0, 0, 400, 300));

        var scene = new CanvasScene();
        var gone = new ShapeItem(CanvasShape.Rectangle, new Rect(0, 0, 10, 10), Brushes.White, 1);
        var kept = new ShapeItem(CanvasShape.Rectangle, new Rect(50, 0, 10, 10), Brushes.White, 1);
        scene.Add(gone);
        scene.Add(kept);
        canvas.Scene = scene;

        canvas.SelectMany([gone, kept], false);
        scene.Remove(gone);

        Assert.That(canvas.Selection, Is.EqualTo(new[] { kept }));
    }
}
