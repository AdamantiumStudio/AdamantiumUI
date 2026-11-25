using Adamantium.Pdf.Document;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;

namespace Adamantium.UI.Pdf;

// A pen PDF cannot stroke in one go: one cap for both ends of every dash is all PDF has, so a pen with a trim, dashes, or
// ends unlike each other or PDF's is stroked as the stroke shader lays it (StrokeRuns) - runs stroked without caps; the
// caps drawn as shapes, a concave one bitten out of its run.
public sealed partial class PdfContentSession
{
    /// <summary>Whether PDF strokes the pen as it is: no trim, no dashes, one cap for both ends and of PDF's kinds.
    /// </summary>
    private static bool StrokesAsIs(Pen pen) =>
        pen.TrimStart <= 0 && pen.TrimEnd >= 1 && (pen.DashStrokeArray?.Count ?? 0) == 0 &&
        pen.StartLineCap == pen.EndLineCap && pen.StartLineCap is PenLineCap.Flat or PenLineCap.Square or
            PenLineCap.ConvexRound;

    /// <summary>Lengths of a pen measured in the window's units, in the units of the drawing it strokes: the line stays
    /// as wide on the sheet whatever the drawing is scaled by.</summary>
    private double PenUnit(Pen pen, Matrix4x4F transform)
    {
        if (pen.ThicknessUnit != PenThicknessUnit.Window)
        {
            return 1;
        }

        var toRoot = Matrix(transform).Then(_inners.Count > 0 ? _inners.Peek() : PdfMatrix.Identity);
        var scale = Math.Sqrt(Math.Abs(toRoot.A * toRoot.D - toRoot.B * toRoot.C));
        return scale > 1e-12 ? 1 / scale : 1;
    }

    /// <summary>Strokes the pen's runs along <paramref name="curves"/>, in the content's current units: the bites of
    /// concave ends cut out first, the runs stroked without caps, then the convex ends filled with the same paint.
    /// <paramref name="paintCaps"/> fills the path given with the stroke's paint.</summary>
    /// <param name="reach">How far past the line the stroke reaches - its miters and ends: what a run's bites leave
    /// uncut around it.</param>
    private bool StrokeRuns(Pen pen, PathCurves curves, double width, double reach, Action paintCaps)
    {
        reach = Math.Max(reach, width) * 2;
        var laid = Core.Graphics.Vector.StrokeRuns.Lay(pen, curves, width);
        var runs = laid.Runs;
        if (runs.Count == 0)
        {
            return false;
        }

        var content = Content;
        var path = new PdfVectorPath(content);
        var half = width / 2;

        // A run whose two ends are alike and of PDF's kinds is stroked with PDF's own cap, in one piece with its line;
        // the rest without caps, their ends drawn as shapes. A run with a concave end goes alone, its bites cut from it
        // only - as on screen, where a bite takes from its own run and no other.
        var drawn = new bool[runs.Count];
        for (var index = 0; index < runs.Count; index++)
        {
            drawn[index] = runs[index].IsConcave;
        }

        foreach (var cap in (ReadOnlySpan<int>)[0, 1, 2, -1])
        {
            var any = false;
            for (var index = 0; index < runs.Count; index++)
            {
                if (drawn[index] || NativeCap(runs[index]) != cap)
                {
                    continue;
                }

                if (!any)
                {
                    content.LineCap(Math.Max(cap, 0));
                    any = true;
                }

                laid.WriteRun(path, runs[index], cap == 1);
                drawn[index] = true;
            }

            if (any)
            {
                content.Stroke();
            }
        }

        var convex = false;
        foreach (var run in runs)
        {
            if (NativeCap(run) < 0 && !run.IsConcave)
            {
                convex |= laid.WriteEnds(path, run, half, false);
            }
        }

        if (convex)
        {
            paintCaps();
        }

        var box = curves.Bounds;
        foreach (var run in runs)
        {
            if (!run.IsConcave)
            {
                continue;
            }

            content.SaveState();
            content.Rectangle(box.X - reach, box.Y - reach, box.Width + reach * 2, box.Height + reach * 2);
            laid.WriteEnds(path, run, half, true);
            content.Clip(true);
            content.LineCap(0);
            laid.WriteRun(path, run);
            content.Stroke();
            if (laid.WriteEnds(path, run, half, false))
            {
                paintCaps();
            }

            content.RestoreState();
        }

        return true;
    }

    /// <summary>The PDF cap a run is stroked with when both its ends are alike and PDF has their kind - 0 butt, 1 round,
    /// 2 projecting square; -1 when its ends are drawn as shapes. A square dot is a shape: PDF does not say which way a
    /// square cap turns on a point.</summary>
    private static int NativeCap(StrokeRun run) => run.StartCap != run.EndCap
        ? -1
        : run.StartCap switch
        {
            PenLineCap.Flat => 0,
            PenLineCap.ConvexRound => 1,
            PenLineCap.Square when run.End > run.Start => 2,
            _ => -1,
        };
}
