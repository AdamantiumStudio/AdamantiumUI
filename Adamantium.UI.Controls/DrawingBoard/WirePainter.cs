using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>Draws a wire's curve as ink, not geometry, which would fill the geometry cache on every drag frame. One per wire
/// drawer, since brush and buffer are held by reference until the frame draws.</summary>
internal sealed class WirePainter
{
    // Enough that a curve reads as a curve rather than as a chain of chords at any zoom a wire is looked at: the
    // longest a wire ever is on screen is a viewport, and two dozen chords over that is well under a pixel of error.
    private const int Steps = 24;

    private readonly InkBrush _ink = new();
    private Vector2F[] _points;

    public void Draw(IDrawingSession session, InfiniteCanvas canvas, Vector2 from, Vector2 to, Brush stroke,
        double thickness)
    {
        ConnectionItem.Bend(from, to, out var first, out var second);
        Draw(session, canvas, from, first, second, to, stroke, thickness);
    }

    /// <summary>...along a curve whose bend is already decided - what a wire that has been ROUTED round the nodes in
    /// its way hands over, having worked out where to go.</summary>
    public void Draw(IDrawingSession session, InfiniteCanvas canvas, Vector2 from, Vector2 first, Vector2 second,
        Vector2 to, Brush stroke, double thickness)
    {
        if (session == null || canvas == null || stroke is not SolidColorBrush solid) return;

        // Never thinner than a pixel: a wire zoomed out should thin to a hair, not disappear.
        var width = thickness > 0 ? Math.Max(thickness * canvas.Scale, 1.0) : 0;
        if (width <= 0) return;

        _points ??= new Vector2F[Steps + 1];

        var lowX = Double.MaxValue;
        var lowY = Double.MaxValue;
        var highX = Double.MinValue;
        var highY = Double.MinValue;

        for (var step = 0; step <= Steps; step++)
        {
            var at = canvas.WorldToScreen(ConnectionItem.Along(from, first, second, to, step / (double)Steps));

            _points[step] = new Vector2F((float)at.X, (float)at.Y);

            if (at.X < lowX) lowX = at.X;
            if (at.Y < lowY) lowY = at.Y;
            if (at.X > highX) highX = at.X;
            if (at.Y > highY) highY = at.Y;
        }

        _ink.Points = _points;
        _ink.Count = Steps + 1;
        _ink.Color = solid.Color;
        _ink.Thickness = width;

        // The CONTENTS of the borrowed array just changed and its reference did not, so the paint has to be told - see
        // InkBrush.Revision. Without it the wire stays baked wherever it first was.
        _ink.Revision++;

        // The rectangle is the wire's own box on screen: what the record measures overlap and clipping by. The pass
        // places the curve from the points rather than from it.
        var half = width / 2 + 1;

        session.DrawRectangle(_ink, new Rect(lowX - half, lowY - half,
            highX - lowX + 2 * half, highY - lowY + 2 * half));
    }
}
