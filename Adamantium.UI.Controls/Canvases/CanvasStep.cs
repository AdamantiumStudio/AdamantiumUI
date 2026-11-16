using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Canvases;

/// <summary>One undoable step: what the drawing held before and what it holds now.</summary>
internal sealed class CanvasStep : ICanvasStep
{
    private readonly List<ICanvasItem> _before;
    private readonly List<ICanvasItem> _after;
    private readonly Dictionary<ICanvasItem, Rect> _wasAt;
    private readonly Dictionary<ICanvasItem, Rect> _isAt;
    private List<object> _targets;

    internal CanvasStep(string reason, List<ICanvasItem> before, List<ICanvasItem> after,
        Dictionary<ICanvasItem, Rect> wasAt, Dictionary<ICanvasItem, Rect> isAt)
    {
        Reason = reason ?? string.Empty;
        _before = before;
        _after = after;
        _wasAt = wasAt;
        _isAt = isAt;
    }

    public string Reason { get; }

    /// <summary>The items it added, took away, moved, or put in another place among the others - only those that left
    /// the order of the rest, so one carried to the front is named and not every item it passed. Worked out once, when
    /// first asked.</summary>
    public IReadOnlyList<object> Targets => _targets ??= WhatChanged();

    /// <summary>What the scene holds and where each thing is, right now. The one place either half of a step is taken,
    /// so the canvas's gesture steps and an application's one-shot steps cannot disagree about what a step is.</summary>
    internal static void Snapshot(ICanvasScene scene, out List<ICanvasItem> items, out Dictionary<ICanvasItem, Rect> at)
    {
        items = new List<ICanvasItem>(scene.Items);
        at = new Dictionary<ICanvasItem, Rect>(items.Count);

        foreach (var item in items) at[item] = item.Bounds;
    }

    /// <summary>Whether anything actually happened. A gesture that changed nothing - a click that selected and let go -
    /// must not fill the history with steps that undo to the same drawing.</summary>
    internal bool IsSomething => _before.Count != _after.Count || Moved() || Reordered();

    public void Apply(ICanvasScene scene, bool forward)
    {
        scene.Reset(forward ? _after : _before);

        // Position LAST: what is put back has to be in the scene before it is asked to move, and an item that left the
        // drawing has nowhere to be moved to.
        foreach (var (item, box) in forward ? _isAt : _wasAt) PutBack(item, box);

        scene.Touch();
    }

    private static void PutBack(ICanvasItem item, Rect box)
    {
        var now = item.Bounds;
        if (Same(now, box)) return;

        // A MOVE is exact and a resize is not always - a stroke resized back is scaled back, not restored point by
        // point - so anything that only traveled is put back by traveling.
        if (Math.Abs(now.Width - box.Width) < 1e-9 && Math.Abs(now.Height - box.Height) < 1e-9)
        {
            item.Move(new Vector2<double>(box.X - now.X, box.Y - now.Y));
            return;
        }

        item.Resize(box);
    }

    private List<object> WhatChanged()
    {
        var changed = new List<object>();
        foreach (var (item, box) in _wasAt)
        {
            if (!_isAt.TryGetValue(item, out var now) || !Same(box, now))
            {
                changed.Add(item);
            }
        }

        foreach (var item in _isAt.Keys)
        {
            if (!_wasAt.ContainsKey(item))
            {
                changed.Add(item);
            }
        }

        AddReordered(changed);
        return changed;
    }

    private void AddReordered(List<object> changed)
    {
        var placeAfter = new Dictionary<ICanvasItem, int>(_after.Count, ReferenceEqualityComparer.Instance);
        for (var index = 0; index < _after.Count; index++)
        {
            placeAfter[_after[index]] = index;
        }

        var kept = new List<ICanvasItem>(_before.Count);
        var places = new List<int>(_before.Count);
        foreach (var item in _before)
        {
            if (placeAfter.TryGetValue(item, out var place))
            {
                kept.Add(item);
                places.Add(place);
            }
        }

        var inOrder = LongestRising(places);
        HashSet<object> named = null;
        for (var index = 0; index < kept.Count; index++)
        {
            if (inOrder[index])
            {
                continue;
            }

            named ??= new HashSet<object>(changed, ReferenceEqualityComparer.Instance);
            if (named.Add(kept[index]))
            {
                changed.Add(kept[index]);
            }
        }
    }

    private static bool[] LongestRising(List<int> places)
    {
        var count = places.Count;
        var tails = new int[count];
        var previous = new int[count];
        var length = 0;
        for (var index = 0; index < count; index++)
        {
            var low = 0;
            var high = length;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (places[tails[middle]] < places[index])
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            previous[index] = low > 0 ? tails[low - 1] : -1;
            tails[low] = index;
            if (low == length)
            {
                length++;
            }
        }

        var inOrder = new bool[count];
        for (var at = length > 0 ? tails[length - 1] : -1; at >= 0; at = previous[at])
        {
            inOrder[at] = true;
        }

        return inOrder;
    }

    private bool Moved()
    {
        foreach (var (item, box) in _wasAt)
        {
            if (_isAt.TryGetValue(item, out var now) && !Same(box, now)) return true;
        }

        return false;
    }

    private bool Reordered()
    {
        for (var i = 0; i < _before.Count; i++)
        {
            if (!ReferenceEquals(_before[i], _after[i])) return true;
        }

        return false;
    }

    private static bool Same(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9 &&
        Math.Abs(a.Width - b.Width) < 1e-9 && Math.Abs(a.Height - b.Height) < 1e-9;
}
