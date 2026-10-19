using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Panels;

/// <summary>The items host of a <see cref="RibbonTab"/>: groups in a row, each at its own width. Not a StackPanel -
/// that virtualizes, and would give every group one probed width.</summary>
public class RibbonGroupsPanel : Panel
{
    public RibbonGroupsPanel()
    {
        GotKeyboardFocus += OnFocusEntered;
    }

    /// <summary>A ROW: Up/Down must not become "the next group" through the generic order-based walk. UP is the one
    /// exception - out of the top of the band is the STRIP, and the walk cannot find it on its own: the two live in
    /// separate subtrees of the ribbon's template, and the panel between them answers only by child order.</summary>
    public override IUIComponent Navigate(IUIComponent from, FocusNavigationDirection direction)
    {
        if (IsArrow(direction) && IsVertical(direction))
        {
            return IsForward(direction) ? null : SelectedHeader();
        }

        return base.Navigate(from, direction);
    }

    private IUIComponent SelectedHeader() =>
        this.GetVisualAncestors().OfType<Ribbon>().FirstOrDefault()?.SelectedHeader;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;

            // Unbounded: what a group GETS is the arrange slot, and the variant is chosen from that.
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // From the SLOT, never the measure constraint - a parent may measure unbounded just to learn our natural size.
        ChooseVariants(finalSize.Width);

        double x = 0;
        _slots.Clear();
        foreach (var child in Children)
        {
            if (child.Visibility != Visibility.Visible) continue;

            // The chosen variant may have just changed the sizes underneath it.
            child.Measure(new Size(double.PositiveInfinity, finalSize.Height));

            var width = WidthOf(child);
            _slots.Add((child, x, width));
            // Full band height, so every caption sits on one line. Shifted by the scroll, which is the LAST resort -
            // everything above has already been shrunk and collapsed.
            child.Arrange(new Rect(x - Offset, 0, width, finalSize.Height));
            x += width;
        }

