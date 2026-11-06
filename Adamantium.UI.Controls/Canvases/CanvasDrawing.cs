using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Where one item on an <see cref="InfiniteCanvas"/> draws. The item states its drawing in WORLD units relative
/// to <see cref="Origin"/>, through <see cref="Placement"/>; the layer drawing it keeps that placement on the camera, so
/// a pan or a zoom rewrites a matrix and records nothing.</summary>
public sealed class CanvasDrawing
{
    /// <summary>The least anything on the plane is drawn wide, in device pixels, however far it is zoomed out: an outline
    /// or ink thins to a hair rather than vanishing.</summary>
    public const double Hairline = 1;

    private readonly CanvasPlacements _places;
    private CanvasPlaced _placed;

    internal CanvasDrawing(CanvasPlacements places)
    {
        _places = places;
    }

    /// <summary>The session to draw into - always with one of this drawing's placements.</summary>
    public IDrawingSession Session { get; private set; }

    /// <summary>The canvas the item stands on.</summary>
    public InfiniteCanvas Canvas { get; private set; }

    /// <summary>The world point the drawing is stated from.</summary>
    public Vector2<double> Origin => _placed.Origin;

    /// <summary>Carries what is drawn in local units - see <see cref="Local(Vector2{double})"/> - to the screen: the
    /// camera and the item's own turn.</summary>
    public Placement Placement => _placed.Main;

    /// <summary>A world point in the units this drawing is stated in.</summary>
    public Vector2<double> Local(Vector2<double> world) => world - _placed.Origin;

    /// <summary>A world box in the units this drawing is stated in.</summary>
    public Rect Local(Rect world) => new(world.X - _placed.Origin.X, world.Y - _placed.Origin.Y, world.Width, world.Height);

    /// <summary>A placement that first puts what is drawn through <paramref name="local"/> - into the drawing's own
    /// units - and then moves and scales with the item: a contour stated in its file's units, fitted to the item's box.
    /// </summary>
    public Placement PartWith(Matrix4x4F local)
    {
        var placed = _placed;
        return _places.Part(placed, () => local * placed.Main.Matrix);
    }

    /// <summary>A placement put wherever <paramref name="where"/> says for the camera as it is when asked - for chrome
    /// that is neither scaled with the plane nor pinned to one point of it.</summary>
    internal Placement PartBy(Func<Matrix4x4F> where) => _places.Part(_placed, where);

    internal void Begin(IDrawingSession session, InfiniteCanvas canvas, CanvasPlaced placed)
    {
        Session = session;
        Canvas = canvas;
        _placed = placed;
    }
}
