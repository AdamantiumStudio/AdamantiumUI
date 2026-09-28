using System;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Adorners;

/// <summary>A selection frame with corner handles, drawn just inside the adorned element's bounds so an element touching the
/// window edge does not lose it off-window.</summary>
public class SelectionAdorner : Adorner
{
    private const double FrameThickness = 4;
    private const double HandleSize = 6.0;    // corner handle square (side length)

    public SelectionAdorner(UIComponent adornedElement) : base(adornedElement)
    {
    }

    /// <summary>Frame + handle outline colour. Default a designer blue.</summary>
    public Brush Stroke { get; set; } = new SolidColorBrush(Colors.CornflowerBlue);

    /// <summary>Corner handle fill. Default white.</summary>
    public Brush HandleFill { get; set; } = new SolidColorBrush(Colors.White);

    // The theme's SelectionAdorner template wraps the whole element; the stage sizes it to the adorned bounds.
    public override bool FillsAdornedBounds => true;

    protected override void OnRender(IDrawingContext context)
    {
        if (Template != null) return;   // a themed template draws the frame + handles - this OnRender is the no-theme fallback

        // Inset the frame by half the stroke so the whole 4px line sits within the bounds (never clipped at a window edge).
        var b = AdornedBounds;
        var half = FrameThickness / 2.0;
        var frame = new Rect(b.X + half, b.Y + half,
            Math.Max(0, b.Width - FrameThickness), Math.Max(0, b.Height - FrameThickness));
        var session = context.ForControl(this);
        var pen = new Pen(Stroke, FrameThickness);

        // Outlined frame (transparent fill = outline only).
        session.DrawRectangle(Brushes.Transparent, frame, pen);

        // Corner handles: small filled squares tucked INTO each inner corner of the frame (so they also stay in bounds).
        var hs = HandleSize;
        var handlePen = new Pen(Stroke, 1.0);
        Rect[] handles =
        [
            new Rect(frame.X, frame.Y, hs, hs),
            new Rect(frame.X + frame.Width - hs, frame.Y, hs, hs),
            new Rect(frame.X, frame.Y + frame.Height - hs, hs, hs),
            new Rect(frame.X + frame.Width - hs, frame.Y + frame.Height - hs, hs, hs)
        ];
        foreach (var h in handles)
            session.DrawRectangle(HandleFill, h, handlePen);
    }
}
