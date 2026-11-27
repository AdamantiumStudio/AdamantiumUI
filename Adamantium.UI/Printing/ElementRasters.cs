using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Rendering;
using Adamantium.UI.Rendering;

namespace Adamantium.UI.Printing;

/// <summary>Pictures of the parts of an element that are not vector, made by the GPU at one resolution, unfaded - the
/// print fades them as it fades shapes. An element's shadows and glows are pictures of their own - the outer ones, and
/// its glyphs' outlines, shadows, glows and reflections, laid under its drawing, the inner ones between its fills and
/// its text - and the element itself stays vector, its text too. One whose own drawing shows a live surface or paints with a visual brush has its own drawing made a picture,
/// what it holds staying vector. A soft edge or a reflection reaches all an element holds: that one is made a picture
/// whole. Large pictures are made in squares the GPU can draw.</summary>
internal sealed class ElementRasters : IDisposable
{
    // The largest texture side the GPU is asked to draw, as for a raster on the screen.
    private const int LargestTexture = 8192;

    private readonly Dictionary<IUIComponent, List<RasterTile>> _under = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IUIComponent, List<RasterTile>> _over = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IUIComponent, List<RasterTile>> _effects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IUIComponent, List<RasterTile>> _own = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IUIComponent, List<RasterTile>> _whole = new(ReferenceEqualityComparer.Instance);

    /// <param name="scale">Device pixels to the element's unit: the print's resolution, times the scale the element is
    /// laid on the sheet at - a picture finer than the paper shows is memory for nothing.</param>
    public ElementRasters(double scale)
    {
        Scale = scale;
    }

    public double Scale { get; }

    /// <summary>The pictures <paramref name="component"/>'s own drawing is printed as; null for its shapes.</summary>
    public IReadOnlyList<RasterTile> OwnTilesOf(IUIComponent component) =>
        _own.TryGetValue(component, out var tiles) ? tiles : null;

    /// <summary>The picture of <paramref name="component"/>'s outer shadows and glows, laid under its drawing; null for
    /// none.</summary>
    public IReadOnlyList<RasterTile> BandsUnderOf(IUIComponent component) =>
        _under.TryGetValue(component, out var tiles) ? tiles : null;

    /// <summary>The picture of <paramref name="component"/>'s inner shadows and glows, laid over its fills and under its
    /// text; null for none.</summary>
    public IReadOnlyList<RasterTile> BandsOverOf(IUIComponent component) =>
        _over.TryGetValue(component, out var tiles) ? tiles : null;

    /// <summary>The picture of <paramref name="component"/>'s glyph effects - its text's outlines, shadows, glows and
    /// reflections - laid over its fills and under its glyphs; null for none.</summary>
    public IReadOnlyList<RasterTile> GlyphEffectsOf(IUIComponent component) =>
        _effects.TryGetValue(component, out var tiles) ? tiles : null;

    /// <summary>The pictures <paramref name="component"/> and all it holds are printed as; null where they are not.
    /// </summary>
    public IReadOnlyList<RasterTile> WholeTilesOf(IUIComponent component) =>
        _whole.TryGetValue(component, out var tiles) ? tiles : null;

    /// <summary>Whether the effect <paramref name="component"/> wears reaches all it holds, so it is a picture whole. A
    /// masked element whose own drawing is a picture is one whole as well: its mask, in the picture, reaches what it
    /// holds once.</summary>
    public static bool NeedsWholeRaster(IUIComponent component) =>
        component.SoftEdge > 0 || component.Reflection is { Shows: true };

    /// <summary>Whether <paramref name="component"/> wears a shadow or a glow.</summary>
    public static bool WearsBands(IUIComponent component) =>
        component.Shadow is { IsEnabled: true } || component.Aura is { IsEnabled: true };

    // Whether it wears an outer band, which reaches past its edges under its fills; an inner one lies over them.
    private static bool WearsOuterBand(IUIComponent component) =>
        component.Shadow is { IsEnabled: true, Inner: false } || component.Aura is { IsEnabled: true, Inner: false };

    private static bool WearsInnerBand(IUIComponent component) =>
        component.Shadow is { IsEnabled: true, Inner: true } || component.Aura is { IsEnabled: true, Inner: true };

