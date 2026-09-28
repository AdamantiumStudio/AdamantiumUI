using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>A scene that keeps its items in a list and rejects by bounds. Linear on purpose; a spatial index can replace it
/// behind <see cref="ICanvasScene"/> without touching the canvas.</summary>
public class CanvasScene : ICanvasScene
{
    private readonly List<ICanvasItem> _items = new();

    public IReadOnlyList<ICanvasItem> Items => _items;

    public event EventHandler Changed;

    public IEnumerable<ICanvasItem> ItemsIn(Rect world)
    {
        foreach (var item in _items)
        {
            if (Meets(item.Bounds, world)) yield return item;
        }
    }

    public void Add(ICanvasItem item)
    {
        if (item == null) return;

        _items.Add(item);
        Settled();
    }

    public bool Remove(ICanvasItem item)
    {
        if (!_items.Remove(item)) return false;

        Settled();
        return true;
    }

    public bool Replace(ICanvasItem item, IReadOnlyList<ICanvasItem> pieces)
    {
        var at = _items.IndexOf(item);
        if (at < 0) return false;

        _items.RemoveAt(at);
        for (var i = 0; i < pieces.Count; i++) _items.Insert(at + i, pieces[i]);

        Settled();
        return true;
    }

    public bool Fold(IReadOnlyList<ICanvasItem> gathered, ICanvasItem into)
    {
        if (gathered == null || gathered.Count == 0 || into == null) return false;

        // Where the topmost of them stood, before anything moves: at the end of the list a group would rise above
        // everything put on the plane after its contents were.
        var at = _items.IndexOf(gathered[^1]);
        if (at < 0) return false;

        var taken = new HashSet<ICanvasItem>(gathered);

        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (!taken.Contains(_items[i])) continue;

            _items.RemoveAt(i);
            if (i < at) at--;
        }

        _items.Insert(Math.Clamp(at, 0, _items.Count), into);
        Settled();
        return true;
    }

    public void Clear()
    {
        if (_items.Count == 0) return;

        _items.Clear();
        Settled();
    }

    public void Reset(IReadOnlyList<ICanvasItem> items)
    {
        _items.Clear();
        if (items != null) _items.AddRange(items);

        Settled();
    }

    /// <summary>The topmost item under a world point, or nothing. Paint order is the list's order, so the LAST one that
    /// answers is the one on top.</summary>
    public ICanvasItem HitTest(Vector2 world, double tolerance)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].HitTest(world, tolerance)) return _items[i];
        }

        return null;
    }

    /// <summary>To the end of the list, which is the front of paint order.</summary>
    public bool BringToFront(ICanvasItem item) => MoveTo(item, _items.Count - 1);

    /// <summary>To the start of the list, which is the back.</summary>
    public bool SendToBack(ICanvasItem item) => MoveTo(item, 0);

    /// <summary>Puts an item straight after or before a named neighbor in paint order; named, since "one place along" may
    /// pass an item the current mode does not show.</summary>
    public bool MoveNextTo(ICanvasItem item, ICanvasItem neighbour, bool after)
    {
        var at = item == null ? -1 : _items.IndexOf(item);
        var to = neighbour == null ? -1 : _items.IndexOf(neighbour);

        if (at < 0 || to < 0 || ReferenceEquals(item, neighbour)) return false;

        // WHERE IT LANDS once it has been taken out. Removing the item first shifts everything above it down by one,
        // so an item moving UP lands on the neighbour's own index and one moving DOWN lands just past it - the off-by-
        // one that puts a shape on the wrong side of the very thing it was sent to.
        var wanted = at < to
            ? after ? to : to - 1
            : after ? to + 1 : to;

        return MoveTo(item, wanted);
    }

    private bool MoveTo(ICanvasItem item, int index)
    {
        var at = item == null ? -1 : _items.IndexOf(item);
        if (at < 0 || at == index) return false;

        _items.RemoveAt(at);
        _items.Insert(index, item);

        Settled();
        return true;
    }

    /// <summary>Says that something already in the scene has changed - a stroke still being drawn, an item moved. The
    /// scene itself cannot notice: what an item holds is the item's business.</summary>
    /// <remarks>Nothing is renumbered here. This is the hot one - a pen being dragged says it on every mouse move - and
    /// nothing it reports can change the ORDER, which is the only thing a number describes.</remarks>
    public void Touch() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Puts an item at a layer number counted from the back among its own kind, as <see cref="Settled"/> numbers
    /// them; out-of-range numbers are clamped.</summary>
    public bool Reposition(ICanvasItem item, int place)
    {
        if (item == null || !_items.Contains(item)) return false;

        // WHERE THE NTH OF THIS KIND STANDS in the list as a whole. Asked of the list rather than worked out, because
        // the two kinds are interleaved in whatever order they were put there.
        var wanted = Math.Max(0, place);
        var seen = 0;
        var landing = -1;
        var last = -1;

        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Mode != item.Mode) continue;

            last = i;

            if (seen++ == wanted) landing = i;
        }

        if (last < 0) return false;

        return MoveTo(item, landing < 0 ? last : landing);
    }

    // Every change to the plane ends here, restamping each item's layer number among its own kind so it never drifts.
    private void Settled()
    {
        var drawing = 0;
        var nodes = 0;

        foreach (var item in _items)
        {
            item.Order = item.Mode == CanvasMode.Nodes ? nodes++ : drawing++;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Touching counts: a stroke exactly on the edge of the viewport is visible, and an item with no thickness in one
    // direction (a horizontal line) has a zero-height box that must still meet the world it lies in.
    private static bool Meets(Rect item, Rect world) =>
        item.X <= world.X + world.Width && item.X + item.Width >= world.X &&
        item.Y <= world.Y + world.Height && item.Y + item.Height >= world.Y;
}
