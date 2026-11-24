using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// Elements drawn as a raster (DrawingMode.Raster): their subtree is drawn into one texture in the element's own space -
// what the window shows of it and a quarter as much again each way, at a scale a power of two from what the camera shows
// - and laid down where the camera puts it: moved and scaled, not drawn again, until the scale goes a step further, the
// camera leaves what it holds or what it shows changes. It is drawn as a clone of the subtree is: under a matrix from the
// window to the texture, into a target of its own - one walk of the subtree, as a frame drawn from its shapes takes.
public partial class RenderCache
{
    // The element whose texture is being drawn this frame, or null.
    private RasterRun _rasterRun;

    // What each element drawn as a raster holds in its texture, and what it is asked to hold, from frame to frame.
    private readonly Dictionary<Guid, RasterHeld> _rasterParts = new();

    private sealed class RasterHeld
    {
        // What the texture holds: the part of the element, in its units, at the scale, in texels.
        public bool Drawn;
        public float Scale;
        public Rect Part;
        public int Width;
        public int Height;

        // What the frames ask of it - another scale, part or print - and since when they have asked the same.
        public float WantedScale;
        public Rect WantedPart = Rect.Empty;
        public ulong WantedPrint;
        public long WantedSince;
    }

    // How long the scale and part a raster is asked to hold have to stay the same before its texture is drawn again:
    // while the camera zooms, a step of scale or a part left each few frames, the texture it holds is laid down
    // stretched, and drawn anew once, when the camera has stopped.
    private static readonly long RasterSettles = System.Diagnostics.Stopwatch.Frequency * 150 / 1000;

    // A raster laid down stretched this frame waits to be drawn anew: the frames go on until it is.
    private bool _rasterSettling;

    // How far past what is seen the texture reaches each way, as a share of it: room for the camera to move in.
    private const double RasterRoom = 0.25;

    // The largest side of the texture, in texels; past it the element is drawn from its shapes.
    private const int RasterLargest = 8192;

    private sealed class RasterRun
    {
        public IUIComponent Element;

        // The element's groups: [Start, End).
        public int Start;
        public int End;

        // The element's world, and its inverse - from the window to the element.
        public Matrix4x4F World;
        public Matrix4x4F FromWindow;

        // Device pixels to a unit of the element its texture is drawn at, and the part of it held, in its units.
        public float Scale;
        public Rect Part;
        public int Width;
        public int Height;

        // What its subtree draws, relative to it: its texture holds it while this stays.
        public ulong Print;

        // What cuts it in the window, in device pixels; and the bounds the walk drew within when it began, which closing
        // the texture goes back to.
        public Rect2D Scissor;
        public Rect2D Bounds;

        // Whether its texture is drawn this frame.
        public bool Draws;

        // Whether what it draws can be told apart frame to frame (see PrintRaster).
        public bool Still;

        public bool RecordingBefore;
    }

