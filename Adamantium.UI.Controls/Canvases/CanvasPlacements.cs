using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>The placements a layer of an <see cref="InfiniteCanvas"/> draws its things with, one set per thing. The
/// matrices are worked out here in double - the camera's offset and scale against each thing's own origin - so what
/// reaches the GPU is a small number near the view however far the plane runs.</summary>
internal sealed class CanvasPlacements
{
    private readonly Dictionary<object, CanvasPlaced> _placed = new();
    private readonly HashSet<object> _seen = new();
    private readonly List<object> _gone = new();
    private readonly CanvasDrawing _drawing;

    public CanvasPlacements()
    {
        _drawing = new CanvasDrawing(this);
    }

    /// <summary>How many things hold placements here.</summary>
    public int Count => _placed.Count;

    /// <summary>Starts a record: what is not drawn again before <see cref="End"/> lets its placements go.</summary>
    public void Start() => _seen.Clear();

    /// <summary>The drawing for an item, its placements on the camera as it is now.</summary>
    public CanvasDrawing For(IDrawingSession session, InfiniteCanvas canvas, ICanvasItem item)
    {
        var bounds = item.Bounds;
        var turned = item as ICanvasTransformed;

        return For(session, canvas, item, new Vector2<double>(bounds.X, bounds.Y),
            turned?.Transform ?? CanvasTransform.None, turned?.Pivot ?? default);
    }

    /// <summary>The drawing for anything else on the plane - the frame round a selection - stated from
    /// <paramref name="origin"/> and turned about <paramref name="pivot"/>, both in world units.</summary>
    public CanvasDrawing For(IDrawingSession session, InfiniteCanvas canvas, object key, Vector2<double> origin,
        CanvasTransform turn, Vector2<double> pivot)
    {
        if (!_placed.TryGetValue(key, out var placed)) _placed[key] = placed = new CanvasPlaced();

        _seen.Add(key);
        placed.Origin = origin;
        placed.Turn = turn;
        placed.Pivot = pivot;
        placed.Used = 0;
        placed.Main.Matrix = Main(canvas, placed);

        _drawing.Begin(session, canvas, placed);
        return _drawing;
    }

    /// <summary>Ends a record: whatever was not drawn in it gives its placements up.</summary>
    public void End()
    {
        _gone.Clear();
        foreach (var (key, placed) in _placed)
        {
            if (!_seen.Contains(key)) _gone.Add(key);
            else if (placed.Parts.Count > placed.Used) placed.Parts.RemoveRange(placed.Used, placed.Parts.Count - placed.Used);
        }

        foreach (var key in _gone) _placed.Remove(key);
    }

    /// <summary>Puts every placement where the camera shows it now - the whole of what a pan or a zoom costs.</summary>
    public void Place(InfiniteCanvas canvas)
    {
        foreach (var placed in _placed.Values)
        {
            var main = Main(canvas, placed);
            placed.Main.Matrix = main;

            foreach (var part in placed.Parts) part.Placement.Matrix = part.Where();
        }
    }

    /// <summary>Lets every placement go.</summary>
    public void Clear() => _placed.Clear();

    /// <summary>One more placement for <paramref name="placed"/>, put wherever <paramref name="where"/> says - now and
    /// on every camera move.</summary>
    internal Placement Part(CanvasPlaced placed, Func<Matrix4x4F> where)
    {
        if (placed.Used == placed.Parts.Count) placed.Parts.Add(new CanvasPlacedPart());

        var part = placed.Parts[placed.Used++];
        part.Where = where;
        part.Placement.Matrix = where();

        return part.Placement;
    }

    private static Matrix4x4F Main(InfiniteCanvas canvas, CanvasPlaced placed)
    {
        var scale = (float)canvas.Scale;
        var at = canvas.WorldToScreen(placed.Origin);
        var camera = Matrix4x4.CreateScale<float>(scale, scale, 1) *
                     Matrix4x4.CreateTranslation<float>((float)at.X, (float)at.Y, 0);

        return placed.Turn.IsSomething ? placed.Turn.Matrix(placed.Pivot - placed.Origin) * camera : camera;
    }
}
