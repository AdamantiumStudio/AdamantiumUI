using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>Draws one run of an <see cref="InfiniteCanvas"/>'s scene between two controls, so drawings and controls share
/// one paint order. Draw-only; picking is the canvas's own walk.</summary>
public class CanvasDrawLayer : MeasurableUIComponent
{
    private const double RedrawZoom = 2;

    private readonly List<ICanvasItem> _items = new();
    private readonly CanvasPlacements _places = new();
    private double _drawnAt;

    /// <summary>The canvas whose items this draws. Written by the canvas when it makes the layer.</summary>
    public InfiniteCanvas Owner { get; set; }

    public CanvasDrawLayer()
    {
        IsHitTestVisible = false;
    }

    /// <summary>What this run holds now. Cheap to call: it does nothing when the run is unchanged, which is what every
    /// camera move finds.</summary>
    public void Sync(List<ICanvasItem> items)
    {
        if (Same(items)) return;

        _items.Clear();
        _items.AddRange(items);

        InvalidateRender(false);
    }

    /// <summary>What this layer paints, in the order it paints it. For anything that has to check the order without
    /// looking at pixels.</summary>
    public ICanvasItem[] Painted => _items.ToArray();

    /// <summary>Says the items have changed WITHOUT the run changing - a stroke grown by another point, a shape
    /// dragged. The list is the same objects, so <see cref="Sync"/> would do nothing.</summary>
    public void Repaint() => InvalidateRender(false);

    /// <summary>Follows the camera: every item's placement is put where the camera shows it now, and nothing is drawn
    /// again - unless the zoom has moved twice as far either way from the one the items were drawn at, where what was
    /// worked out for the screen (a curve's steps, text's shaping) is drawn again for the screen as it is.</summary>
    public void Place()
    {
        if (Owner == null) return;

        var ratio = _drawnAt > 0 ? Owner.Scale / _drawnAt : 0;
        if (ratio is >= RedrawZoom or <= 1 / RedrawZoom)
        {
            InvalidateRender(false);
            return;
        }

        _places.Place(Owner);
    }

    /// <summary>How many things this layer keeps placements for - each one a slot the camera rewrites.</summary>
    internal int Placed => _places.Count;

    private bool Same(List<ICanvasItem> items)
    {
        if (items.Count != _items.Count) return false;

        for (var i = 0; i < items.Count; i++)
        {
            if (!ReferenceEquals(items[i], _items[i])) return false;
        }

        return true;
    }

    // THE WHOLE SLOT. A layer that draws the plane covers the plane: asked how much room it wants it used to answer
    // "none" - it has no children and nothing of its own to measure - and a thing of no size draws nothing, wherever
    // its items happen to be. What it draws is placed from the camera and not from this box, so the box is simply
    // whatever it has been given.
    protected override Size<double> MeasureOverride(Size<double> availableSize) =>
        new(Double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            Double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    protected override void OnRender(IDrawingContext context)
    {
        base.OnRender(context);

        if (Owner == null || _items.Count == 0)
        {
            _places.Clear();
            return;
        }

        var session = context.ForControl(this);
        _places.Start();
        _drawnAt = Owner.Scale;

        // THE FRAME STANDS WHERE ITS OBJECT STANDS. Drawn over everything, it said nothing about where the thing it is
        // round actually is - and where a thing is in the order is the one question a person moving it is asking. So
        // the canvas is asked at each step whether the frame belongs HERE: before this run, when what is held is a
        // control in the run below, and straight after the item itself otherwise.
        if (Owner.ChromeGoesBefore(_items[0])) Owner.DrawChrome(session, _places);

        var open = Rect.Empty;
        foreach (var item in _items)
        {
            var clip = Owner.ClipOf(item);
            if (clip != open)
            {
                if (!open.IsEmpty) session.PopGroup();
                if (!clip.IsEmpty) Cut(session, clip);
                open = clip;
            }

            item.Render(_places.For(session, Owner, item));

            if (!Owner.ChromeGoesAfter(item)) continue;

            if (!open.IsEmpty) session.PopGroup();
            open = Rect.Empty;
            Owner.DrawChrome(session, _places);
        }

        if (!open.IsEmpty) session.PopGroup();

        _places.End();
    }

    private void Cut(IDrawingSession session, Rect clip) =>
        session.PushGroup(new DrawingGroupOptions
        {
            Clip = new RectangleGeometry(new Rect(0, 0, clip.Width, clip.Height)),
            ClipPlacement = _places.Clip(Owner, clip)
        });
}
