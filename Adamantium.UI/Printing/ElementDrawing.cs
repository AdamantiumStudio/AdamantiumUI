using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Rendering;

namespace Adamantium.UI.Printing;

/// <summary>An element and everything in it drawn into a print session as the screen draws them: each by its own
/// rendering, read-only, so the window is not disturbed; placed by its transform from the element; cut by its bounds -
/// what it holds inside its rounded stroke - faded and masked as on the screen, in the screen's order.</summary>
/// <remarks>A faded element's own drawing is faded as one: where two of its own draws overlap - a background under its
/// glyphs - the lower one does not show through the upper, as it does on the screen. A mask covers the element's box;
/// what a masked element draws past its box without a clip is not printed.</remarks>
internal sealed class ElementDrawing : IDrawingContext
{
    private readonly IDrawingSession _session;
    private readonly Matrix4x4F _fromElement;
    private readonly ElementRasters _rasters;

    // A list of children for each depth the walk is at, kept for the next element at that depth.
    private readonly List<List<IUIComponent>> _children = [];
    private IDrawingSession _current;

    private ElementDrawing(IDrawingSession session, Matrix4x4F fromElement, ElementRasters rasters)
    {
        _session = session;
        _fromElement = fromElement;
        _rasters = rasters;
    }

    /// <summary>Draws <paramref name="element"/> into <paramref name="session"/>, its own top left corner where
    /// <paramref name="place"/> puts the origin - upright and at its own size, without the turn or scale it is given
    /// on the screen; nothing where that scale leaves nothing to show. Only what reaches <paramref name="visible"/>, in
    /// the session's units, is drawn: an element whose box, or whose clip, lies wholly outside it is left out. An
    /// element <paramref name="rasters"/> holds pictures of is drawn as them, all it holds with it; without them, from
    /// its shapes.</summary>
    public static void Draw(IUIComponent element, IDrawingSession session, Matrix4x4F place, Rect visible,
        ElementRasters rasters)
    {
        var world = element.WorldTransform;
        // The area its plane is scaled by: none, and nothing of it shows.
        if (System.Math.Abs(world.M11 * world.M22 - world.M12 * world.M21) < 1e-12f)
        {
            return;
        }

        new ElementDrawing(session, Matrix4x4F.Invert(world) * place, rasters).Walk(element, 1, visible, 0);
    }

    public IDrawingSession ForControl(IUIComponent component) => _current;