    // An element drawn as a raster, starting at its group, or null when it is drawn from its shapes: it turns on the
    // window, has no size, lies under a rounded clip (the texture is laid down cut square), holds copies of its subtree,
    // lies outside the window, or would take a texture too large.
    private RasterRun StartRaster(int groupIndex, Rect2D fullScissor)
    {
        var element = _groups[groupIndex].Component;
        var world = World(element);
        var size = ApplySnap(element).RenderSize;
        if (!AlongTheAxes(world) || !(size.Width > 0) || !(size.Height > 0) || UnderARoundedClip(element))
        {
            return null;
        }

        var across = MathF.Sqrt(world.M11 * world.M11 + world.M12 * world.M12);
        var down = MathF.Sqrt(world.M21 * world.M21 + world.M22 * world.M22);
        var shown = MathF.Max(across, down) * (float)_renderScale;
        if (!(shown > 0))
        {
            return null;
        }

        var run = new RasterRun
        {
            Element = element,
            Start = groupIndex,
            End = CloneSubtreeEnd(groupIndex),
            World = world,
            FromWindow = Matrix4x4F.Invert(world),
            Scale = MathF.Pow(2f, MathF.Ceiling(MathF.Log2(shown))),
            Bounds = fullScissor,
        };

        // It is held cut by its own clip only; what cuts it above is where it is laid down.
        run.Scissor = LaidDownClip(element) is { } laidDown ? ToClipScissor(laidDown, fullScissor) : fullScissor;
        if (run.Scissor.Extent.Width == 0 || run.Scissor.Extent.Height == 0)
        {
            return null;
        }

        // What the window shows of it, in its units: the part held stays while it holds that at this scale.
        var scale = (float)_renderScale;
        var whole = new Rect(0, 0, size.Width, size.Height);
        var seen = new Rect(run.Scissor.Offset.X / scale, run.Scissor.Offset.Y / scale, run.Scissor.Extent.Width / scale,
            run.Scissor.Extent.Height / scale).TransformToAABB(run.FromWindow).Intersect(whole);
        if (seen.IsEmpty || !(seen.Width > 0) || !(seen.Height > 0))
        {
            return null;
        }

        if (!_rasterParts.TryGetValue(element.RenderId, out var held))
        {
            held = new RasterHeld();
            _rasterParts[element.RenderId] = held;
        }

        // The part held, or the part waited for, while either holds what is seen: a zoom eases to its end, the camera
        // moving by a hair each frame, and a part worked out anew each frame never stayed the same long enough.
        run.Part = held.Drawn && held.Scale == run.Scale && Holds(held.Part, seen) ? held.Part
            : held.WantedScale == run.Scale && !held.WantedPart.IsEmpty && Holds(held.WantedPart, seen) ? held.WantedPart
            : Grown(seen, RasterRoom).Intersect(whole);
        run.Width = (int)Math.Ceiling(run.Part.Width * run.Scale);
        run.Height = (int)Math.Ceiling(run.Part.Height * run.Scale);
        if (run.Width > RasterLargest || run.Height > RasterLargest)
        {
            // Without the room, then; and past that, drawn from its shapes.
            run.Part = seen;
            run.Width = (int)Math.Ceiling(run.Part.Width * run.Scale);
            run.Height = (int)Math.Ceiling(run.Part.Height * run.Scale);
            if (run.Width > RasterLargest || run.Height > RasterLargest)
            {
                return null;
            }
        }

        if (run.Width <= 0 || run.Height <= 0 || !PrintRaster(run))
        {
            return null;
        }

        _rasterShown.Add(element);
        _offscreenLayers ??= new OffscreenLayers();
        run.Draws = !_offscreenLayers.HoldsTile(element.RenderId, run.Print);
        // Only what the camera does waits: another scale or part. What it draws changing is drawn at once.
        var cameraMoved = held.Scale != run.Scale || held.Part != run.Part;
        if (run.Draws && cameraMoved && held.Drawn && run.Still && _offscreenLayers.HoldsAnyTile(element.RenderId)
            && !Settled(held, run))
        {
            // Still moving: what it holds, stretched to where the camera puts it.
            run.Scale = held.Scale;
            run.Part = held.Part;
            run.Width = held.Width;
            run.Height = held.Height;
            run.Draws = false;
            _rasterSettling = true;
            return run;
        }

        if (run.Draws)
        {
            held.Drawn = true;
            held.Scale = run.Scale;
            held.Part = run.Part;
            held.Width = run.Width;
            held.Height = run.Height;
        }

        return run;
    }

    // Whether what the frames ask the raster to hold has stayed the same for RasterSettles; asked something else, it
    // starts counting again.
    private static bool Settled(RasterHeld held, RasterRun run)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (held.WantedScale != run.Scale || held.WantedPart != run.Part || held.WantedPrint != run.Print)
        {
            held.WantedScale = run.Scale;
            held.WantedPart = run.Part;
            held.WantedPrint = run.Print;
            held.WantedSince = now;
            return false;
        }