        _extent = x;
        _viewport = finalSize.Width;
        Clamp();
        return new Size(finalSize.Width, finalSize.Height);
    }

    // --- Scrolling the row -------------------------------------------------------------------------------------------
    //
    // Not a ScrollViewer: a bar under the band would eat height from a strip that has none to spare, and it is chrome
    // nobody asked for. The row moves by WHOLE GROUPS behind two repeat buttons that overlay its edges - a half-shown
    // group reads as damage rather than as "there is more".

    private readonly List<(IMeasurableComponent Child, double Start, double Width)> _slots = [];   // unscrolled
    private double _extent;
    private double _viewport;

    /// <summary>How far the row is scrolled. Not a property with a callback: the panel is the only thing that moves it,
    /// and every move already re-arranges.</summary>
    public double Offset { get; private set; }

    public bool CanScrollBack => Offset > 0.5;

    public bool CanScrollForward => _extent - Offset > _viewport + 0.5;

    /// <summary>Told after every arrange, so the tab can show or hide its arrows and edge fades. The tab owns the
    /// chrome because the panel is inside its items presenter, where a template cannot reach it.</summary>
    public event EventHandler ScrollStateChanged;

    /// <summary>One GROUP back. Office steps by group, not by pixels: what a scroll ends on has to be a whole command
    /// set, or the row reads as clipped.</summary>
    public void ScrollBack() => ScrollTo(PreviousEdge());

    public void ScrollForward() => ScrollTo(NextEdge());

    private double PreviousEdge()
    {
        var target = 0.0;
        foreach (var slot in _slots)
        {
            if (slot.Start >= Offset - 0.5) break;
            target = slot.Start;
        }

        return target;
    }

    private double NextEdge()
    {
        foreach (var slot in _slots)
        {
            if (slot.Start > Offset + 0.5) return slot.Start;
        }

        return Offset;
    }

    /// <summary>Tab and the arrows may land on a group that is scrolled off - focus you cannot see is worse than no
    /// focus at all, so the row follows the keyboard.</summary>
    private void OnFocusEntered(object sender, KeyboardGotFocusEventArgs e)
    {
        var slot = SlotContaining(e.NewFocus as IUIComponent);
        if (slot == null) return;

        var (_, start, width) = slot.Value;
        if (start < Offset)
        {
            ScrollTo(start);
        }
        else if (start + width > Offset + _viewport)
        {
            // Its far edge, unless the group is wider than the row - then its near edge, so it starts where it can be read.
            ScrollTo(Math.Min(start, start + width - _viewport));
        }
    }

    private (IMeasurableComponent Child, double Start, double Width)? SlotContaining(IUIComponent focused)
    {
        while (focused != null)
        {
            foreach (var slot in _slots)
            {
                if (ReferenceEquals(slot.Child, focused)) return slot;
            }

            focused = focused.VisualParent as IUIComponent;
        }

        return null;
    }

    private void ScrollTo(double offset)
    {
        if (Math.Abs(offset - Offset) < 0.5) return;

        Offset = offset;
        Clamp();
        InvalidateArrange();
    }

    // A row that shrank (a wider window, a collapsed group) must not leave the view hanging past its end.
    private void Clamp()
    {
        var max = Math.Max(0, _extent - _viewport);
        var clamped = Math.Min(Math.Max(0, Offset), max);
        var changed = Math.Abs(clamped - Offset) > 0.5;
        Offset = clamped;

        ScrollStateChanged?.Invoke(this, EventArgs.Empty);
        if (changed) InvalidateArrange();
    }

    // --- Choosing which variant each group is drawn at ---------------------------------------------------------------

    private void ChooseVariants(double available)
    {
        var groups = new List<RibbonGroup>();
        foreach (var child in Children)
        {
            if (child is RibbonGroup group && group.Visibility == Visibility.Visible)
            {
                groups.Add(group);
            }
        }

        if (groups.Count == 0) return;

        // Labels rewritten (another language) change what the open groups cost at once. A collapsed group cannot be
        // measured inside its flyout, so what it keeps is about the old labels: forgotten, and the group drawn open to
        // learn them again - unless its flyout is in use. Collapsing a neighbor changes no open group's widths, so this
        // does not feed itself.
        var widths = new double[groups.Count][];
        var rewritten = false;
        for (var g = 0; g < groups.Count; g++)
        {
            if (groups[g].IsCollapsed) continue;

            widths[g] = WidthsOf(groups[g]);
            rewritten |= _measured.TryGetValue(groups[g], out var before) && Differ(before, widths[g]);
            _measured.AddOrUpdate(groups[g], widths[g]);
        }

        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            if (!group.IsCollapsed) continue;

            if (rewritten) group.ForgetWidths();
            if (!group.IsDropDownOpen && HasUnknownWidths(group)) group.ApplyVariant(0);

            widths[g] = WidthsOf(group);
        }

        // Decided afresh on every pass, from every group whole, and only then applied: the answer is the width's and the
        // content's alone. Stepping on from the last answer, a width misread for one pass while fonts were still
        // arriving collapsed a group for good - the room it freed went to a neighbor and never came back.
        var plan = new int[groups.Count];
        var held = new bool[groups.Count];
        for (var g = 0; g < groups.Count; g++)
        {
            // A group whose flyout is in use stays as it is: changing it would close the flyout under the pointer.
            held[g] = groups[g].IsCollapsed && groups[g].IsDropDownOpen;

            // From its roomiest KNOWN variant: one it cannot say the cost of is not one to draw it at.
            plan[g] = held[g] ? groups[g].CurrentVariant : FirstKnown(widths[g]);
        }

        Lower(groups, widths, plan, held, available);
        GiveBackTheRoom(groups, widths, plan, held, available);

        for (var g = 0; g < groups.Count; g++)
        {
            if (groups[g].CurrentVariant != plan[g]) groups[g].ApplyVariant(plan[g]);
        }    }

    // What each open group cost the last time it was asked.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<RibbonGroup, double[]> _measured = new();

    private static int FirstKnown(double[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            if (!double.IsNaN(widths[i])) return i;
        }

        return 0;
    }

    // The collapsed button's cost is left out: measured the first time the group collapses, it is not the labels changing.
    private static bool Differ(double[] before, double[] now)
    {
        if (before.Length != now.Length) return true;

        for (var i = 0; i < now.Length - 1; i++)
        {
            if (double.IsNaN(before[i]) != double.IsNaN(now[i]) || Math.Abs(before[i] - now[i]) > 0.5) return true;
        }

        return false;
    }

    private static bool HasUnknownWidths(RibbonGroup group)
    {
        for (var i = 0; i < group.Variants.Count - 1; i++)
        {
            if (double.IsNaN(group.WidthAt(i))) return true;
        }

        return false;
    }

    // What each variant costs; a group with no variants is what it measured. Unknown stays NaN, which never fits.
    private static double[] WidthsOf(RibbonGroup group)
    {
        var count = group.Variants.Count;
        if (count == 0) return [group.DesiredSize.Width];

        var widths = new double[count];
        for (var i = 0; i < count; i++) widths[i] = group.WidthAt(i);
        return widths;
    }

    private static double Total(double[][] widths, int[] plan)
    {
        double total = 0;
        for (var g = 0; g < plan.Length; g++)
        {
            var width = widths[g][plan[g]];
            if (!double.IsNaN(width)) total += width;
        }

        return total;
    }

    // ONE group at a time, each to the end of its own ladder - collapsed is simply its last step, not a separate pass:
    // the lowest ShrinkPriority, among equals the one furthest RIGHT, so the tab gives way from its end inwards.
    private static void Lower(List<RibbonGroup> groups, double[][] widths, int[] plan, bool[] held, double available)
    {
        while (Total(widths, plan) > available)
        {
            var pick = -1;
            for (var g = 0; g < groups.Count; g++)
            {
                if (held[g] || NextNarrower(groups[g], widths[g], plan[g]) < 0) continue;
                if (pick < 0 || groups[g].ShrinkPriority <= groups[pick].ShrinkPriority) pick = g;
            }

            if (pick < 0) return;

            plan[pick] = NextNarrower(groups[pick], widths[pick], plan[pick]);
        }
    }

    // The mirror: sacrificed last, restored first, each to the roomiest variant that still fits. A step that collapses
    // a group frees more than was needed, and the band does not stand empty beside commands put away: a group the room
    // does not fit is passed over, not the end of it - one further on may need less.
    private static void GiveBackTheRoom(List<RibbonGroup> groups, double[][] widths, int[] plan, bool[] held,
        double available)
    {
        // Stable, so among equals the leftmost - the last the tab gave up - comes first.
        var order = Enumerable.Range(0, groups.Count).OrderByDescending(g => groups[g].ShrinkPriority);
        foreach (var g in order)
        {
            if (held[g]) continue;

            var room = available - (Total(widths, plan) - widths[g][plan[g]]);
            for (var i = 0; i < plan[g]; i++)
            {
                if (i > 0 && (groups[g].ShrinkSteps & RibbonGroupShrinkSteps.Sizes) == 0) break;
                if (widths[g][i] <= room)
                {
                    plan[g] = i;
                    break;
                }
            }
        }
    }

    // A step has to BUY width, collapsing included. Ladders are not monotonic: dropping a group's only large command
    // re-packs its rows into MORE columns, so the rung below can cost more than the one above it. -1 = nothing left.
    private static int NextNarrower(RibbonGroup group, double[] widths, int from)
    {
        var steps = group.ShrinkSteps;
        if (steps == RibbonGroupShrinkSteps.None) return -1;

        var variants = group.Variants;
        for (var i = from + 1; i < variants.Count; i++)
        {
            var step = variants[i].IsCollapsed ? RibbonGroupShrinkSteps.Collapse : RibbonGroupShrinkSteps.Sizes;
            if ((steps & step) == 0) continue;

            if (widths[i] < widths[from]) return i;
        }

        return -1;
    }

    private static double WidthOf(IMeasurableComponent child) =>
        child is RibbonGroup group && !double.IsNaN(group.WidthAt(group.CurrentVariant))
            ? group.WidthAt(group.CurrentVariant)
            : child.DesiredSize.Width;
}
