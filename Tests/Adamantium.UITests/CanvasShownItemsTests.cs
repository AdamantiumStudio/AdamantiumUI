using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An item a derived canvas hides (<c>IsShown</c>) stays in the scene and in the history - what a view shows of
/// a document is not an edit of it - but is not reached by a press or a band and is let go of by the selection; shown
/// again, it is all of that again.</summary>
public class CanvasShownItemsTests
{
    private sealed class HidingBoard : DrawingBoard
    {
        public ICanvasItem Hidden { get; set; }

        public void Hide(ICanvasItem item)
        {
            Hidden = item;
            RefreshShown();
        }

        protected override bool IsShown(ICanvasItem item) => !ReferenceEquals(item, Hidden);
    }

    private static HidingBoard Sized()
    {
        var canvas = new HidingBoard { Scene = new CanvasScene() };
        canvas.Measure(new Size<double>(800, 600), force: true);
        canvas.Arrange(new Rect(0, 0, 800, 600));
        return canvas;
    }

    private static ShapeItem Box(double x, double y) =>
        new(CanvasShape.Rectangle, new Rect(x, y, 40, 40), Brushes.Red, 2) { Fill = Brushes.Red };

    [Test]
    public void AnItemHidden_StaysInTheSceneAndTheHistory_ButIsNotReachedOrSelected()
    {
        var canvas = Sized();
        var kept = Box(100, 100);
        var hidden = Box(120, 110);
        canvas.Scene.Add(kept);
        canvas.Scene.Add(hidden);
        canvas.SelectMany([kept, hidden], false);
        canvas.History.Clear();
        canvas.BeginEdit("Move");
        kept.Move(new Vector2<double>(5, 0));
        canvas.EndEdit();

        canvas.Hide(hidden);
        var reached = canvas.ItemAt(new Vector2<double>(155, 145));
        var inBand = canvas.ItemsHere(new Rect(0, 0, 800, 600)).ToList();
        var shown = canvas.ShownItems().ToList();
        var selected = canvas.Selection.ToList();
        var undoable = canvas.History.CanUndo;
        canvas.SelectMany([kept, hidden], false);
        var selectedAgain = canvas.Selection.ToList();
        canvas.SelectMany([kept], false);
        var forwardWhileHidden = canvas.BringForwardCommand.CanExecute(null);
        canvas.Hide(null);
        var forwardShown = canvas.BringForwardCommand.CanExecute(null);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Scene.Items, Does.Contain(hidden), "hiding took the item out of the scene");
            Assert.That(reached, Is.Null, "a press reached the item hidden");
            Assert.That(inBand, Is.EqualTo(new[] { kept }), "the plane's items in a band held the item hidden");
            Assert.That(shown, Is.EqualTo(new[] { kept }), "the items shown held the item hidden");
            Assert.That(selected, Is.EqualTo(new[] { kept }), "the selection kept the item hidden");
            Assert.That(selectedAgain, Is.EqualTo(new[] { kept }), "the item hidden was selected when asked");
            Assert.That(undoable, Is.True, "hiding an item cleared the history");
            Assert.That(forwardWhileHidden, Is.False, "a step forward over an item hidden was offered");
            Assert.That(forwardShown, Is.True, "a step forward over the item shown again was not offered");
            Assert.That(canvas.ItemAt(new Vector2<double>(155, 145)), Is.SameAs(hidden), "shown again, it is not reached");
        });
    }
}
