using System;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Draws a wire's curve as ink, not geometry, which would fill the geometry cache on every drag frame. One per wire
/// drawer, since brush and buffer are held by reference until the frame draws.</summary>
internal sealed class WirePainter
{
    // Enough that a curve reads as a curve rather than as a chain of chords at any zoom a wire is looked at: the
    // longest a wire ever is on screen is a viewport, and two dozen chords over that is well under a pixel of error.
    private const int Steps = 24;

    private readonly InkBrush _ink = new();
    private Vector2F[] _points;

    public void Draw(CanvasDrawing drawing, Vector2<double> from, Vector2<double> to, Brush stroke, double thickness)
    {
        ConnectionItem.Bend(from, to, out var first, out var second);
        Draw(drawing, from, first, second, to, stroke, thickness);
    }

    /// <summary>...along a curve whose bend is already decided - what a wire that has been ROUTED round the nodes in
    /// its way hands over, having worked out where to go. All the points in world units.</summary>
    public void Draw(CanvasDrawing drawing, Vector2<double> from, Vector2<double> first, Vector2<double> second,
        Vector2<double> to, Brush stroke, double thickness)
    {
        if (drawing == null || stroke is not SolidColorBrush solid || thickness <= 0) return;

        _points ??= new Vector2F[Steps + 1];

        var lowX = Double.MaxValue;
        var lowY = Double.MaxValue;
        var highX = Double.MinValue;
        var highY = Double.MinValue;

        for (var step = 0; step <= Steps; step++)
        {
            var at = drawing.Local(ConnectionItem.Along(from, first, second, to, step / (double)Steps));

            _points[step] = new Vector2F((float)at.X, (float)at.Y);

            if (at.X < lowX) lowX = at.X;
            if (at.Y < lowY) lowY = at.Y;
            if (at.X > highX) highX = at.X;
            if (at.Y > highY) highY = at.Y;
        }

        _ink.Points = _points;
        _ink.Count = Steps + 1;
        _ink.Color = solid.Color;
        _ink.Thickness = thickness;

        // Never thinner than a pixel: a wire zoomed out should thin to a hair, not disappear.
        _ink.MinimumDeviceThickness = CanvasDrawing.Hairline;

        // The CONTENTS of the borrowed array just changed and its reference did not, so the paint has to be told - see
        // InkBrush.Revision. Without it the wire stays baked wherever it first was.
        _ink.Revision++;

        // The rectangle is the wire's own box: what the record measures overlap and clipping by. The pass places the
        // curve from the points rather than from it.
        var half = thickness / 2;

        drawing.Session.DrawRectangle(_ink, new Rect(lowX - half, lowY - half,
            highX - lowX + 2 * half, highY - lowY + 2 * half), CornerRadius.Empty, null, drawing.Placement);
    }
}