    // Hidden or collapsed, an element and what it holds draw nothing. Fades multiply down to each element's own drawing,
    // as on the screen, except over an element drawn as a layer: that one is faded as a whole. What its clip leaves
    // outside the visible part is not drawn, as the screen leaves out what its clip leaves outside the window.
    private void Walk(IUIComponent component, double fade, Rect visible, int depth)
    {
        // As deep as the screen walks, and no deeper: past it the tree is broken.
        if (component.Visibility != Visibility.Visible || depth > RenderCache.MaxTreeDepth)
        {
            return;
        }

        var matrix = component.WorldTransform * _fromElement;
        var box = new Rect(new Size<double>(component.RenderSize.Width, component.RenderSize.Height));

        // A box under perspective has no flat bounds to test, so it is not left out, as on the screen.
        var flat = matrix.M14 == 0 && matrix.M24 == 0 && matrix.M34 == 0;
        var onSheet = box.TransformToAABB(matrix);
        if (component.ClipToBounds && flat)
        {
            visible = visible.Intersect(onSheet);
            if (visible.IsEmpty)
            {
                return;
            }
        }

        var placed = new TransformedDrawingSession(_session, matrix);
        // A picture of it and all it holds is laid down as its layer is on the screen: faded as a whole.
        if (_rasters?.WholeTilesOf(component) is { } whole)
        {
            DrawTiles(whole, placed, matrix, flat, fade * component.Opacity, visible);
            return;
        }

        var groups = 0;
        if (RenderCache.WantsLayer(component))
        {
            placed.PushGroup(new DrawingGroupOptions
            {
                Opacity = fade * component.Opacity,
                Mask = component.OpacityMask is { } mask
                    ? new GeometryDrawing { Geometry = new RectangleGeometry(box), Brush = mask }
                    : null,
                MaskKind = MaskKind.Alpha,
            });
            groups++;
            fade = 1;
        }
        else
        {
            fade *= component.Opacity;
        }

        var radii = component.ClipToBounds ? component.ClipRadii : Vector4F.Zero;
        if (component.ClipToBounds)
        {
            placed.PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(box, Corners(radii, 0)) });
            groups++;
        }

        // Its own drawing as its pictures - its shadow with it, left out tile by tile - or from its shapes.
        if (_rasters?.OwnTilesOf(component) is { } own)
        {
            DrawTiles(own, placed, matrix, flat, fade * component.SelfOpacity, visible);
        }
        else if (!flat || Reaches(ElementRasters.Outset(component, box).TransformToAABB(matrix), visible))
        {
            DrawOwn(component, placed, matrix, flat, fade * component.SelfOpacity, visible);
        }

        // What it holds is cut inside its rounded stroke; where the stroke leaves nothing inside, by its edge alone.
        var inset = radii != Vector4F.Zero ? component.ClipInset : 0;
        if (inset > 0 && box.Width > 2 * inset && box.Height > 2 * inset)
        {
            placed.PushGroup(new DrawingGroupOptions
            {
                Clip = new RectangleGeometry(
                    new Rect(inset, inset, box.Width - 2 * inset, box.Height - 2 * inset), Corners(radii, inset)),
            });
            groups++;
        }

        if (_children.Count == depth)
        {
            _children.Add([]);
        }

        var children = _children[depth];
        RenderCache.PaintOrderOf(component, children);
        for (var i = 0; i < children.Count; i++)
        {
            Walk(children[i], fade, visible, depth + 1);
        }

        for (var i = 0; i < groups; i++)
        {
            _session.PopGroup();
        }
    }

    // Pictures laid down where they were taken, faded as shapes would be: they were taken unfaded.
    private static void DrawTiles(IReadOnlyList<RasterTile> tiles, IDrawingSession placed, Matrix4x4F matrix, bool flat,
        double fade, Rect visible)
    {
        var faded = fade < 1;
        if (faded)
        {
            placed.PushGroup(new DrawingGroupOptions { Opacity = fade });
        }

        LayTiles(tiles, placed, matrix, flat, visible);
        if (faded)
        {
            placed.PopGroup();
        }
    }

    // The squares of a picture, each that reaches the visible part.
    private static void LayTiles(IReadOnlyList<RasterTile> tiles, IDrawingSession placed, Matrix4x4F matrix, bool flat,
        Rect visible)
    {
        foreach (var tile in tiles)
        {
            if (!flat || Reaches(tile.Region.TransformToAABB(matrix), visible))
            {
                // The region's share of the picture, which runs to the whole pixel past it.
                var share = new Rect(0, 0, System.Math.Min(1, tile.Region.Width / tile.Image.Width),
                    System.Math.Min(1, tile.Region.Height / tile.Image.Height));
                placed.DrawImage(tile.Image, null, tile.Region, new CornerRadius(0), share);
            }
        }
    }

    // Its own drawing from its shapes, with its shadows and glows as the screen lays them: the outer ones under it, the
    // inner ones and its glyphs' effects over its fills and under its glyphs - so the drawing goes down in two goes
    // around them. A session that draws glyph effects itself, as the screen's preview does, gets no picture of them.
    private void DrawOwn(IUIComponent component, IDrawingSession placed, Matrix4x4F matrix, bool flat, double fade,
        Rect visible)
    {
        var faded = fade < 1;
        if (faded)
        {
            placed.PushGroup(new DrawingGroupOptions { Opacity = fade });
        }

        if (_rasters?.BandsUnderOf(component) is { } under)
        {
            LayTiles(under, placed, matrix, flat, visible);
        }

        var over = _rasters?.BandsOverOf(component);
        var effects = placed.DrawsGlyphEffects ? null : _rasters?.GlyphEffectsOf(component);
        if (over != null || effects != null)
        {
            _current = new AroundTextSession(placed, false);
            component.RenderReadOnly(this);
            if (over != null)
            {
                LayTiles(over, placed, matrix, flat, visible);
            }

            if (effects != null)
            {
                LayTiles(effects, placed, matrix, flat, visible);
            }

            _current = new AroundTextSession(placed, true);
            component.RenderReadOnly(this);
        }
        else
        {
            _current = placed;
            component.RenderReadOnly(this);
        }

        if (faded)
        {
            _session.PopGroup();
        }
    }

    // Whether a box touches the visible part - edges count: a line has a box with no height.
    private static bool Reaches(Rect box, Rect visible) =>
        box.X <= visible.Right && box.Right >= visible.X && box.Y <= visible.Bottom && box.Bottom >= visible.Y;

    // A clip's corners (top left, top right, bottom right, bottom left), each brought in by the inset.
    private static CornerRadius Corners(Vector4F radii, double inset) =>
        new(System.Math.Max(0, radii.X - inset), System.Math.Max(0, radii.Y - inset),
            System.Math.Max(0, radii.Z - inset), System.Math.Max(0, radii.W - inset));
}
