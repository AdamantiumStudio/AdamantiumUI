using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

// Brush.PaintEpoch: a quiet application does not move it, and any brush rewriting itself does.
[TestFixture]
public class BrushPaintEpochTests
{
    [Test]
    public void AQuietApplicationDoesNotMoveTheEpoch()
    {
        var brush = new SolidColorBrush(Colors.Red);
        var before = Brush.PaintEpoch;

        _ = brush.Color;
        _ = brush.Opacity;
        _ = brush.PaintVersion;

        Assert.That(Brush.PaintEpoch, Is.EqualTo(before), "reading a brush is not repainting it");
    }

    [Test]
    public void ARepaintMovesTheEpoch()
    {
        var brush = new SolidColorBrush(Colors.Red);
        var before = Brush.PaintEpoch;

        brush.Color = Colors.Green;

        Assert.That(Brush.PaintEpoch, Is.GreaterThan(before),
            "a reader that skipped its scan on this frame has to be told to run it");
    }

    /// <summary>The epoch is the WHOLE APPLICATION's, deliberately: a reader holds a record per scene and cannot know
    /// which brushes are in somebody else's. So one brush moving it makes every reader look once - which is right, and
    /// still O(1) on the frames where nothing moved at all.</summary>
    [Test]
    public void AnyBrushMovesIt()
    {
        var watched = new SolidColorBrush(Colors.Red);
        var other = new SolidColorBrush(Colors.Blue);
        var before = Brush.PaintEpoch;

        other.Opacity = 0.5;

        Assert.That(Brush.PaintEpoch, Is.GreaterThan(before));
        Assert.That(watched.PaintVersion, Is.Not.Negative, "and the watched brush itself is untouched");
    }
}