        return now - held.WantedSince >= RasterSettles;
    }

    // The elements drawn as a raster this frame - laid down or drawn anew; the rest are drawn from their shapes.
    private readonly HashSet<IUIComponent> _rasterShown = [];

    // A frame's walk begins: what each element held is forgotten once it is drawn no more.
    private void BeginRasterFrame()
    {
        _rasterRun = null;
        _rasterSettling = false;
        if (_rasterParts.Count > _rasterShown.Count)
        {
            _rasterShownIds.Clear();
            foreach (var element in _rasterShown)
            {
                _rasterShownIds.Add(element.RenderId);
            }

            _rasterGone.Clear();
            foreach (var id in _rasterParts.Keys)
            {
                if (!_rasterShownIds.Contains(id))
                {
                    _rasterGone.Add(id);
                }
            }

            foreach (var id in _rasterGone)
            {
                _rasterParts.Remove(id);
            }
        }

        _rasterShown.Clear();
    }

    private readonly HashSet<Guid> _rasterShownIds = [];
    private readonly List<Guid> _rasterGone = [];

    private bool UnderARoundedClip(IUIComponent element)
    {
        for (var c = element; c != null; c = ApplySnap(c).RenderParent)
        {
            var s = ApplySnap(c);
            if (s.ClipToBounds && s.ClipRadii != Vector4F.Zero)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Holds(Rect outer, Rect inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static Rect Grown(Rect rect, double share) =>
        new(rect.X - rect.Width * share, rect.Y - rect.Height * share, rect.Width * (1 + 2 * share),
            rect.Height * (1 + 2 * share));

    // What the element's subtree draws, relative to the element, and the part of it held: false when it holds copies of
    // itself, which no texture follows. A change of what it draws is told by its groups being recorded again - not by the
    // identity of what they hold, which the walk renews as it draws them (a brush's snapshot, a text's colors) - and by a
    // brush repainted in place, by its paint version. Content that cannot be told apart - a living halo - is drawn anew
    // each frame.
    private bool PrintRaster(RasterRun run)
    {
        var print = PrintSeed;
        Mix(ref print, run.Scale);
        Mix(ref print, run.Part.X);
        Mix(ref print, run.Part.Y);
        Mix(ref print, run.Part.Width);
        Mix(ref print, run.Part.Height);
        var still = !MovedByTheCompositor(run.Element);
        var hasText = false;
        for (var i = run.Start; i < run.End; i++)
        {
            var group = _groups[i];
            if (group.Clones is { Count: > 0 })
            {
                return false;
            }

            Mix(ref print, group.Recorded);
            if (!ReferenceEquals(group.Component, run.Element))
            {
                MixRelative(ref print, World(group.Component) * run.FromWindow);
            }

            foreach (var unit in group.Units)
            {
                still &= MixRasterUnit(ref print, unit, ref hasText);
                if (unit.Placement is { } placement)
                {
                    MixRelative(ref print, Newest(placement));
                }
            }
        }

        // Glyphs still on their way are drawn missing: it is drawn again until none is. Not the atlas's version, which any
        // text anywhere moves - and drawing it asks for glyphs of its own, so it never stood still.
        if (hasText)
        {
            still &= !FontAtlasStore.HasPendingGlyphs;
        }

        // Not to be told apart: a print of its own each frame, which no texture holds.
        run.Print = still ? print : print ^ (ulong)++_rasterFrames;
        run.Still = still;
        return true;
    }

    private long _rasterFrames;

    private static void Mix(ref ulong print, double value) => Mix(ref print, BitConverter.DoubleToInt64Bits(value));

    // What of a draw of a raster changes without its group being recorded again: its brushes repainted in place, its
    // text's revision. False for a living halo, which changes every frame.
    private static bool MixRasterUnit(ref ulong print, IRenderUnit unit, ref bool hasText)
    {
        if (unit.RenderData?.LivingHalo != null)
        {
            return false;
        }

        switch (unit)
        {
            case RenderUnits.GeometryRenderUnit { Payload: { } shape }:
                Mix(ref print, shape.Brush?.PaintVersion ?? 0);
                Mix(ref print, shape.Pen?.Brush?.PaintVersion ?? 0);
                break;
            case RenderUnits.TextRenderUnit { Payload: { } text }:
                hasText = true;
                Mix(ref print, text.Revision);
                Mix(ref print, text.Foreground?.PaintVersion ?? 0);
                Mix(ref print, text.Background?.PaintVersion ?? 0);
                Mix(ref print, text.Stroke?.PaintVersion ?? 0);
                break;
        }

        return true;
    }

    // Where a draw stands in the element, to a 4096th of its unit: worked out through the window, the same place comes
    // back a few float steps apart as the camera moves, which is not a change.
    private static void MixRelative(ref ulong print, Matrix4x4F relative)
    {
        Mix(ref print, (long)Math.Round(relative.M11 * 4096.0));
        Mix(ref print, (long)Math.Round(relative.M12 * 4096.0));
        Mix(ref print, (long)Math.Round(relative.M21 * 4096.0));
        Mix(ref print, (long)Math.Round(relative.M22 * 4096.0));
        Mix(ref print, (long)Math.Round(relative.M41 * 4096.0));
        Mix(ref print, (long)Math.Round(relative.M42 * 4096.0));
    }

    // Opens the run's texture, and draws the subtree into it from here: under the matrix from the window to it, a texel
    // to the device pixel.
    private Rect2D OpenRasterTile(IGraphicsDevice device, RasterRun run)
    {
        var toTexels = (float)(run.Scale / _renderScale);
        var toTexture = Matrix4x4F.Identity;
        toTexture.M11 = toTexels;
        toTexture.M22 = toTexels;
        toTexture.M41 = (float)(-run.Part.X * toTexels);
        toTexture.M42 = (float)(-run.Part.Y * toTexels);
        _cloneMatrix = run.FromWindow * toTexture;
        // Its subtree is cut by its own clip only in here (CumulativeClip): what was worked out for it outside is not.
        _clipCache.Clear();
        return DrawIn(_offscreenLayers.BeginTile(device, run.Element.RenderId, run.Print, _projectionMatrix, run.Bounds,
            run.Width, run.Height));
    }

    private Rect2D CloseRasterTile(IGraphicsDevice device)
    {
        _clipCache.Clear();
        return DrawIn(_offscreenLayers.EndTile(device));
    }

    /// <summary>Whether <paramref name="unit"/> was drawn this frame into the texture of an element drawn as a raster - its
    /// own or an ancestor's - rather than into the frame's batches.</summary>
    internal bool DrawnIntoTiles(IRenderUnit unit)
    {
        if (_rasterShown.Count == 0)
        {
            return false;
        }

        for (var c = unit.Component; c != null; c = ApplySnap(c).RenderParent)
        {
            if (_rasterShown.Contains(c))
            {
                return true;
            }
        }

        return false;
    }

    // Lays the texture down where the element stands now.
    private void LayDownRaster(IGraphicsDevice device, RasterRun run)
    {
        var scale = (float)_renderScale;
        var held = new Rect(run.Part.X, run.Part.Y, run.Width / run.Scale, run.Height / run.Scale);
        var box = held.TransformToAABB(run.World);
        _offscreenLayers.LayDownTile(device, run.Element.RenderId, new Vector4F((float)box.X * scale,
            (float)box.Y * scale, (float)box.Width * scale, (float)box.Height * scale), run.Scissor);
    }
}
