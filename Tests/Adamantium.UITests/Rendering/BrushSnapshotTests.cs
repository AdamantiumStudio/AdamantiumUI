using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering.Payloads;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// The render thread reads brushes through snapshots that are immutable and current, so brush animations reach the screen.
[TestFixture]
public class BrushSnapshotTests
{
    private static readonly Rect Box = new Rect(0, 0, 10, 10);

    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;

    [Test]
    public void PayloadTracksTheBrushAsItChanges()
    {
        var brush = new SolidColorBrush(Colors.Red);
        var payload = new RectanglePayload(brush, Box, new CornerRadius(0), null);

        Assert.That(ColorOf(payload.Brush), Is.EqualTo(Colors.Red));

        brush.Color = Colors.Blue;   // the element is NOT re-recorded: a color change re-bakes the payload it already has

        Assert.That(ColorOf(payload.Brush), Is.EqualTo(Colors.Blue),
            "a recorded payload must see the brush's CURRENT appearance, or an animated brush never reaches the screen");
    }

    [Test]
    public void PayloadNeverExposesTheLiveBrush()
    {
        var brush = new SolidColorBrush(Colors.Red);
        var payload = new RectanglePayload(brush, Box, new CornerRadius(0), null);

        Assert.That(payload.Brush, Is.Not.SameAs(brush), "the render path must not be handed the mutable brush");
        Assert.That(payload.Brush.IsFrozen, Is.True);
    }

    [Test]
    public void TheSnapshotItselfIsImmutable()
    {
        var brush = new SolidColorBrush(Colors.Red);
        var snapshot = new RectanglePayload(brush, Box, new CornerRadius(0), null).Brush;

        ((SolidColorBrush)snapshot).Color = Colors.Blue;
        snapshot.Opacity = 0.1;

        Assert.That(ColorOf(snapshot), Is.EqualTo(Colors.Red), "a frozen snapshot's setters must be inert");
        Assert.That(snapshot.Opacity, Is.EqualTo(1.0));
    }

    [Test]
    public void AnUnchangedBrushKeepsTheSameSnapshotInstance()
    {
        // The render cache detects change by REFERENCE (Brush has no value equality). A brush that did not change must
        // therefore keep handing out the same snapshot, or every re-record would look like a recolor and re-bake - and
        // every TextBlock would re-raster its glyphs - for nothing.
        var brush = new SolidColorBrush(Colors.Red);
        var payload = new RectanglePayload(brush, Box, new CornerRadius(0), null);

        Assert.That(payload.Brush, Is.SameAs(payload.Brush));

        var before = payload.Brush;
        brush.Color = Colors.Blue;
        Assert.That(payload.Brush, Is.Not.SameAs(before), "a CHANGED brush must publish a new snapshot");
    }

    [Test]
    public void MovingAGradientStopReachesThePayload()
    {
        // The shimmer: the band is not an element that travels, it is a gradient whose STOPS move. Nothing about the
        // element changes - so the whole effect lives or dies on the payload re-reading the brush.
        var lead = new GradientStop(Colors.Transparent, 0);
        var peak = new GradientStop(Colors.White, 0.25);
        var brush = new LinearGradientBrush { GradientStops = { lead, peak } };
        var payload = new RectanglePayload(brush, Box, new CornerRadius(0), null);

        Assert.That(((GradientBrush)payload.Brush).GradientStops[1].Offset, Is.EqualTo(0.25));

        peak.Offset = 0.75;

        Assert.That(((GradientBrush)payload.Brush).GradientStops[1].Offset, Is.EqualTo(0.75));
    }

    [Test]
    public void AnimatingAStrokeBrushReachesThePen()
    {
        var stroke = new SolidColorBrush(Colors.Red);
        var pen = new Pen(stroke, 2);
        var payload = new RectanglePayload(null, Box, new CornerRadius(0), pen);

        Assert.That(ColorOf(payload.Pen.Brush), Is.EqualTo(Colors.Red));
        Assert.That(payload.Pen.Brush, Is.Not.SameAs(stroke));

        stroke.Color = Colors.Blue;

        Assert.That(ColorOf(payload.Pen.Brush), Is.EqualTo(Colors.Blue));
    }

    [Test]
    public void BrushOpacityReachesThePayload()
    {
        // What the loading skeletons pulse: one shared brush's Opacity, animated, painting hundreds of cards.
        var brush = new SolidColorBrush(Colors.White);
        var payload = new RectanglePayload(brush, Box, new CornerRadius(0), null);

        brush.Opacity = 0.4;

        Assert.That(payload.Brush.Opacity, Is.EqualTo(0.4));
    }
}
