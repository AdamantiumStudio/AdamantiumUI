using System;
using System.Collections.Generic;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

// Elements drawn as a raster (DrawingMode.Raster): their subtree is drawn into tiles of a texture in the element's own
// space, at a scale a power of two from what the camera shows, and the tiles are laid down where the camera puts them -
// moved and scaled, not drawn again, until the scale goes a step further or what they show changes. A tile is drawn as a
// clone of the subtree is: under a matrix from the window to the tile, into a target that is the tile's own.
public partial class RenderCache
{
    // The element whose tiles are being drawn this frame, or null.
    private RasterRun _rasterRun;

    // A tile of an element at a scale: what its texture is kept under from frame to frame.
    private readonly record struct RasterTileKey(Guid Element, float Scale, int Column, int Row);

    private sealed class RasterRun
    {
        public IUIComponent Element;

        // The element's groups: [Start, End).
        public int Start;
        public int End;

        // The element's world, and its inverse - from the window to the element.
        public Matrix4x4F World;
        public Matrix4x4F FromWindow;

        // Device pixels to a unit of the element its tiles are drawn at, and a tile's side in its units.
        public float Scale;
        public double Side;

        // What its subtree draws, relative to it: its tiles hold it while this stays.
        public ulong Print;

        // What cuts it in the window, in device pixels; and the bounds the walk drew within when it began, which closing a
        // tile goes back to.
        public Rect2D Scissor;
        public Rect2D Bounds;

        // Every tile it shows now, and those of them drawn this frame.
        public readonly List<RasterTileKey> Tiles = [];
        public readonly List<RasterTileKey> ToDraw = [];
        public int Drawing;

        public bool RecordingBefore;
    }

    // The tiles of an element drawn as a raster, starting at its group, or null when it is drawn from its shapes: it
    // turns on the window, has no size, holds copies of its subtree, or lies outside the window.
    private RasterRun StartRaster(int groupIndex, Rect2D fullScissor)
    {
        var element = _groups[groupIndex].Component;
        var world = World(element);
        var size = ApplySnap(element).RenderSize;
        if (!AlongTheAxes(world) || !(size.Width > 0) || !(size.Height > 0))
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
        };
        run.Side = OffscreenLayers.TileTexels / run.Scale;
        run.Bounds = fullScissor;
        // Its tiles hold it cut by its own clip only; what cuts it above is where they are laid down.
        run.Scissor = LaidDownClip(element) is { } laidDown ? ToClipScissor(laidDown, fullScissor) : fullScissor;
        if (run.Scissor.Extent.Width == 0 || run.Scissor.Extent.Height == 0 || !PrintRaster(run))
        {
            return null;
        }

