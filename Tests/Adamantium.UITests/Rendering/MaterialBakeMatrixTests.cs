using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using Adamantium.UI.Rendering.Payloads;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// The material batch folds in the bake matrix like every family, so a pane under a motion node keeps its offset.
[TestFixture]
public class MaterialBakeMatrixTests
{
    private static RectanglePayload Pane(Rect destination) =>
        new(new MaterialBrush(), destination, CornerRadius.Empty, null);

    /// <summary>The control: IDENTITY changes nothing, so the world-baked path is bit-for-bit what it always was.</summary>
    [Test]
    public void AnIdentityBakeLeavesTheBoundsAlone()
    {
        Assert.That(MaterialRectCollector.BakeItem(Pane(new Rect(10, 20, 300, 200)), Matrix4x4F.Identity,
            1.0, transformSlot: 7, fadeSlot: -1, source: null, clipSlot: -1, out var item), Is.True);

        Assert.That(item.Bounds.X, Is.EqualTo(10f));
        Assert.That(item.Bounds.Y, Is.EqualTo(20f));
        Assert.That(item.Bounds.Z, Is.EqualTo(300f));
        Assert.That(item.Bounds.W, Is.EqualTo(200f));
    }

    /// <summary>...and a NODE-RELATIVE bake has to reach the record. Dropped, the pane draws at the node's origin -
    /// measured on the brushes tab as an acrylic pane 90x124 off, exactly its offset within the sliding view.</summary>
    [Test]
    public void ANodeRelativeBakeReachesTheRecord()
    {
        var rel = Matrix4x4F.Translation(new Vector3F(90, 124, 0));

        Assert.That(MaterialRectCollector.BakeItem(Pane(new Rect(10, 20, 300, 200)), rel,
            1.0, transformSlot: 7, fadeSlot: -1, source: null, clipSlot: -1, out var item), Is.True);

        Assert.That(item.Bounds.X, Is.EqualTo(100f), "the node-relative X was dropped - the pane draws at the node's origin");
        Assert.That(item.Bounds.Y, Is.EqualTo(144f), "the node-relative Y was dropped - the pane draws at the node's origin");
        Assert.That(item.Bounds.Z, Is.EqualTo(300f), "a translation must not resize the pane");
        Assert.That(item.Bounds.W, Is.EqualTo(200f), "a translation must not resize the pane");
    }

    /// <summary>The scale half of the same matrix, for the same reason - the sibling batches fold both.</summary>
    [Test]
    public void AScaledBakeReachesTheRecordToo()
    {
        var scaled = Matrix4x4F.Scaling(2, 3, 1);

        Assert.That(MaterialRectCollector.BakeItem(Pane(new Rect(10, 20, 300, 200)), scaled,
            1.0, transformSlot: 7, fadeSlot: -1, source: null, clipSlot: -1, out var item), Is.True);

        Assert.That(item.Bounds.X, Is.EqualTo(20f));
        Assert.That(item.Bounds.Y, Is.EqualTo(60f));
        Assert.That(item.Bounds.Z, Is.EqualTo(600f));
        Assert.That(item.Bounds.W, Is.EqualTo(600f));
    }
}
