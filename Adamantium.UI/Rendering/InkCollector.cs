using System;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.FX;
using Adamantium.UI.Rendering.Payloads;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// Ink strokes for InkBrush fills: one record per polyline point, each drawing its own segment's box, within the clip
// group's single draw. See InkEffect.fx.
internal sealed class InkCollector : SdfBatchCollector<InkSegmentItem>
{
    public static bool Enabled = true;

    private InkEffect Effect;

    // SMALL to start with, and it grows. A collector is made for every render cache - including every off-screen one a
    // bake or a test opens - and its buffer is allocated whether or not anything ever draws through it. Asking for four
    // thousand segments up front took the device out of memory partway through a run: thirty off-screen tests failed on
    // AllocateMemory, none of them anything to do with ink.
    public InkCollector() : base(256) { }

    protected override IEffectPass DrawPass => Effect.InkSegmentsPass;

    protected override void EnsureEffect(IGraphicsDevice device)
    {
        if (Effect != null) return;

        Effect = Own(new InkEffect(device));
        ProjectionParam = Effect.Projection;
        ViewportSizeParam = Effect.ViewportSize;
        InstancesAddressParam = Effect.InstancesAddress;
        TransformsAddressParam = Effect.TransformsAddress;
    }

    /// <summary>THE one statement of what this batch draws - the render unit asks THIS, never its own copy.</summary>
    public static bool WantsBatch(RectanglePayload p)
    {
        if (!Enabled) return false;
        if (p.Brush is not InkBrush ink) return false;

        // A pen would be a border around the ink, which is not a thing ink has. Nothing to draw is not an error either:
        // a stroke with one point is a dot, which is a segment from a point to itself.
        return p.Pen == null && ink.Count > 0;
    }

    public bool CanBatch(RectanglePayload p) => WantsBatch(p);

    public bool TryAdd(RectanglePayload p, Matrix4x4F world, double opacity, Rect2D scissor, Rect logicalBounds,
        int transformSlot = 0, int fadeSlot = -1, int clipSlot = -1)
    {
        if (p.Brush is not InkBrush ink || ink.Points == null) return false;

        const float eps = 1e-4f;
        if (Math.Abs(world.M12) > eps || Math.Abs(world.M21) > eps) return false;   // rotation/shear -> per-unit

        var points = Math.Max(1, Math.Min(ink.Count, ink.Points.Length));

        // One record per point, each drawing its segment's box, so only the ribbon is shaded. A fragment keeps a pixel
        // only if its segment is the nearest of the whole polyline, so overlaps blend once.
        EnsureCpuCapacity(Count + points);
        if (Count + points > GpuCapacity) return false;

        var sx = world.M11;
        var sy = world.M22;
        var tx = world.M41;
        var ty = world.M42;

        var color = Straight(ink.Color, (float)(opacity * ink.Opacity));
        var half = (float)(Math.Max(ink.Thickness, 0.1) * 0.5 * Math.Abs(sx));

        var first = Count;

        for (var i = 0; i < points; i++)
        {
            var at = first + i;

            var ax = ink.Points[i].X * sx + tx;
            var ay = ink.Points[i].Y * sy + ty;

            // THE POINT AFTER IT, so the record is a whole segment and its quad can be built from it alone. The last
            // point has nobody after it: it carries itself, and draws nothing - the segment before it already reaches
            // that far, cap and all.
            var last = i == points - 1;
            var bx = last ? ax : ink.Points[i + 1].X * sx + tx;
            var by = last ? ay : ink.Points[i + 1].Y * sy + ty;

            Items[Count++] = new InkSegmentItem
            {
                Segment = new Vector4F(ax, ay, bx, by),
                // .w is WHICH SEGMENT this is, counted from ONE - and a non-zero .w is also what says this record
                // draws. Zero on the last point of a stroke, which has no segment of its own; and one on a stroke of a
                // single point, which is a dot and draws its segment from itself to itself.
                Params = new Vector4F(half, transformSlot, fadeSlot, last && points > 1 ? 0 : i + 1),
                // RELATIVE to THIS record, never absolute - and negative for every record but the first. The draw
                // offsets the buffer address by the first instance of the run it is flushing, so inside the shader
                // index 0 is the run's start and not the array's; an absolute index is only right while a run happens
                // to begin at zero, and reads somebody else's record as soon as one does not.
                Clip = new Vector4F(clipSlot, points, first - at, 0),
                Color = color
            };
        }

        MarkPending(scissor, logicalBounds);

        return true;
    }

    // The max distance of the stroke from its every-CoarseStride-th-point polyline (measured to segments), so fragments
    // can reject early against the coarse line.
    private float Spread(int first, int points)
    {
        if (points < CoarseStride * 2) return -1;   // fewer points than a coarse walk saves: none is taken

        var most = 0f;

        for (var at = 0; at < points - 1; at += CoarseStride)
        {
            var next = Math.Min(at + CoarseStride, points - 1);

            var ax = Items[first + at].Segment.X;
            var ay = Items[first + at].Segment.Y;
            var bx = Items[first + next].Segment.X;
            var by = Items[first + next].Segment.Y;

            var dx = bx - ax;
            var dy = by - ay;
            var span = dx * dx + dy * dy;

            for (var i = at + 1; i < next; i++)
            {
                var px = Items[first + i].Segment.X - ax;
                var py = Items[first + i].Segment.Y - ay;

                var t = span > 1e-12f ? Math.Clamp((px * dx + py * dy) / span, 0f, 1f) : 0f;
                var offX = px - dx * t;
                var offY = py - dy * t;

                var away = MathF.Sqrt(offX * offX + offY * offY);
                if (away > most) most = away;
            }
        }

        return most;
    }

    /// <summary>How many points a coarse step skips. Sixteen because the saving is what the coarse walk does NOT do and
    /// the cost is the spread it opens up: too fine saves nothing, too coarse bulges so far from the ink that no pixel
    /// is ever allowed to stop.</summary>
    internal const int CoarseStride = 16;

    /// <summary>Bake one unit into the patch stage - see BatchArena. Ink does not patch: a stroke is many instances and
    /// the stage repairs one, so a stroke that changed is re-recorded like anything else.</summary>
    public override bool TryStage(IRenderUnit unit, Matrix4x4F world, int transformSlot, int ownerTag, int clipSlot = -1)
        => false;

    private static Vector4F Straight(Color color, float opacity) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f * opacity);
}