        // The tiles under what the window shows of it, in its units.
        var scale = (float)_renderScale;
        var seen = new Rect(run.Scissor.Offset.X / scale, run.Scissor.Offset.Y / scale, run.Scissor.Extent.Width / scale,
            run.Scissor.Extent.Height / scale).TransformToAABB(run.FromWindow);
        var columns = (int)Math.Ceiling(size.Width / run.Side);
        var rows = (int)Math.Ceiling(size.Height / run.Side);
        var firstColumn = Math.Max(0, (int)Math.Floor(seen.X / run.Side));
        var lastColumn = Math.Min(columns - 1, (int)Math.Floor(seen.Right / run.Side));
        var firstRow = Math.Max(0, (int)Math.Floor(seen.Y / run.Side));
        var lastRow = Math.Min(rows - 1, (int)Math.Floor(seen.Bottom / run.Side));
        _offscreenLayers ??= new OffscreenLayers();
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var key = new RasterTileKey(element.RenderId, run.Scale, column, row);
                run.Tiles.Add(key);
                if (!_offscreenLayers.HoldsTile(key, run.Print))
                {
                    run.ToDraw.Add(key);
                }
            }
        }

        return run.Tiles.Count > 0 ? run : null;
    }

    // What the element's subtree draws, relative to the element, as its layer's print is worked out: false when it
    // holds copies of itself, which no tile follows. Content that cannot be told apart from frame to frame is drawn anew
    // each frame.
    private bool PrintRaster(RasterRun run)
    {
        var print = PrintSeed;
        Mix(ref print, run.Scale);
        var still = Core.Media.Brush.PaintEpoch == _framePaintEpoch && !MovedByTheCompositor(run.Element);
        var hasText = false;
        for (var i = run.Start; i < run.End; i++)
        {
            var group = _groups[i];
            if (group.Clones is { Count: > 0 })
            {
                return false;
            }

            foreach (var unit in group.Units)
            {
                if (!still)
                {
                    break;
                }

                still = MixRasterContent(ref print, unit, ref hasText);
                if (!ReferenceEquals(unit.Component, run.Element))
                {
                    MixRelative(ref print, World(unit.Component) * run.FromWindow);
                }

                if (unit.Placement is { } placement)
                {
                    MixMatrix(ref print, Newest(placement));
                }
            }
        }

        // Glyphs still on their way are drawn missing: the tiles are drawn again until none is. Not the atlas's version,
        // which any text anywhere moves - and drawing the tiles asks for glyphs of their own, so it never stood still.
        if (hasText)
        {
            still &= !FontAtlasStore.HasPendingGlyphs;
        }

        // Not to be told apart: a print of its own each frame, which no tile holds.
        run.Print = still ? print : print ^ (ulong)++_rasterFrames;
        return true;
    }

    private long _rasterFrames;

    // What a draw of a raster shows, as a layer's print tells it - and the marks of its groups, its pictures and its
    // meshes by what they are: recorded anew, they are new objects, so a change is told; still, they stay. A brush that a
    // layer cannot tell apart from frame to frame - a pattern drawn by a drawing - is told by its paint version, which a
    // repaint in place moves.
    private static bool MixRasterContent(ref ulong print, IRenderUnit unit, ref bool hasText)
    {
        if (unit.RenderData?.LivingHalo != null)
        {
            return false;
        }

        switch (unit)
        {
            case RenderUnits.GroupMarkRenderUnit mark:
                Mix(ref print, Identity(mark));
                Mix(ref print, Identity(mark.Payload));
                return true;
            case RenderUnits.ImageRenderUnit or RenderUnits.ColoredMeshRenderUnit:
                Mix(ref print, Identity(unit));
                Mix(ref print, Identity(unit.RenderData));
                return true;
            case RenderUnits.GeometryRenderUnit { Payload: { } shape }:
                Mix(ref print, Identity(unit));
                Mix(ref print, Identity(unit.RenderData?.Halo));
                Mix(ref print, Identity(shape));
                MixPainted(ref print, shape.Brush);
                MixPainted(ref print, shape.Pen?.Brush);
                Mix(ref print, Identity(shape.Pen));
                return true;
            case RenderUnits.TextRenderUnit { Payload: { } text }:
                hasText = true;
                Mix(ref print, Identity(unit));
                Mix(ref print, Identity(unit.RenderData?.Halo));
                Mix(ref print, Identity(text));
                Mix(ref print, text.Revision);
                MixPainted(ref print, text.Foreground);
                MixPainted(ref print, text.Background);
                MixPainted(ref print, text.Stroke);
                return true;
            default:
                return MixContent(ref print, unit, ref hasText);
        }
    }

    private static void MixPainted(ref ulong print, Core.Media.Brush brush)
    {
        Mix(ref print, Identity(brush));
        Mix(ref print, brush?.PaintVersion ?? 0);
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

    // Opens the next tile of the run to draw, and draws the subtree into it from here: under the matrix from the window to
    // the tile, a texel to the device pixel.
    private Rect2D OpenRasterTile(IGraphicsDevice device, RasterRun run)
    {
        var key = run.ToDraw[run.Drawing];
        var toTexels = (float)(run.Scale / _renderScale);
        var toTile = Matrix4x4F.Identity;
        toTile.M11 = toTexels;
        toTile.M22 = toTexels;
        toTile.M41 = (float)(-key.Column * run.Side * toTexels);
        toTile.M42 = (float)(-key.Row * run.Side * toTexels);
        _cloneMatrix = run.FromWindow * toTile;
        return DrawIn(_offscreenLayers.BeginTile(device, key, run.Print, _projectionMatrix, run.Bounds));
    }

    private Rect2D CloseRasterTile(IGraphicsDevice device) => DrawIn(_offscreenLayers.EndTile(device));

    /// <summary>Whether <paramref name="unit"/> is drawn into the tiles of an element drawn as a raster - its own or an
    /// ancestor's - rather than into the frame's batches.</summary>
    internal bool DrawnIntoTiles(IRenderUnit unit)
    {
        for (var c = unit.Component; c != null; c = ApplySnap(c).RenderParent)
        {
            if (c.DrawingMode == DrawingMode.Raster)
            {
                return true;
            }
        }

        return false;
    }

    // Lays down every tile the run shows, where the element stands now.
    private void LayDownRaster(IGraphicsDevice device, RasterRun run)
    {
        var scale = (float)_renderScale;
        foreach (var key in run.Tiles)
        {
            var box = new Rect(key.Column * run.Side, key.Row * run.Side, run.Side, run.Side).TransformToAABB(run.World);
            _offscreenLayers.LayDownTile(device, key, new Vector4F((float)box.X * scale, (float)box.Y * scale,
                (float)box.Width * scale, (float)box.Height * scale), run.Scissor);
        }
    }
}
