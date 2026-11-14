using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Draws items of a plane into any session, away from a canvas: one world unit is one unit of the session, and
/// nothing is put on whole pixels. The same <see cref="ICanvasItem.Render"/> a canvas draws them with, groups opened
/// out as a canvas opens them - what writes them into a file shows them as the canvas does. Each item is drawn by a
/// copy of it, so what an item keeps for its canvas is not touched. A wire and a control are not drawn: a wire's route
/// is worked out on a canvas, and a control is drawn by itself, not by its item.</summary>
public static class CanvasPicture
{
    /// <summary>Draws <paramref name="items"/> into <paramref name="session"/>, each in turn, in world units.</summary>
    public static void Draw(IEnumerable<ICanvasItem> items, IDrawingSession session)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(session);

        var places = new CanvasPlacements();
        var plane = new CanvasPlane();
        places.Start();
        foreach (var item in InfiniteCanvas.Laid(items))
        {
            if (item.Copy() is not { } copy)
            {
                continue;
            }

            copy.Render(places.For(session, null, plane, copy));
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
