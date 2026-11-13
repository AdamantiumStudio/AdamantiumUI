using Adamantium.UI.Core;
using Adamantium.UI.Rendering;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>An element's clip and a snapped rectangle beside it share an edge, and at a fractional scale that edge is a
/// midpoint: at 150% the 25-DIP boundary of a scroll bar's track sits on pixel 37.5. The track's background snaps it to
/// 38, so a clip rounded outward (to 37) let one column of the scrolled content show between the window and the track.
/// </summary>
[TestFixture]
public class ClipScissorMeetsSnappedEdgeTests
{
    private const double Scale = 1.5;

    private static readonly Rect2D Window = new()
    {
        Offset = new Offset2D { X = 0, Y = 0 },
        Extent = new Extent2D { Width = 4000, Height = 4000 }
    };

    [Test]
    public void ClipEdgesLandWhereSnappedEdgesDo()
    {
        for (var dip = 1; dip <= 64; dip++)
        {
            var clip = RenderCache.ClipScissorOf(new Rect(dip, dip, 18, 18), Scale, Window);
            var near = DevicePixels.ToDevicePixel(dip, Scale);
            var far = DevicePixels.ToDevicePixel(dip + 18, Scale);

            Assert.Multiple(() =>
            {
                Assert.That(clip.Offset.X, Is.EqualTo(near), $"left edge at {dip} DIP");
                Assert.That(clip.Offset.Y, Is.EqualTo(near), $"top edge at {dip} DIP");
                Assert.That(clip.Offset.X + (int)clip.Extent.Width, Is.EqualTo(far), $"right edge at {dip + 18} DIP");
                Assert.That(clip.Offset.Y + (int)clip.Extent.Height, Is.EqualTo(far), $"bottom edge at {dip + 18} DIP");
            });
        }
    }

    [Test]
    public void TheScrollBarsTrackEdge()
    {
        var content = RenderCache.ClipScissorOf(new Rect(25, 0, 100, 10), Scale, Window);

        Assert.That(content.Offset.X, Is.EqualTo(38), "the track's background starts at 38; so must what it borders");
    }

    [Test]
    public void ClipStaysInsideTheWindow()
    {
        var small = new Rect2D { Offset = new Offset2D { X = 10, Y = 10 }, Extent = new Extent2D { Width = 20, Height = 20 } };
        var clip = RenderCache.ClipScissorOf(new Rect(-5, 0, 100, 100), Scale, small);

        Assert.Multiple(() =>
        {
            Assert.That(clip.Offset.X, Is.EqualTo(10));
            Assert.That(clip.Extent.Width, Is.EqualTo(20u));
        });
    }
}