    /// <summary>Takes the pictures of everything in <paramref name="element"/> that needs one, one after another, from
    /// <paramref name="renderer"/>. On the UI thread: the snapshots come back there.</summary>
    public async Task MakeAsync(IUIComponent element, IVisualRenderer renderer, CancellationToken cancellation)
    {
        var toElement = Matrix4x4F.Invert(element.WorldTransform);
        var pending = new Stack<IUIComponent>();
        var seen = new HashSet<IUIComponent>(ReferenceEqualityComparer.Instance);
        var children = new List<IUIComponent>();
        pending.Push(element);
        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var component = pending.Pop();

            // A tree that reaches an element twice is broken; it is walked once.
            if (component.Visibility != Visibility.Visible || !seen.Add(component))
            {
                continue;
            }

            // A picture that could not be made - no GPU to spare, no tree to read it in - is none: the element is printed
            // from its shapes instead of not at all. Each is made as fine in its own units as the sheet shows it, so one a
            // view box enlarges is made finer.
            var probe = VectorProbe.Of(component);
            var vector = probe.DrawsVector;
            var textEffects = probe.DrawsTextEffects;
            var bands = WearsBands(component) || textEffects;
            var relative = component.WorldTransform * toElement;
            var scale = Scale * Math.Sqrt(Math.Abs(relative.M11 * relative.M22 - relative.M12 * relative.M21));
            var box = BoxOf(component);

            // A masked element with a picture is one picture whole: its mask, in it, reaches what it holds once.
            if (NeedsWholeRaster(component) || (bands || !vector) && component.OpacityMask != null)
            {
                if (await SnapAsync(renderer, component, InkOf(component), scale, SnapshotContent.Subtree,
                        cancellation) is { Count: > 0 } whole)
                {
                    _whole[component] = whole;
                    continue;
                }
            }
            else if (!vector)
            {
                if (await SnapAsync(renderer, component, component.ClipToBounds ? box : Outset(component, box), scale,
                        SnapshotContent.Own, cancellation) is { Count: > 0 } own)
                {
                    _own[component] = own;
                }
            }
            else if (bands)
            {
                // Its outer bands - cut to its box by its own clip, where they still show through a fill that lets them -
                // its inner ones, which never leave its box, and its glyphs' effects, cut to its box by its own clip.
                if (WearsOuterBand(component) &&
                    await SnapAsync(renderer, component, component.ClipToBounds ? box : Outset(component, box), scale,
                        SnapshotContent.BandsUnder, cancellation) is { Count: > 0 } under)
                {
                    _under[component] = under;
                }

                if (textEffects &&
                    await SnapAsync(renderer, component, component.ClipToBounds ? box : Outset(component, box), scale,
                        SnapshotContent.GlyphEffects, cancellation) is { Count: > 0 } effects)
                {
                    _effects[component] = effects;
                }

                if (WearsInnerBand(component) &&
                    await SnapAsync(renderer, component, box, scale, SnapshotContent.BandsOver, cancellation) is
                        { Count: > 0 } over)
                {
                    _over[component] = over;
                }
            }

            RenderCache.PaintOrderOf(component, children);
            foreach (var child in children)
            {
                pending.Push(child);
            }
        }
    }

    public void Dispose()
    {
        foreach (var pictures in new[] { _under, _over, _effects, _own, _whole })
        {
            foreach (var tiles in pictures.Values)
            {
                Free(tiles);
            }

            pictures.Clear();
        }
    }

    private static void Free(List<RasterTile> tiles)
    {
        foreach (var tile in tiles)
        {
            tile.Image.Dispose();
        }
    }

    // The pictures of a region, square by square; none at all where one of them could not be made - a band left out of
    // the picture would be a hole in the print.
    private static async Task<List<RasterTile>> SnapAsync(IVisualRenderer renderer, IUIComponent component, Rect region,
        double scale, SnapshotContent content, CancellationToken cancellation)
    {
        var tiles = new List<RasterTile>();
        if (!(scale > 0))
        {
            return tiles;
        }

        foreach (var piece in PiecesOf(region, scale))
        {
            if (await SnapPieceAsync(renderer, component, piece, scale, content, cancellation) is not { } image)
            {
                Free(tiles);
                tiles.Clear();
                return tiles;
            }

            tiles.Add(new RasterTile(piece, image));
        }

        return tiles;
    }

    // One snapshot, awaited. Called off, it is let go of; a picture that comes after is nobody's and is freed.
    private static Task<ImageSource> SnapPieceAsync(IVisualRenderer renderer, IUIComponent component, Rect piece,
        double scale, SnapshotContent content, CancellationToken cancellation)
    {
        var taken = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = cancellation.Register(() => taken.TrySetCanceled(cancellation));
        renderer.RequestSnapshot(component, piece, scale, content, image =>
        {
            stop.Dispose();
            if (!taken.TrySetResult(image))
            {
                image?.Dispose();
            }
        });
        return taken.Task;
    }

    // The region split into squares no wider than the GPU draws at this scale.
    private static IEnumerable<Rect> PiecesOf(Rect region, double scale)
    {
        if (region.IsEmpty)
        {
            yield break;
        }

        // Half a pixel short of the limit: the pixels a side covers are rounded up, and never past it.
        var side = (LargestTexture - 0.5) / scale;
        for (var y = region.Y; y < region.Bottom; y += side)
        {
            for (var x = region.X; x < region.Right; x += side)
            {
                yield return new Rect(x, y, Math.Min(side, region.Right - x), Math.Min(side, region.Bottom - y));
            }
        }
    }

    private static Rect BoxOf(IUIComponent component) =>
        new(new Size<double>(component.RenderSize.Width, component.RenderSize.Height));

    // Where an element and all it holds draw, in its own units: their boxes, their shadows and glows past them, and its
    // reflection below. Its own clip cuts all of it, its shadow too; a clip inside it cuts what that one holds.
    private static Rect InkOf(IUIComponent element)
    {
        var own = BoxOf(element);
        var ink = element.ClipToBounds ? own : Outset(element, own);
        if (!element.ClipToBounds)
        {
            var toElement = Matrix4x4F.Invert(element.WorldTransform);
            var pending = new Stack<IUIComponent>(element.VisualChildren);
            var seen = new HashSet<IUIComponent>(ReferenceEqualityComparer.Instance);
            while (pending.Count > 0)
            {
                var component = pending.Pop();
                if (component.Visibility != Visibility.Visible || !seen.Add(component))
                {
                    continue;
                }

                var box = BoxOf(component);
                ink = Union(ink, (component.ClipToBounds ? box : Mirrored(component, Outset(component, box)))
                    .TransformToAABB(component.WorldTransform * toElement));
                if (!component.ClipToBounds)
                {
                    foreach (var child in component.VisualChildren)
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        return Mirrored(element, ink);
    }

    // What an element draws grown by its reflection below it, where it wears one.
    private static Rect Mirrored(IUIComponent component, Rect ink)
    {
        if (component.Reflection is not { Shows: true } reflection)
        {
            return ink;
        }

        var below = reflection.OffsetY + ink.Height * Math.Min(1, reflection.Size);
        var aside = Math.Abs(reflection.OffsetX);
        return new Rect(ink.X - aside, ink.Y, ink.Width + 2 * aside, ink.Height + Math.Max(0, below));
    }

    /// <summary>A box grown by its element's shadow, glow and glyphs' overhang, each as far as it reaches on that side:
    /// where its own drawing can reach.</summary>
    public static Rect Outset(IUIComponent component, Rect box)
    {
        double left = 0, top = 0, right = 0, bottom = 0;
        if (component.Shadow is { IsEnabled: true, Inner: false } shadow)
        {
            var reach = Math.Max(0, shadow.BlurRadius + shadow.Spread);
            left = Math.Max(left, reach - shadow.OffsetX);
            right = Math.Max(right, reach + shadow.OffsetX);
            top = Math.Max(top, reach - shadow.OffsetY);
            bottom = Math.Max(bottom, reach + shadow.OffsetY);
        }

        if (component.Aura is { IsEnabled: true, Inner: false } aura)
        {
            var reach = Math.Max(0, aura.Radius * (1 + Math.Max(0, aura.Turbulence)) + Math.Max(0, aura.Spread));
            left = Math.Max(left, reach);
            right = Math.Max(right, reach);
            top = Math.Max(top, reach);
            bottom = Math.Max(bottom, reach);
        }

        if (component is IInkOverhang { InkOverhang: var overhang })
        {
            left = Math.Max(left, overhang.Left);
            right = Math.Max(right, overhang.Right);
            top = Math.Max(top, overhang.Top);
            bottom = Math.Max(bottom, overhang.Bottom);
        }

        return new Rect(box.X - left, box.Y - top, box.Width + left + right, box.Height + top + bottom);
    }

    private static Rect Union(Rect one, Rect other)
    {
        var left = Math.Min(one.X, other.X);
        var top = Math.Min(one.Y, other.Y);
        return new Rect(left, top, Math.Max(one.Right, other.Right) - left, Math.Max(one.Bottom, other.Bottom) - top);
    }
}
