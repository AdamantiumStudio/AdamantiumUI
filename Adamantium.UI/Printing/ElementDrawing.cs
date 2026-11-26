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

    // A list of children for each depth the walk is at, kept for the next element at that depth.
    private readonly List<List<IUIComponent>> _children = [];
    private IDrawingSession _current;

    private ElementDrawing(IDrawingSession session, Matrix4x4F fromElement)
    {
        _session = session;
        _fromElement = fromElement;
    }

    /// <summary>Draws <paramref name="element"/> into <paramref name="session"/>, its own top left corner where
    /// <paramref name="place"/> puts the origin - upright and at its own size, without the turn or scale it is given
    /// on the screen; nothing where that scale leaves nothing to show. Only what reaches <paramref name="visible"/>, in
    /// the session's units, is drawn: an element whose box, or whose clip, lies wholly outside it is left out.</summary>
    public static void Draw(IUIComponent element, IDrawingSession session, Matrix4x4F place, Rect visible)
    {
        var world = element.WorldTransform;
        // The area its plane is scaled by: none, and nothing of it shows.
        if (System.Math.Abs(world.M11 * world.M22 - world.M12 * world.M21) < 1e-12f)
        {
            return;
        }

        new ElementDrawing(session, Matrix4x4F.Invert(world) * place).Walk(element, 1, visible, 0);
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

        if (!flat || Reaches(InkOf(component, box).TransformToAABB(matrix), visible))
        {
            DrawOwn(component, placed, fade * component.SelfOpacity);
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

    private void DrawOwn(IUIComponent component, IDrawingSession placed, double fade)
    {
        var faded = fade < 1;
        if (faded)
        {
            placed.PushGroup(new DrawingGroupOptions { Opacity = fade });
        }

        _current = placed;
        component.RenderReadOnly(this);
        if (faded)
        {
            _session.PopGroup();
        }
    }

    // Where an element's drawing reaches: its box, and past it where its ink overhangs - a glyph's ascender, an italic.
    private static Rect InkOf(IUIComponent component, Rect box) =>
        component is IInkOverhang { InkOverhang: var overhang }
            ? new Rect(box.X - overhang.Left, box.Y - overhang.Top, box.Width + overhang.Left + overhang.Right,
                box.Height + overhang.Top + overhang.Bottom)
            : box;

    // Whether a box touches the visible part - edges count: a line has a box with no height.
    private static bool Reaches(Rect box, Rect visible) =>
        box.X <= visible.Right && box.Right >= visible.X && box.Y <= visible.Bottom && box.Bottom >= visible.Y;

    // A clip's corners (top left, top right, bottom right, bottom left), each brought in by the inset.
    private static CornerRadius Corners(Vector4F radii, double inset) =>
        new(System.Math.Max(0, radii.X - inset), System.Math.Max(0, radii.Y - inset),
            System.Math.Max(0, radii.Z - inset), System.Math.Max(0, radii.W - inset));
}
