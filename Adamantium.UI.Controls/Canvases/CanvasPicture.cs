using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Draws items of a plane into any session, away from a canvas: one world unit is one unit of the session, and
/// nothing is put on whole pixels. The same <see cref="ICanvasItem.Render"/> a canvas draws them with, groups opened
/// out as a canvas opens them - what writes them into a file shows them as the canvas does.
/// <see cref="Draw(IEnumerable{ICanvasItem}, IDrawingSession)"/> draws each item by a copy of it, so what an item keeps
/// for its canvas is not touched, and a wire and a control are not drawn - neither is copied.
/// <see cref="DrawUncopied"/> draws the items themselves, for items made for the picture.
/// </summary>
public static class CanvasPicture
{
    /// <summary>Draws <paramref name="items"/> into <paramref name="session"/>, each in turn, in world units.</summary>
    public static void Draw(IEnumerable<ICanvasItem> items, IDrawingSession session) => Draw(items, session, true);

    /// <summary>Draws <paramref name="items"/> themselves, not copies - items the caller made for this and keeps for no
    /// canvas - so one that cannot be copied, a mark tied to text, is drawn too.</summary>
    public static void DrawUncopied(IEnumerable<ICanvasItem> items, IDrawingSession session) =>
        Draw(items, session, false);

    private static void Draw(IEnumerable<ICanvasItem> items, IDrawingSession session, bool copies)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(session);

        var places = new CanvasPlacements();
        var plane = new CanvasPlane();
        places.Start();
        foreach (var item in InfiniteCanvas.Laid(items))
        {
            if ((copies ? item.Copy() : item) is not { } drawn)
            {
                continue;
            }

            drawn.Render(places.For(session, null, plane, drawn));
        }

        places.End();
    }

    private sealed class CanvasPlane : ICanvasCamera
    {
        public double Scale => 1;

        public bool SnapsToPixels => false;

        public Vector2<double> WorldToScreen(Vector2<double> world) => world;
    }
}
