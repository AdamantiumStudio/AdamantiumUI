using Adamantium.Mathematics;
using Adamantium.Pdf.Content;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Pdf.Steps;

namespace Adamantium.UI.Pdf.Interpretation;

// Path construction and painting (ISO 32000-2, 8.5): the path is painted in user space and placed by the CTM, so a
// line's width scales with it as PDF says; a W before the painting operator cuts all that follows by the path.
internal sealed partial class PdfPageInterpreter
{
    private readonly PdfPathBuilder _path = new();
    private FillRule? _pendingClip;

    // The grid a painted path is placed on, in the page's units.
    private const double PlacementGrid = 256;

    // A path placed by a translation of its own - a map's exporter puts each in place by a cm of its own - is placed by
    // the corner of the grid's square it falls in instead, its points moved by the rest: the paths of one part of the page
    // then share a matrix, and PdfPathJoin can join those of one paint. Returns how far to move the points, in user
    // space; nothing for a matrix that squashes the page flat.
    private static Vector2<double> OnPlacementGrid(ref Matrix4x4F matrix)
    {
        double a = matrix.M11, b = matrix.M12, c = matrix.M21, d = matrix.M22;
        var determinant = a * d - b * c;
        if (Math.Abs(determinant) < 1e-12)
        {
            return default;
        }

        var cornerX = Math.Floor(matrix.M41 / PlacementGrid) * PlacementGrid;
        var cornerY = Math.Floor(matrix.M42 / PlacementGrid) * PlacementGrid;
        var restX = matrix.M41 - cornerX;
        var restY = matrix.M42 - cornerY;
        matrix.M41 = (float)cornerX;
        matrix.M42 = (float)cornerY;

        // The rest in user space: the point that the matrix's linear part takes to it.
        return new Vector2<double>((restX * d - restY * c) / determinant, (restY * a - restX * b) / determinant);
    }

    private bool PathOperator(PdfContentOperation operation, PdfFrame frame)
    {
        switch (operation.Operator)
        {
            case "m":
                _path.MoveTo(operation.Number(0), operation.Number(1));
                return true;
            case "l":
                _path.LineTo(operation.Number(0), operation.Number(1));
                return true;
            case "c":
                _path.CurveTo(operation.Number(0), operation.Number(1), operation.Number(2), operation.Number(3),
                    operation.Number(4), operation.Number(5));
                return true;
            case "v":
                _path.CurveFromCurrent(operation.Number(0), operation.Number(1), operation.Number(2),
                    operation.Number(3));
                return true;
            case "y":
                _path.CurveTo(operation.Number(0), operation.Number(1), operation.Number(2), operation.Number(3),
                    operation.Number(2), operation.Number(3));
                return true;
            case "h":
                _path.Close();
                return true;
            case "re":
                _path.Rectangle(operation.Number(0), operation.Number(1), operation.Number(2), operation.Number(3));
                return true;
            case "W":
                _pendingClip = FillRule.NonZero;
                return true;
            case "W*":
                _pendingClip = FillRule.EvenOdd;
                return true;
            case "n":
                EndPath(frame);
                return true;
            case "f":
            case "F":
                Paint(frame, FillRule.NonZero, fill: true, stroke: false);
                return true;
            case "f*":
                Paint(frame, FillRule.EvenOdd, fill: true, stroke: false);
                return true;
            case "S":
                Paint(frame, FillRule.NonZero, fill: false, stroke: true);
                return true;
            case "s":
                _path.Close();
                Paint(frame, FillRule.NonZero, fill: false, stroke: true);
                return true;
            case "B":
                Paint(frame, FillRule.NonZero, fill: true, stroke: true);
                return true;
            case "B*":
                Paint(frame, FillRule.EvenOdd, fill: true, stroke: true);
                return true;
            case "b":
                _path.Close();
                Paint(frame, FillRule.NonZero, fill: true, stroke: true);
                return true;
            case "b*":
                _path.Close();
                Paint(frame, FillRule.EvenOdd, fill: true, stroke: true);
                return true;
            default:
                return false;
        }
    }

    private void Paint(PdfFrame frame, FillRule rule, bool fill, bool stroke)
    {
        if (!_path.IsEmpty && !Hidden)
        {
            var matrix = PdfMatrices.ToDrawing(_state.Ctm);

            // A W waiting cuts by the path's points as they are, after this: they are moved only when nothing else
            // takes them.
            var geometry = _pendingClip == null
                ? _path.ToGeometry(rule, OnPlacementGrid(ref matrix))
                : _path.ToGeometry(rule);
            var fillBrush = fill ? FillBrush() : null;
            var pen = stroke ? StrokePen() : null;
            if (fillBrush != null || pen != null)
            {
                var grouped = BeginObjectGroup(frame.Output);
                // A pattern paints through a group cut to the path; a color is the path's own brush.
                if (fill && _state.Fill.Pattern != null)
                {
                    PaintPattern(_state.Fill, geometry, matrix, frame, stroking: false);
                    fillBrush = null;
                }

                if (stroke && _state.Stroke.Pattern != null)
                {
                    Tell("A stroke painted with a pattern is drawn with its pattern's base color.");
                }

                if (fillBrush != null || pen != null)
                {
                    Write(new PdfGeometryStep(geometry, fillBrush, pen, matrix) { IsPagePath = true }, frame.Output);
                }

                EndObjectGroup(grouped, frame.Output);
            }
        }

        EndPath(frame);
    }

    // The path ends; a W before it now cuts what follows by the path as it was.
    private void EndPath(PdfFrame frame)
    {
        if (_pendingClip is { } rule)
        {
            // A clip to an empty path leaves nothing to draw.
            ClipByPath(_path.IsEmpty ? new RectangleGeometry(new Rect(0, 0, 0, 0)) : _path.ToGeometry(rule),
                PdfMatrices.ToDrawing(_state.Ctm), frame);
        }

        _pendingClip = null;
        _path.Clear();
    }

    private void ClipByPath(Geometry clip, Matrix4x4F matrix, PdfFrame frame)
    {
        OpenGroup(new PdfPushGroupStep { Clip = clip, ClipMatrix = matrix }, frame.Output);
        _state.OpenGroups = _openGroups;
    }
}
