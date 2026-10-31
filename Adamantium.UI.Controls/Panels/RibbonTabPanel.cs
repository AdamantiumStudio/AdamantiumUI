using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The ribbon strip's items host: tab headers in a row, a contextual group's label standing just before its
/// run of tabs. The labels live in the panel so they move and clip with those tabs, and IN the row so a context
/// appearing never changes the strip's height.</summary>
public class RibbonTabPanel : Panel
{
    // The labels are OURS, not items: they are added straight to the visual tree, so the items presenter's own
    // Children.Clear() never touches them and the container generator never sees them.
    private readonly List<RibbonContextualLabel> _labels = [];

    // The headers in the order they are LAID OUT, which is not the order they were authored in - see Order().
    private readonly List<IMeasurableComponent> _order = [];

    // (first header index, group) for each run of neighboring tabs sharing a group that shows its header; one label
    // per run, standing before the run's first tab.
    private readonly List<(int First, RibbonContextualGroup Group)> _runs = [];

    // Where each label was last put, so a move can be told to re-record it (see ArrangeOverride).
    private readonly Dictionary<RibbonContextualLabel, Rect> _placed = [];

    /// <summary>Left/Right walk the headers as they STAND, labels skipped - a label names tabs and is not one, and the
    /// laid-out order is not the authored one once contextual tabs are in the strip.</summary>
    public override IUIComponent Navigate(IUIComponent from, FocusNavigationDirection direction)
    {
        if (!IsArrow(direction)) return base.Navigate(from, direction);
        if (IsVertical(direction)) return null;   // a ROW: Up/Down are not "the next tab"

        var at = _order.IndexOf(from as IMeasurableComponent);
        if (at < 0) return null;

        var next = at + (IsForward(direction) ? 1 : -1);
        return next >= 0 && next < _order.Count ? _order[next] : null;
    }

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        Order();
        Repartition();
        SyncLabels();

        // Unbounded along the row so each header takes exactly its own width.
        var forHeaders = new Size<double>(double.PositiveInfinity, availableSize.Height);

        double width = 0, height = 0;
        foreach (var header in _order)
        {
            header.Measure(forHeaders);
            width += header.DesiredSize.Width;
            height = Math.Max(height, header.DesiredSize.Height);
        }

        // The tabs set the row's height; a label stands at that height and adds only its width.
        foreach (var label in _labels)
        {
            label.Measure(new Size<double>(double.PositiveInfinity, height));
            width += label.DesiredSize.Width;
        }

        return new Size<double>(width, height);
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        // Read from the headers HERE rather than from a number the last measure left behind. Clamped to the slot,
        // never stretched to it: a strip occupies only what it stacks.
        double tallest = 0;
        foreach (var header in _order) tallest = Math.Max(tallest, header.DesiredSize.Height);

        var row = Math.Min(finalSize.Height, tallest);

        double x = 0;
        var run = 0;
        for (var i = 0; i < _order.Count; i++)
        {
            if (run < _runs.Count && run < _labels.Count && _runs[run].First == i)
            {
                var label = _labels[run++];
                var slot = new Rect(x, 0, label.DesiredSize.Width, row);

                // A label is a raw visual child, outside Children and outside the items presenter, so nothing marks
                // it for re-recording when it merely MOVES. Told here, and only when it actually moved, so a still
                // strip records nothing.
                if (!_placed.TryGetValue(label, out var before) || before != slot)
                {
                    _placed[label] = slot;
                    label.InvalidateRender(true);
                }

                label.Arrange(slot);
                x += slot.Width;
            }

            var header = _order[i];
            var width = header.DesiredSize.Width;
            header.Arrange(new Rect(x, 0, width, row));
            x += width;
        }

        return new Size<double>(x, row);
    }

    // Ordinary tabs first, in the order their author wrote them; then the contextual ones, group by group, oldest
    // activation first. The AUTHOR's list is never touched - it is his - so the order lives here, in the layout.
    private void Order()
    {
        _order.Clear();

        foreach (var child in Children)
        {
            if (Shown(child) && GroupOf(child) == null) _order.Add(child);
        }

        var contextual = new List<(IMeasurableComponent Header, long Activated, int Authored)>();
        var at = 0;
        foreach (var child in Children)
        {
            if (Shown(child) && GroupOf(child) != null) contextual.Add((child, GroupOf(child).ActivatedAt, at));
            at++;
        }

        // Groups by when they became active; WITHIN a group the author's own order. The authored index is part of the
        // key on purpose: List.Sort is not stable, so comparing activation alone would let a group's tabs shuffle among
        // themselves - and would let two groups interleave whenever their stamps tie.
        contextual.Sort((a, b) =>
        {
            var byGroup = a.Activated.CompareTo(b.Activated);
            return byGroup != 0 ? byGroup : a.Authored.CompareTo(b.Authored);
        });

        foreach (var entry in contextual) _order.Add(entry.Header);
    }

    // A run is a MAXIMAL stretch of neighboring headers with the SAME group. Neighboring, not merely belonging: an
    // ordinary tab placed between two tabs of one group yields TWO labels with the same title, and that is the honest
    // answer - the strip shows what is in it. Office forbids the arrangement instead; we make it predictable.
    // Only a group that shows its header gets a label; the others are carried by their tabs' color alone.
    private void Repartition()
    {
        _runs.Clear();

        var i = 0;
        while (i < _order.Count)
        {
            var group = GroupOf(_order[i]);
            if (group == null)
            {
                i++;
                continue;
            }

            var last = i;
            while (last + 1 < _order.Count && ReferenceEquals(GroupOf(_order[last + 1]), group)) last++;

            if (group.ShowContextualHeader && group.Header != null) _runs.Add((i, group));
            i = last + 1;
        }
    }

    // One label per run. Kept as a pool rather than rebuilt: the runs change on every activation, and a fresh visual
    // each time would throw away its themed template for nothing.
    private void SyncLabels()
    {
        while (_labels.Count < _runs.Count)
        {
            var label = new RibbonContextualLabel();
            _labels.Add(label);
            AddVisualChild(label);
            Theme(label);
        }

        while (_labels.Count > _runs.Count)
        {
            var last = _labels[^1];
            _labels.RemoveAt(_labels.Count - 1);
            _placed.Remove(last);
            RemoveVisualChild(last);
        }

        for (var i = 0; i < _runs.Count; i++)
        {
            var group = _runs[i].Group;
            _labels[i].Content = group.Header;
            _labels[i].Accent = group.Accent;
        }
    }

    /// <summary>A label is framework CHROME, not an item: it is joined to the visual tree only, so nothing gives it a
    /// logical parent - and it is the logical parent's setter that applies the theme. Left alone it would have no
    /// template at all and would take up its place while drawing nothing. So it is themed OUT OF BAND, exactly as an
    /// adorner and a template root are: the inheritance parent for its DataContext, the theme applied by hand.</summary>
    private void Theme(RibbonContextualLabel label)
    {
        label.InheritanceParent = this;
        label.ApplyCurrentTheme();
    }

    /// <summary>Out-of-band theming means out-of-band RE-theming too: a theme swap reaches this panel, and the labels
    /// hang off it rather than under it.</summary>
    protected override void ApplyCurrentThemeCore()
    {
        base.ApplyCurrentThemeCore();

        foreach (var label in _labels) Theme(label);
    }

    private static bool Shown(IMeasurableComponent child) => child.Visibility == Visibility.Visible;

    private static RibbonContextualGroup GroupOf(IMeasurableComponent child) =>
        (child as RibbonTabHeader)?.ContextualGroup;
}
