using System;
using System.Collections.Generic;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf;

/// <summary>Where each draw of a page stands on it, in the page's units, for drawing only what meets a view: a draw's
/// box, its stroke's width within, and a group's box - all it draws, cut by its clip - with where the group ends.</summary>
internal sealed class PdfStepPlaces
{
    // What a draw of an unknown place - a path not prepared, a group never closed - is taken to cover: all of it.
    private static readonly Rect Everywhere = new(-1e30, -1e30, 2e30, 2e30);

    private readonly Rect[] _boxes;
    private readonly int[] _ends;

    private PdfStepPlaces(Rect[] boxes, int[] ends)
    {
        _boxes = boxes;
        _ends = ends;
    }

    /// <summary>Whether the draw at <paramref name="index"/> may meet <paramref name="view"/>.</summary>
    public bool Meets(int index, Rect view) => _boxes[index].Intersects(view);

    /// <summary>Where the group begun at <paramref name="index"/> ends - the index of its pop; -1 for any other draw.
    /// </summary>
    public int EndOf(int index) => _ends[index];

    /// <summary>Works out the places of <paramref name="steps"/>; their paths are to have been prepared, which works out
    /// their bounds.</summary>
    public static PdfStepPlaces Of(IReadOnlyList<PdfDrawStep> steps)
    {
        var boxes = new Rect[steps.Count];
        var ends = new int[steps.Count];
        var open = new Stack<Open>();
        for (var index = 0; index < steps.Count; index++)
        {
            ends[index] = -1;
            var step = steps[index];
            switch (step)
            {
                case PdfPushGroupStep:
                    open.Push(new Open { Push = index });
                    continue;
                case PdfPopGroupStep when open.Count > 0:
                {
                    var closed = open.Pop();
                    var group = (PdfPushGroupStep)steps[closed.Push];
                    var drawn = closed.Any ? closed.Box : Rect.Empty;
                    if (closed.Any && group.Clip is { } clip && !clip.Bounds.IsEmpty)
                    {
                        drawn = drawn.Intersect(Across(clip.Bounds, group.ClipMatrix));
                    }

                    boxes[closed.Push] = drawn;
                    boxes[index] = drawn;
                    ends[closed.Push] = index;
                    if (closed.Any)
                    {
                        Merge(open, drawn);
                    }

                    continue;
                }
                case PdfPopGroupStep:
                    boxes[index] = Everywhere;
                    continue;
            }

            var place = BoxOf(step);
            boxes[index] = place;
            Merge(open, place);
        }

        // A group never closed draws all it holds, wherever.
        while (open.Count > 0)
        {
            boxes[open.Pop().Push] = Everywhere;
        }

        return new PdfStepPlaces(boxes, ends);
    }

    // A group open while its draws are gone through: where it begins and the box of what it draws so far.
    private sealed class Open
    {
        public int Push;
        public Rect Box;
        public bool Any;
    }

    private static void Merge(Stack<Open> open, Rect box)
    {
        if (open.Count == 0)
        {
            return;
        }

        var group = open.Peek();
        group.Box = group.Any ? group.Box.Merge(box) : box;
        group.Any = true;
    }

    private static Rect BoxOf(PdfDrawStep step) => step switch
    {
        PdfGeometryStep { Geometry: { } geometry } path => geometry.Bounds.IsEmpty
            ? Everywhere
            : Across(Stroked(geometry.Bounds, path.Stroke), path.Matrix),
        PdfGlyphStep glyphs => Across(Stroked(glyphs.Run.Bounds, glyphs.Stroke), glyphs.Matrix),
        PdfImageStep image => Across(new Rect(0, 0, 1, 1), image.Matrix),
        PdfMeshStep mesh => Across(mesh.Mesh.Bounds, mesh.Matrix),
        _ => Everywhere,
    };

    // A stroke reaches past the path by half its width; a mitred corner by up to its miter limit times that, a square
    // cap across a slanted end by the square root of two times it; and a stroke's edge is smoothed a little past that -
    // the thinnest line, of no width, is drawn a device pixel wide.
    private static Rect Stroked(Rect box, Pen stroke) => stroke == null
        ? box
        : box.Inflate(stroke.Thickness / 2 * Math.Max(Math.Sqrt(2), stroke.MiterLimit) + EdgeReach);

    // How far past a draw's outline its smoothed edge may reach, in the page's units: more than a device pixel at any zoom
    // a page is shown at whole.
    private const double EdgeReach = 1;

    // The box round a box placed by an affine matrix (row vectors, the translation in the fourth row).
    private static Rect Across(Rect box, Matrix4x4F matrix)
    {
        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        for (var corner = 0; corner < 4; corner++)
        {
            var x = (corner & 1) == 0 ? box.X : box.Right;
            var y = (corner & 2) == 0 ? box.Y : box.Bottom;
            var atX = x * matrix.M11 + y * matrix.M21 + matrix.M41;
            var atY = x * matrix.M12 + y * matrix.M22 + matrix.M42;
            left = Math.Min(left, atX);
            top = Math.Min(top, atY);
            right = Math.Max(right, atX);
            bottom = Math.Max(bottom, atY);
        }

        return new Rect(left, top, right - left, bottom - top);
    }
}
