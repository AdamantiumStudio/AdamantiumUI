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
            var geometry = _path.ToGeometry(rule);
            var matrix = PdfMatrices.ToDrawing(_state.Ctm);
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
                    Write(new PdfGeometryStep(geometry, fillBrush, pen, matrix), frame.Output);
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
