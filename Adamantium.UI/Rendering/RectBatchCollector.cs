using System;
using Adamantium.UI.Core.Graphics;
using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.FX;
using Adamantium.UI.Rendering.Payloads;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// Item-background batch (the "подложки" instancing): collects same-clip SOLID rounded-rect fills - each baked to WORLD
// space on the CPU - into ONE instanced draw per segment (RectBatchEffect reconstructs the rounded corners from an SDF
// = self-anti-aliasing, so N item backgrounds cost ~1 draw AND no separate AA fringe). Segment/buffer/overlap
// machinery is in BatchCollector; this adds rect baking + the SDF draw. Rendered BELOW the text batch (lower layer).
internal sealed class RectBatchCollector : ShapeSdfCollector<RectItem>
{
    // A/B / safety-valve toggle: off routes every rect back to its per-unit fill + AA-fringe draw (the pre-batch path).
    public static bool Enabled = true;

    public RectBatchCollector() : base(4096) { }

    protected override IEffectPass DrawPass => Effect.BatchRectPass;

    /// <summary>The single rule for what this batch draws (a visible solid fill, no pen or a solid one, any four corners);
    /// render units ask it too, so the two never drift.</summary>
    public static bool WantsBatch(RectanglePayload p)
    {
        if (!Enabled) return false;
        if (p.Brush is not (null or SolidColorBrush)) return false;   // gradient/image FILL -> fallback
        if (!IsPenBatchable(p.Pen)) return false;
        // A BORDER (per-side thickness) and a PEN share one color slot in the instance, so a payload carrying both is
        // not something this record can express. Nothing produces that pair - the check is here so a future caller
        // finds the per-unit path instead of a border drawn in the pen's color.
        if (p.HasFrame && (p.BorderBrush is not SolidColorBrush || p.Pen != null)) return false;
        // Need at least a visible fill OR a visible stroke (a hollow stroked rect batches too - fill just alpha 0).
        var hasFill = p.Brush is SolidColorBrush { Color.A: > 0 };
        var hasStroke = p.Pen is { Brush: SolidColorBrush { Color.A: > 0 } };
        var hasBorder = p.HasFrame && p.BorderBrush is SolidColorBrush { Color.A: > 0 };
        return hasFill || hasStroke || hasBorder;
    }

    public bool CanBatch(RectanglePayload p) => WantsBatch(p);

    // A pen the SDF stroke shader can draw analytically: none, or a SOLID-color stroke. Dashes are supported up to a
    // SIX-run pattern (an even count - runs 0,1 in Stroke0.zw, 2..5 in Dash); longer falls back to the compute expander. Trim, dash
    // offset and thickness are all handled per-fragment (see BatchEffect.fx), so a dashed/trimmed stroke still BATCHES -
    // which is what lets the whole virtualized grid dash without per-tile GPU buffers (the device-memory OOM).
    internal static bool IsPenBatchable(Pen pen)
    {
        if (pen == null) return true;
        if (pen.Brush is not SolidColorBrush) return false;
        var dash = pen.DashStrokeArray;
        if (dash is not { Count: > 0 }) return true;
        if (!IsDashPatternBatchable(dash)) return false;

        // The analytic dash mask jumps at corners (the nearest-point arc length is discontinuous), so only strokes too
        // thin for caps to resolve batch; the rest go to the compute expander.
        return pen.Thickness * 0.5 <= 1.5;
    }

    /// <summary>A dash pattern the instance can carry: an EVEN number of runs (a pattern that does not alternate
    /// ON/OFF whole would swap its meaning every lap round a closed contour), at most six of them - runs 0 and 1 in
    /// <see cref="RectItem.Stroke0"/>.zw and runs 2..5 in <see cref="RectItem.Dash"/>. Anything longer keeps going to
    /// the compute expander, which builds the pieces as real geometry.</summary>
    internal static bool IsDashPatternBatchable(IReadOnlyList<double> dash)
        => dash.Count is >= 2 and <= 6 && dash.Count % 2 == 0;

