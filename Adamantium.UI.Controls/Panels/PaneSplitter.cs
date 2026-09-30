using System;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The grip between two neighbors in a <see cref="PaneHost"/>. A drag moves their boundary, editing the pane
/// lengths in place; the pair keeps its total, so no one else in the row moves.</summary>
public class PaneSplitter : Thumb
{
    private double _originBefore;
    private double _originAfter;

    /// <summary>Which way this splitter resizes, set by the host; picks the cursor, and themes trigger on it to orient the
    /// grip.</summary>
    public static readonly AdamantiumProperty OrientationProperty = AdamantiumProperty.Register(nameof(Orientation),
        typeof(Orientation), typeof(PaneSplitter),
        new PropertyMetadata(Orientation.Horizontal, PropertyMetadataOptions.AffectsRender, OnOrientationChanged));

    /// <summary>The cursor for the DEFAULT orientation, stated here because the callback below cannot state it: the
    /// host writes every splitter's orientation, and writing the value it already has is not a change - so a splitter
    /// left at the default was the one case that never ran the callback and never got its arrows. This side effect
    /// used to live in a plain setter, which ran on every write and hid that.</summary>
    public PaneSplitter() => ApplyCursor();

    private static void OnOrientationChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        if (a is PaneSplitter splitter) splitter.ApplyCursor();
    }

    private void ApplyCursor()
        => Cursor = Orientation == Orientation.Horizontal ? CursorType.SizeEWE : CursorType.SizeNS;

    public Orientation Orientation
    {
        get => GetValue<Orientation>(OrientationProperty);
        internal set => SetValue(OrientationProperty, value);
    }

    protected override void OnDragStarted(DragStartedEventArgs e)
    {
        base.OnDragStarted(e);

        if (VisualParent is not PaneHost host) return;

        var (before, after) = Neighbors();
        if (before == null || after == null) return;

        // Where the two neighbors START, in PIXELS - which is also what the drag will write. A pixel of mouse is a
        // pixel of boundary, with no basis to convert to and nothing to compound: Thumb reports a CUMULATIVE change, so
        // every delta is measured from here rather than added to whatever the last one produced.
        _originBefore = host.PixelsOf(before);
        _originAfter = host.PixelsOf(after);
    }

    protected override void OnDragDelta(DragEventArgs e)
    {
        base.OnDragDelta(e);

        var (before, after) = Neighbors();
        if (before == null || after == null) return;

        var moved = Orientation == Orientation.Horizontal ? e.Change.X : e.Change.Y;
        var total = _originBefore + _originAfter;

        // Neither side may be squeezed past what it says it needs - that MinSize is also what stops the tree from
        // being split into slivers, so the two rules are the same rule.
        var floor = VisualParent is PaneHost host ? Math.Max(0, host.MinFraction) * total : 0;
        var minBefore = Math.Max(floor, MinPixelsOf(before));
        var minAfter = Math.Max(floor, MinPixelsOf(after));
        moved = Math.Clamp(moved, minBefore - _originBefore, total - minAfter - _originBefore);

        var newBefore = _originBefore + moved;
        var newAfter = _originAfter - moved;

        var lengthBefore = PaneHost.GetPaneLength(before);
        var lengthAfter = PaneHost.GetPaneLength(after);

        // Move the boundary, keeping each length's unit: a share stays a share and a fixed size stays fixed, so the row
        // still grows with the window.
        if (lengthBefore.IsStar && lengthAfter.IsStar)
        {
            // Two shares of one pool: divide their COMBINED weight the way the pixels now divide, so the pair is worth
            // what it was worth together.
            var weight = Weight(lengthBefore) + Weight(lengthAfter);
            PaneHost.SetPaneLength(before, PaneLength.Stars(weight * newBefore / total));
            PaneHost.SetPaneLength(after, PaneLength.Stars(weight * newAfter / total));
        }
        else if (lengthBefore.IsStar)
        {
            // A share takes whatever is left over, so moving this boundary is entirely the FIXED one's business.
            PaneHost.SetPaneLength(after, PaneLength.Pixels(newAfter));
        }
        else if (lengthAfter.IsStar)
        {
            PaneHost.SetPaneLength(before, PaneLength.Pixels(newBefore));
        }
        else
        {
            PaneHost.SetPaneLength(before, PaneLength.Pixels(newBefore));
            PaneHost.SetPaneLength(after, PaneLength.Pixels(newAfter));
        }

    }

    /// <summary>A share's weight, treating an unstated one as a single share - the same reading the host uses.</summary>
    private static double Weight(PaneLength length) => length.Value > 0 ? length.Value : 1;

    /// <summary>The smallest this neighbor may become, in pixels. An explicit MinWidth/MinHeight wins; otherwise the
    /// neighbor is asked what it needs - and a docking area answers with its own policy, so a group cannot be squeezed
    /// under what its panes declared (nor, therefore, under its own tab strip). A nested host answers for its children,
    /// since squeezing it squeezes them.</summary>
    private double MinPixelsOf(IUIComponent neighbor)
    {
        if (neighbor is MeasurableUIComponent measurable)
        {
            var explicitMin = Orientation == Orientation.Horizontal ? measurable.MinWidth : measurable.MinHeight;
            if (!double.IsNaN(explicitMin) && explicitMin > 0) return explicitMin;
        }

        return neighbor is IPaneMinimum owner ? owner.MinimumExtent(Orientation) : 0;
    }

    /// <summary>The two content children this splitter sits between (other splitters are not content).</summary>
    private (IUIComponent Before, IUIComponent After) Neighbors()
    {
        if (VisualParent is not PaneHost host) return (null, null);

        IUIComponent before = null;
        var seenSelf = false;
        foreach (var child in host.Children)
        {
            if (ReferenceEquals(child, this))
            {
                seenSelf = true;
                continue;
            }

            if (child is PaneSplitter) continue;

            if (!seenSelf) before = child;
            else return (before, child);
        }

        return (before, null);
    }
}