    // Bake a pen into the instance's stroke fields (shared by the rect + ellipse batches). Color with opacity folded;
    // width/dash/offset scale by the world device scale (sx) into device px, matching the arc-length `s` the shader
    // computes; trim is a 0..1 fraction. CENTER-aligned (half in / half out). No pen -> a zero stroke (fill only).
    /// <param name="contourLength">Length of the outline being stroked, in LOCAL units, or 0 when the caller cannot say.
    /// Only <see cref="Pen.FitDashesToContour"/> needs it: the pattern is stretched so a whole number of periods goes
    /// round, which is what keeps a closed dashed ring from carrying one long dash at its seam.</param>
    internal static void BakeStroke(Pen pen, double opacity, float sx,
        out Vector4F strokeColor, out Vector4F stroke0, out Vector4F stroke1, double contourLength = 0)
        => BakeStroke(pen, opacity, sx, out strokeColor, out stroke0, out stroke1, out _, contourLength);

    /// <param name="dash">Dash runs 2..5, in device px - the pattern beyond the first ON/GAP pair. Zero for the plain
    /// two-run pattern, which is what nearly every pen carries.</param>
    internal static void BakeStroke(Pen pen, double opacity, float sx,
        out Vector4F strokeColor, out Vector4F stroke0, out Vector4F stroke1, out Vector4F dash, double contourLength = 0)
    {
        strokeColor = Vector4F.Zero;
        stroke0 = Vector4F.Zero;
        stroke1 = new Vector4F(0, 0, 1, 0);   // dashOffset=0, trimStart=0, trimEnd=1, flags=0
        dash = Vector4F.Zero;
        if (pen?.Brush is not SolidColorBrush penBrush) return;

        var sc = penBrush.Color.ToVector4();
        sc.W *= (float)(opacity * penBrush.Opacity);
        strokeColor = sc;

        float dashOn = 0f, dashGap = 0f;
        var period = 0.0;
        var fit = 1.0;
        var runs = 0;
        if (pen.DashStrokeArray is { Count: >= 2 } d && IsDashPatternBatchable(d))
        {
            runs = d.Count;
            for (var i = 0; i < runs; i++) period += d[i];
            // Fit the pattern to the outline: without it the leftover of the last period lands where the contour closes
            // and reads as one long dash, and it moves about as the shape resizes.
            if (pen.FitDashesToContour && period > 0 && contourLength > 0)
            {
                var periods = Math.Max(1.0, Math.Round(contourLength / period));
                fit = contourLength / (periods * period);
            }

            dashOn = (float)(d[0] * fit * sx);
            dashGap = (float)(d[1] * fit * sx);
            // Runs 2..5 - a pattern of two carries none of them and leaves this zero.
            dash = new Vector4F(
                runs > 2 ? (float)(d[2] * fit * sx) : 0f,
                runs > 3 ? (float)(d[3] * fit * sx) : 0f,
                runs > 4 ? (float)(d[4] * fit * sx) : 0f,
                runs > 5 ? (float)(d[5] * fit * sx) : 0f);
        }
        // Packed for the shader: four caps base-8 (codes below) - the two DASH caps (a dash's own two ends, separate so
        // it can be asymmetric), then Start/EndLineCap for the contour's real ends (which only exist when trimmed);
        // the JOIN (0 miter, 1 bevel, 2 round) sits above them all, and the RUN COUNT above that.
        var capFlags = CapCode(pen.DashStartCap) + 8f * CapCode(pen.DashEndCap)
                     + 64f * CapCode(pen.StartLineCap) + 512f * CapCode(pen.EndLineCap)
                     + 4096f * JoinCode(pen.PenLineJoin)
                     + 32768f * runs;
        stroke0 = new Vector4F((float)(pen.Thickness * sx), 0f, dashOn, dashGap);
        // DashPhase is in PERIODS - so an animation runs 0 -> 1 and lands back on itself whatever the array says - and
        // becomes pixels here, alongside the pixel offset. Both take the fit, or a ring seamless in shape would still
        // drift in phase as it marched.
        var offset = (pen.DashOffset + pen.DashPhase * period) * fit;
        stroke1 = new Vector4F((float)(offset * sx), (float)pen.TrimStart, (float)pen.TrimEnd, capFlags);
    }

    // Outline length of a rounded rect: the four straight runs, each shortened by the two corners it meets, plus the four
    // quarter-arcs. Only the dash FIT reads it. Must agree with RoundRectArc's perimeter in BatchEffect.fx, or a dashed
    // ring would close on a different phase than it was fitted for.
    private static double RoundedRectPerimeter(Rect rect, ProceduralGeometry.CornerRadius corners)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return 0;

        var c = ClampCorners(corners, rect.Width, rect.Height);
        var edges = 2 * rect.Width - c.TopLeft - c.TopRight - c.BottomLeft - c.BottomRight
                  + 2 * rect.Height - c.TopLeft - c.BottomLeft - c.TopRight - c.BottomRight;
        return edges + Math.PI / 2 * (c.TopLeft + c.TopRight + c.BottomRight + c.BottomLeft);
    }

    // Each corner independently capped at half the shorter side, exactly as the tessellator caps it (Shapes.Rectangle's
    // ValidateCorners) - the SDF and the geometry path must round the same shape.
    internal static ProceduralGeometry.CornerRadius ClampCorners(ProceduralGeometry.CornerRadius c, double width, double height)
    {
        var max = Math.Min(width, height) / 2.0;
        return new ProceduralGeometry.CornerRadius(
            Math.Clamp(c.TopLeft, 0, max),
            Math.Clamp(c.TopRight, 0, max),
            Math.Clamp(c.BottomRight, 0, max),
            Math.Clamp(c.BottomLeft, 0, max));
    }

    /// <summary>The four corner radii as the instance carries them: clamped to the box, scaled to device px, in the
    /// order the shader reads them (x = TL, y = TR, z = BR, w = BL). Every rect family bakes them through here - four
    /// copies of the rule is how the batch and the tessellator drifted apart the last time.</summary>
    internal static Vector4F BakeRadii(ProceduralGeometry.CornerRadius corners, Rect dest, double sx)
    {
        var c = ClampCorners(corners, dest.Width, dest.Height);
        return new Vector4F((float)(c.TopLeft * sx), (float)(c.TopRight * sx), (float)(c.BottomRight * sx), (float)(c.BottomLeft * sx));
    }

    /// <summary>The largest of the four - what the quad has to make room for, and all the vertex stage needs.</summary>
    internal static float MaxOf(Vector4F radii) => Math.Max(Math.Max(radii.X, radii.Y), Math.Max(radii.Z, radii.W));

    /// <summary>A color with an opacity folded into its ALPHA, staying in the four BYTES a record carries the whole
    /// way. THE one place that fold happens: a color is bytes to begin with, so taking it through a
    /// <see cref="Vector4F"/> and back only rounds it twice for nothing.</summary>
    internal static Color WithOpacity(Color color, double opacity)
    {
        color.A = Color.ToByte((int)(color.A * opacity));
        return color;
    }

    /// <summary>The fill a solid brush contributes. Anything but a solid brush contributes nothing - alpha 0, which is
    /// how these batches say "no fill".</summary>
    internal static Color FillColor(Brush brush, double opacity)
        => brush is SolidColorBrush solid ? WithOpacity(solid.Color, opacity * solid.Opacity) : default;

    // All six caps, drawn analytically by CapReach in BatchEffect.fx. Codes MATCH the geometry stroker's MapCap so the two
    // stroke paths render the same shape: 0 flat, 1 square, 2 convex round, 3 convex triangle, 4 concave triangle, 5 concave round.
    private static float CapCode(PenLineCap cap) => cap switch
    {
        PenLineCap.Square => 1f,
        PenLineCap.ConvexRound => 2f,
        PenLineCap.ConvexTriangle => 3f,
        PenLineCap.ConcaveTriangle => 4f,
        PenLineCap.ConcaveRound => 5f,
        _ => 0f   // Flat
    };

    // The three outer-corner joins the SDF draws (see SdRoundRectJoin): 0 miter (sharp), 1 bevel (chamfer), 2 round.
    private static float JoinCode(PenLineJoin join) => join switch
    {
        PenLineJoin.Bevel => 1f,
        PenLineJoin.Round => 2f,
        _ => 0f
    };

    // Bake one solid rounded-rect fill (position -> world, color straight with opacity folded in) into an instance.
    // False = not bakeable this way (rotated/sheared world). Shared by TryAdd (append) AND the partial-replay UpdateSlot
    // path, which re-bakes ONE dirty tile in place (a hover recolor) without re-walking the scene.
    public static bool BakeItem(RectanglePayload p, Matrix4x4F world, double opacity, out RectItem item)
        => BakeItem(p, world, opacity, 0, -1, out item);

    /// <summary><paramref name="transformSlot"/>: 0 for world space, or a motion node's slot with <paramref name="world"/>
    /// relative to it. <paramref name="fadeSlot"/>: the opacity slot read at draw time (-1 = opaque).</summary>
    public static bool BakeItem(RectanglePayload p, Matrix4x4F world, double opacity, int transformSlot, int fadeSlot, out RectItem item)
    {
        item = default;
        const float eps = 1e-4f;
        if (Math.Abs(world.M12) > eps || Math.Abs(world.M21) > eps) return false;   // rotation/shear -> per-unit

        var color = FillColor(p.Brush, opacity);

        // Stroke (optional): the full pen baked to the instance (color + device-px width, dash on/gap, dash offset,
        // trim), CENTER-aligned (half in / half out). Solid, dashed and trimmed strokes all draw analytically in the SDF
        // shader, so a stroked tile stays in the batch (no per-tile GPU buffers) - the whole grid can dash without OOM.
        var sx = world.M11; var sy = world.M22; var tx = world.M41; var ty = world.M42;
        BakeStroke(p.Pen, opacity, (float)sx, out var strokeColor, out var stroke0, out var stroke1, out var dash,
            RoundedRectPerimeter(p.DestinationRect, p.CornerRadius));

        var r = p.DestinationRect;
        var radii = BakeRadii(p.CornerRadius, r, sx);

        // A BORDER instead of a pen: its four sides ride in Inset (device px, x/z horizontal so they take sx, y/w
        // vertical so they take sy) and its color takes the stroke slot - a payload never carries both (WantsBatch).
        var inset = Vector4F.Zero;
        if (p.HasFrame && p.BorderBrush is SolidColorBrush border)
        {
            var t = p.BorderThickness;
            inset = new Vector4F((float)(t.Left * sx), (float)(t.Top * sy), (float)(t.Right * sx), (float)(t.Bottom * sy));
            strokeColor = border.Color.ToVector4();
            strokeColor.W *= (float)(opacity * border.Opacity);
        }

        item = new RectItem
        {
            Bounds = new Vector4F((float)(r.X * sx + tx), (float)(r.Y * sy + ty), (float)(r.Width * sx), (float)(r.Height * sy)),
            // .x is the LARGEST of the four: it decides how far the quad has to reach, and one number is enough for that.
            // .z = 1 means "no fringe": the shader takes the edge hard instead of fading it over a pixel.
            Params = new Vector4F(MaxOf(radii), transformSlot, p.AntiAlias ? 0 : 1, fadeSlot),
            Radii = radii,
            Color = color,
            StrokeColor = new Color(strokeColor),
            Stroke0 = stroke0,
            Stroke1 = stroke1,
            Dash = dash,
            Inset = inset,
            // NO CLIP is -1, and it has to be set HERE rather than only where a clip is known: zero is a perfectly
            // valid slot number belonging to somebody else, so an instance baked through any other path (a patch, the
            // stage) would be read against a stranger's matrix and vanish.
            Clip = new Vector4F(-1, 0, 0, 0)
        };
        return true;
    }

    /// <summary>A blank instance is one nobody owns: blanking zeroes the record, and the owner tag with it.</summary>
    protected override bool IsBlank(int first, int count)
    {
        if (first < 0 || first + count > Count) return false;

        for (var i = 0; i < count; i++)
        {
            if (Items[first + i].OwnerTag != 0) return false;
        }

        return true;
    }

    /// <summary>Does this segment still draw anything at all - is any instance in it owned by somebody? A segment whose
    /// every instance has been blanked is a draw call that paints nothing, and the layer holding it can let it go.</summary>
    public bool SegmentDrawsNothing(int id)
    {
        var (first, count) = SegmentRange(id);
        if (first < 0 || count == 0) return true;

        for (var i = 0; i < count; i++)
        {
            if (Items[first + i].OwnerTag != 0) return false;
        }

        return true;
    }

    /// <summary>Blanks every instance owned by one of <paramref name="tags"/>, found by owner tag rather than by
    /// remembered slots, which may since belong to others.</summary>
    public int BlankOwnedAnywhere(IGraphicsDevice device, HashSet<int> tags)
    {
        if (tags.Count == 0) return 0;

        var blanked = 0;
        var runStart = -1;
        // The whole array, issued or not: stale owned bytes past Count are issued again when the scene grows back.
        var extent = Items?.Length ?? 0;
        for (var slot = 0; slot <= extent; slot++)
        {
            var mine = slot < extent && Items[slot].OwnerTag != 0 && tags.Contains(Items[slot].OwnerTag);
            if (mine)
            {
                if (runStart < 0) runStart = slot;
                continue;
            }

            if (runStart < 0) continue;
            BlankRunPastTheCursor(device, (uint)runStart, (uint)(slot - runStart));
            blanked += slot - runStart;
            runStart = -1;
        }

        return blanked;
    }

    public void BlankOwned(IGraphicsDevice device, uint first, uint count, int ownerTag)
    {
        if (count == 0 || first + count > (uint)Count) return;

        // The tag decides, not the caller: between the control leaving the order and this sweep a walk may have re-laid
        // the arena and handed these very slots to somebody else. Blanking them then would erase live content - which is
        // exactly what a run-list-based version of this did.
        var runStart = -1;
        for (var i = 0u; i <= count; i++)
        {
            var slot = (int)(first + i);
            var mine = i < count && Items[slot].OwnerTag == ownerTag && FindSegmentContaining(slot) >= 0;
            if (mine)
            {
                if (runStart < 0) runStart = slot;
                continue;
            }

            if (runStart < 0) continue;
            BlankRun(device, (uint)runStart, (uint)(slot - runStart));
            runStart = -1;
        }
    }

    /// <summary>A re-bake changes how a slot LOOKS, not whose it is. <see cref="BakeItem"/> answers with an UNOWNED
    /// record - it is handed a payload, not a group - so writing it whole would erase the tag the orphan sweep asks for,
    /// and the control could then leave the paint order without anyone able to blank its instances. A caller that does
    /// know the owner (the patch stage) still wins.</summary>
    public override void UpdateSlot(IGraphicsDevice device, int slot, RectItem item)
    {
        if (item.OwnerTag == 0 && slot >= 0 && slot < Count) item.OwnerTag = Items[slot].OwnerTag;
        base.UpdateSlot(device, slot, item);
    }

    /// <summary>Is this slot still the one <paramref name="ownerTag"/> was recorded into? A walk that did not visit a
    /// group can have handed its slot to somebody else, and a patch that writes without asking paints that somebody
    /// with these bytes AND takes their tag (<see cref="UpdateSlot"/> inherits it), leaving the true owner blank.</summary>
    public bool SlotOwnedBy(int slot, int ownerTag) =>
        ownerTag == 0 || (slot >= 0 && slot < Count && Items[slot].OwnerTag == ownerTag);

    /// <summary>Bakes one solid rounded-rect fill into the pending segment; false on a rotated world or buffer overflow,
    /// and the caller falls back to the per-unit path.</summary>
    public bool TryAdd(RectanglePayload p, Matrix4x4F world, double opacity, Rect2D scissor, Rect logicalBounds, int transformSlot = 0,
        int fadeSlot = -1, int ownerTag = 0, int clipSlot = -1)
    {
        EnsureCpuCapacity(Count + 1);
        if (Count + 1 > GpuCapacity) return false;
        if (!BakeItem(p, world, opacity, transformSlot, fadeSlot, out var item)) return false;   // rotation/shear -> per-unit
        item.Clip = new Vector4F(clipSlot, 0, 0, 0);
        item.OwnerTag = ownerTag;   // travels with the bytes through every copy the arena makes - see RectItem.OwnerTag
        Items[Count++] = item;
        MarkPending(scissor, logicalBounds);
        return true;
    }

    /// <summary>Bake one unit into the patch stage - see BatchArena. Same bake TryAdd uses; it just lands in the stage
    /// instead of the arena, because a patch has to know the whole frame is repairable before it changes any of it.</summary>
    public override bool TryStage(IRenderUnit unit, Matrix4x4F world, int transformSlot, int ownerTag, int clipSlot = -1)
    {
        if (unit is not RenderUnits.RectangleRenderUnit u || !CanBatch(u.RectPayload)) return false;
        if (!BakeItem(u.RectPayload, world, u.FillOpacity, transformSlot, unit.FadeSlot, out var item)) return false;

        // The same stamp TryAdd makes. Without it a patched-in rectangle had no owner, and the orphan sweep - which asks
        // the INSTANCE whose it is - could never blank it once the control stopped drawing.
        item.OwnerTag = ownerTag;
        item.Clip = new Vector4F(clipSlot, 0, 0, 0);   // ...and the same clip, for the same reason - see BatchArena.TryStage
        Stage.Add(item);
        return true;
    }
}
