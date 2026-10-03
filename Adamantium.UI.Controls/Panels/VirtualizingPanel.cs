using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.Core.Templates;

namespace Adamantium.UI.Controls.Panels;

/// <summary>Base for panels that virtualize an <see cref="ItemsControl"/>'s items; subclasses supply only the geometry.
/// Set <see cref="IsVirtualizing"/> to false for small mixed-height hosts, which then realize and stack every item.</summary>
public abstract class VirtualizingPanel : Panel, IScrollableContent
{
    private Size _extent;
    private Size _viewport;
    private Vector2 _offset;
    // The offset the current measure realized its window against. Arrange positions items with THIS, not a fresh read of
    // _offset: a fast scroll can change _offset between the window's measure phase and its arrange phase, and two
    // different offsets would position an item where the measure didn't realize it. One snapshot per pass keeps the
    // realized window and the arranged positions consistent.
    private Vector2 _passOffset;

    // The panel's size does not depend on its children, so child invalidations raised while it rebinds its own window
    // are muted; otherwise every pass would measure the window twice.
    private bool _inLayout;

    // As a virtualizing items host the desired size is the virtual extent, independent of tiles, so a tile re-measure
    // must not re-dirty this panel.
    public override bool IsMeasureBoundary => (IsItemsHost && IsVirtualizing) || base.IsMeasureBoundary;

    public override void InvalidateMeasure()
    {
        if (_inLayout) return;
        base.InvalidateMeasure();
    }

    /// <summary>Whether to virtualize when hosting items (default true). Set false for a small, mixed-height host (a menu)
    /// so every item is realized and measured/arranged at its OWN size instead of a uniform cell extent.</summary>
    public static readonly AdamantiumProperty IsVirtualizingProperty = AdamantiumProperty.Register(nameof(IsVirtualizing),
        typeof(bool), typeof(VirtualizingPanel), new PropertyMetadata(true));

    public bool IsVirtualizing
    {
        get => GetValue<bool>(IsVirtualizingProperty);
        set => SetValue(IsVirtualizingProperty, value);
    }

    /// <summary>Milliseconds a pass may spend binding containers while scrolling; the rest defer to the next pass as
    /// skeletons. 0 means no budget.</summary>
    public static readonly AdamantiumProperty ScrollBindBudgetProperty = AdamantiumProperty.Register(nameof(ScrollBindBudget),
        typeof(double), typeof(VirtualizingPanel), new PropertyMetadata(6.0));

    public double ScrollBindBudget
    {
        get => GetValue<double>(ScrollBindBudgetProperty);
        set => SetValue(ScrollBindBudgetProperty, value);
    }

    /// <summary>Milliseconds a single pass may spend (re)binding when NOT scrolling - the initial fill or a settled
    /// fling, where the backlog should drain fast. **0 means no budget.** Larger than <see cref="ScrollBindBudget"/> by
    /// default because a still window can afford a longer pass without anyone seeing it.</summary>
    public static readonly AdamantiumProperty FillBindBudgetProperty = AdamantiumProperty.Register(nameof(FillBindBudget),
        typeof(double), typeof(VirtualizingPanel), new PropertyMetadata(30.0));

    public double FillBindBudget
    {
        get => GetValue<double>(FillBindBudgetProperty);
        set => SetValue(FillBindBudgetProperty, value);
    }

    /// <summary>The default floor, named so a caller can express "one guaranteed slice" without hard-coding the number.</summary>
    public const int MinBindsPerPassDefault = 8;

    /// <summary>The floor: however small the budget, a pass always (re)binds at least this many slots, so the window
    /// keeps filling instead of stalling on a machine where the very first bind already overruns.</summary>
    public static readonly AdamantiumProperty MinBindsPerPassProperty = AdamantiumProperty.Register(nameof(MinBindsPerPass),
        typeof(int), typeof(VirtualizingPanel), new PropertyMetadata(MinBindsPerPassDefault));

    public int MinBindsPerPass
    {
        get => GetValue<int>(MinBindsPerPassProperty);
        set => SetValue(MinBindsPerPassProperty, value);
    }

    /// <summary>The budget to hand <c>SetWindow</c>: the caller's milliseconds, with 0 meaning "no budget" spelled the
    /// way the generator understands it.</summary>
    protected static double BudgetOrUnlimited(double ms) => ms <= 0 ? double.MaxValue : ms;

    /// <summary>Index a dropped item would land at, or -1 for none. The panel leaves a real empty slot there and animates
    /// the items that move.</summary>
    public static readonly AdamantiumProperty DropGapIndexProperty = AdamantiumProperty.Register(nameof(DropGapIndex),
        typeof(int), typeof(VirtualizingPanel), new PropertyMetadata(-1, OnDropGapChanged));

    public int DropGapIndex
    {
        get => GetValue<int>(DropGapIndexProperty);
        set => SetValue(DropGapIndexProperty, value);
    }

    private static void OnDropGapChanged(AdamantiumComponent a, AdamantiumPropertyChangedEventArgs e)
    {
        // The gap changes where every following item sits, so the slots have to be recomputed - but only when it MOVES,
        // which is a rare event during a drag (a few times a gesture), not something that runs per mouse move.
        if (a is VirtualizingPanel panel) panel.InvalidateMeasure();
    }

    /// <summary>The slot a drop at <paramref name="point"/> would land in, or false. Must come from the grid, not from
    /// container positions, which the gap itself moves.</summary>
    public virtual bool TryGetDropSlot(Vector2 point, out int index)
    {
        index = -1;
        return false;
    }

    /// <summary>Whether this panel actually opens <see cref="DropGapIndex"/> in its layout. False (the default) means the
    /// drag shows its insertion caret instead - the two are alternatives, never both, since they mark the same place.</summary>
    public virtual bool SupportsDropGap => false;

    /// <summary>The slot an item at <paramref name="index"/> occupies once the drop gap is accounted for: everything from
    /// the gap on shifts along by one, opening exactly one item-sized hole. Identical to the index when no drop is in
    /// progress.</summary>
    protected int SlotOf(int index)
    {
        var gap = DropGapIndex;
        return gap >= 0 && index >= gap ? index + 1 : index;
    }

    /// <summary>How many slots the panel lays out: one more than the item count while a drop gap is open.</summary>
    protected int SlotCount(int itemCount) => DropGapIndex >= 0 ? itemCount + 1 : itemCount;

    public override void InvalidateArrange()
    {
        if (_inLayout) return;
        base.InvalidateArrange();
    }

    /// <summary>The items control this panel hosts (set by the <see cref="ItemsPresenter"/>); null = plain container.</summary>
    internal ItemsControl Owner { get; private set; }

    protected bool IsItemsHost => Owner != null;

    public Size Extent => _extent;
    public Size Viewport => _viewport;
    public Vector2 Offset => _offset;

    // The offset the last measure realized/arranged the window for (see IScrollableContent.RealizedOffset). A host that
    // translates this panel must use this, not Offset, or the translation and the realized window disagree for a frame.
    public Vector2 RealizedOffset => _passOffset;
    public bool CanScrollHorizontally { get; set; } = true;
    public bool CanScrollVertically { get; set; } = true;
    public event EventHandler ScrollMetricsChanged;

    /// <summary>Switches the panel into items-host mode for <paramref name="owner"/>; it now virtualizes its items.</summary>
    internal void AttachOwner(ItemsControl owner)
    {
        Children.Clear();   // drop any plain children; the window is managed via the generator from here
        Owner = owner;
        // No self-clip: the presenter slides this panel, so its own clip would move with it. The presenter's clip bounds
        // the list.
        InvalidateMeasure();
    }

    /// <summary>Gives up hosting the items: lets go of every container it holds and realizes nothing from here on. The
    /// panel that replaces it does - a replaced panel still measured once more would otherwise take them all back.</summary>
    internal void DetachOwner()
    {
        foreach (var child in VisualChildren.ToList())
        {
            RemoveVisualChild(child);
            RemoveLogicalChild(child);
        }

        ResetSkeletons();
        Owner = null;
    }

    /// <summary>Drops the realized window AND the pooled containers (e.g. the collection reset) so the next measure
    /// rebuilds from scratch. Detaches every container the panel holds (realized + pooled), not just the visible ones.</summary>
    internal void Revirtualize()
    {
        foreach (var child in VisualChildren.ToList())
        {
            RemoveVisualChild(child);
            RemoveLogicalChild(child);
        }
        Owner?.ItemContainerGenerator.Clear();
        // The loop above also detached the skeleton cards - drop their now-dangling pool/active/set so the next fill
        // rebuilds fresh ones (else RentSkeleton hands back a card that is no longer a visual child and never renders,
        // which made skeletons vanish after an ItemTemplate switch / any regenerate).
        ResetSkeletons();
        InvalidateMeasure();
    }

    // Applies Add/Remove to the realized window in place, so unchanged containers keep their state; Reset, Move and
    // Replace still rebuild.
    internal void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        if (Owner?.ItemContainerGenerator is not { } generator) return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                // Shift realized indices at/after the insert up; the inserted slot(s) are now an unmapped gap that the
                // next SetWindow fills, and the shifted containers keep their (unchanged) items at their new indices.
                generator.OnItemsInserted(e.NewStartingIndex, e.NewItems.Count);
                InvalidateMeasure();
                break;

            case NotifyCollectionChangedAction.Remove:
                // Recycle the removed slots' containers FIRST (unmap + pool), THEN reindex survivors down - reindexing
                // before recycling would collide a removed key with the survivor shifting onto it. The pooled containers
                // are re-drawn as donors by the next SetWindow, or parked by the arrange's HideUnmappedContainers.
                for (var i = e.OldItems.Count - 1; i >= 0; i--)
                    generator.Recycle(e.OldStartingIndex + i);
                generator.OnItemsRemoved(e.OldStartingIndex, e.OldItems.Count);
                InvalidateMeasure();
                break;

            default:   // Replace / Move / Reset: full rebuild.
                Revirtualize();
                break;
        }
    }

    public void SetOffset(Vector2 offset)
    {
        var clamped = ClampOffset(offset, _extent, _viewport);
        if (clamped == _offset) return;

        // Re-realize only when the offset crosses a cell boundary; sub-pixel wheel deltas just slide the content.
        var windowMoves = RealizedWindowMovesFor(_offset, clamped);
        _offset = clamped;
        if (windowMoves) InvalidateMeasure();   // the on-screen set changes -> realize/measure the new window
        else RaiseMetrics();                     // same window: only the translation + the scrollbar thumb follow
    }

    /// <summary>Does moving the scroll offset from <paramref name="from"/> to <paramref name="to"/> change which items
    /// fall in the realized window (cross a cell/row boundary)? Base returns true (always re-realize - the safe default);
    /// a uniform-cell panel overrides it so a sub-pixel move that stays within the current row skips the re-window.</summary>
    protected virtual bool RealizedWindowMovesFor(Vector2 from, Vector2 to) => true;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!IsItemsHost) return MeasurePlain(availableSize);

        Size desired;
        _inLayout = true;
        try
        {
            _offset = ClampOffset(_offset, _extent, _viewport);
            _passOffset = _offset;   // snapshot: the matching arrange positions against exactly this
            var extent = MeasureVirtualized(availableSize, _offset);
            _extent = extent;
            // An unbounded axis is a size probe, not a viewport: keep the known viewport there, falling back to the
            // extent only if the axis was never measured.
            _viewport = new Size(
                double.IsInfinity(availableSize.Width) ? (_viewport.Width > 0 ? _viewport.Width : extent.Width) : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? (_viewport.Height > 0 ? _viewport.Height : extent.Height) : availableSize.Height);
            // The DESIRED size still answers that question honestly: the extent on an unbounded axis, the slot on a
            // bounded one. It is what the parent asked for and is independent of the viewport above.
            desired = new Size(
                double.IsInfinity(availableSize.Width) ? extent.Width : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? extent.Height : availableSize.Height);
            // The NEW extent can be SMALLER than the one _offset was clamped to above (e.g. the cells just shrank): an
            // offset that was valid against the old, larger extent now over-scrolls the content off the top/left. Re-clamp
            // to the new extent and, if it moved, schedule a follow-up pass so the window realizes at the corrected offset.
            var reclamped = ClampOffset(_offset, _extent, _viewport);
            if (reclamped != _offset)
            {
                _offset = reclamped;
                _passOffset = reclamped;
                // Re-realize for the clamped offset so window and arrange agree this frame; the extent does not depend
                // on the offset, so this cannot loop.
                MeasureVirtualized(availableSize, _offset);
                LayoutManager.For(this).InvalidateMeasureNextPass(this);
            }
        }
        finally { _inLayout = false; }
        RaiseMetrics();
        return desired;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!IsItemsHost) return ArrangePlain(finalSize);

        _inLayout = true;
        try
        {
            _viewport = finalSize;
            // Position against the SAME offset the measure realized/decided visibility with - NOT a fresh _offset (which
            // a mid-pass scroll may have moved). _offset itself is left as-is so the next pass picks up that newer value.
            var arrangeOffset = ClampOffset(_passOffset, _extent, finalSize);
            ArrangeVirtualized(finalSize, arrangeOffset);
            HideUnmappedContainers();
        }
        finally { _inLayout = false; }
        RaiseMetrics();
        return finalSize;
    }

    // Plain (non items-host) layout — the panel used as an ordinary container. Subclass = its existing measure/arrange.
    protected abstract Size MeasurePlain(Size availableSize);
    protected abstract Size ArrangePlain(Size finalSize);

    // Virtualized layout — realize/measure/arrange only the visible window (subclass owns the geometry).
    protected abstract Size MeasureVirtualized(Size availableSize, Vector2 offset);
    protected abstract void ArrangeVirtualized(Size finalSize, Vector2 offset);

    /// <summary>Attaches (if new) and shows the container for <paramref name="index"/>, which the generator's SetWindow
    /// has already realized/rebound. Falls back to a direct realize for the pre-SetWindow probe. The container keeps its
    /// visual + GPU buffers across reuse (it is rebound, never detached/recreated).</summary>
    protected IUIComponent RealizeInWindow(int index)
    {
        var container = Owner.ItemContainerGenerator.ContainerFromIndex(index)
                        ?? Owner.ItemContainerGenerator.Realize(index);
        if (container.VisualParent != this)   // a reused container is already a child; only a brand-new one needs attaching
        {
            AddVisualChild(container);
            AddLogicalChild(container);
        }
        container.Visibility = Visibility.Visible;
        return container;
    }

    /// <summary>Where item <paramref name="index"/> sits in this panel's coordinates, realized or not; false when the panel
    /// cannot say.</summary>
    public virtual bool TryGetItemRect(int index, out Rect rect)
    {
        rect = default;
        return false;
    }

    /// <summary>How many containers virtualization has parked; a large shrink parks thousands at once.</summary>
    public static long ParkCalls;

    /// <summary>Hides an off-screen container and deactivates its bindings, keeping it attached and pooled; reuse sets its
    /// DataContext, which re-subscribes them.</summary>
    protected static void ParkContainer(IUIComponent container)
    {
        ParkCalls++;
        container.Visibility = Visibility.Collapsed;
        DeactivateSubtreeBindings(container);
    }

    private static void DeactivateSubtreeBindings(IUIComponent node)
    {
        Adamantium.UI.Core.Data.BindingEngine.DeactivateBindings(node);
        foreach (var child in node.VisualChildren) DeactivateSubtreeBindings(child);
    }

    // A container is visible only if it is in the realized window: hide and pool any the generator no longer maps.
    private void HideUnmappedContainers()
    {
        // Drain the generator's record of dropped mappings rather than scan every child: a superset of the ghosts, so
        // the tests below still decide.
        var generator = Owner.ItemContainerGenerator;
        var candidates = generator.DrainUnmapped();
        for (var i = 0; i < candidates.Count; i++)
        {
            var child = candidates[i];
            if (child.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (_skeletonSet.Contains(child))
            {
                continue;   // panel-owned loading card, not a generator container
            }

            if (generator.IndexFromContainer(child) >= 0)
            {
                continue;   // back in the realized window - keep
            }

            ParkContainer(child);
            generator.ReclaimDetached(child);
        }
    }

    // ---- Loading skeletons for budget-deferred slots ----
    // One ItemSkeletonTemplate card, drawn at every pending slot through RenderClones; clones have no layout or state.
    private UIComponent _skeletonPrototype;
    private Size _prototypeSize;
    private List<Matrix4x4F> _skeletonClones;   // fresh list per change - the draw walk may read it off the render thread

    /// <summary>How many skeleton cards are on screen right now - clones of the one prototype.</summary>
    protected int ActiveSkeletonCount => _skeletonClones?.Count ?? 0;
    private readonly HashSet<IUIComponent> _skeletonSet = new();             // panel-owned visuals (skip in HideUnmappedContainers)

    /// <summary>Shows a skeleton card at each budget-deferred slot; <paramref name="slotRect"/> maps a slot index to its
    /// rect. Call from ArrangeVirtualized.</summary>
    protected void ReconcileSkeletons(Func<int, Rect> slotRect)
    {
        var pending = Owner.ItemContainerGenerator.PendingIndices;

        if (pending.Count == 0)
        {
            HideSkeletons();
            return;
        }

        // No delay: a fill is when frames are slow, so a frame-count delay made the window look hung.
        var template = Owner?.ItemSkeletonTemplate;
        if (template == null) return;   // unthemed ItemsControl - no skeletons

        var prototype = EnsureSkeletonPrototype(template);
        if (prototype == null) return;   // template has no root

        // Inset each cell rect by the REAL tile's margin (read from a realized item, never hardcoded) so a card sits
        // exactly where its item's visual would - same footprint, same inter-tile gaps.
        var inset = ItemMargin();
        var firstRect = slotRect(pending[0]);
        var size = new Size(
            Math.Max(0, firstRect.Width - inset.Left - inset.Right),
            Math.Max(0, firstRect.Height - inset.Top - inset.Bottom));

        // Arranged ONCE, at the ORIGIN: the clones carry the positions. Slots of a virtualizing grid are the same size
        // by construction, so one arranged card fits them all - which is exactly why a translation is enough and no
        // clone needs a scale (a scale would stretch the card's border thickness with it).
        if (prototype.Visibility != Visibility.Visible) prototype.Visibility = Visibility.Visible;
        if (_prototypeSize != size)
        {
            var measurable = (IMeasurableComponent)prototype;
            measurable.Measure(size);
            measurable.Arrange(new Rect(0, 0, size.Width, size.Height));
            _prototypeSize = size;
        }

        // A FRESH list each time: the draw walk reads RenderClones on the render thread, so refilling the live one in
        // place would move clones under the frame drawing them.
        var clones = new List<Matrix4x4F>(pending.Count);
        for (var i = 0; i < pending.Count; i++) AddClone(clones, slotRect(pending[i]), inset);

        // A slot gets its container passes before it is laid out; keep its skeleton until then, or a band of holes shows.
        foreach (var index in Owner.ItemContainerGenerator.RealizedIndices)
        {
            if (Owner.ItemContainerGenerator.ContainerFromIndex(index) is IMeasurableComponent { IsArrangeValid: true }) continue;
            AddClone(clones, slotRect(SlotOf(index)), inset);
        }

        // Only when the set actually MOVED. The prototype's re-record is what carries the new set to the render side, and
        // a component re-recorded every pass keeps its window off the clean-frame fast path for as long as skeletons are
        // up - measured at 600 fps -> 180 when this was unconditional. A steady screenful of pending slots produces the
        // same matrices frame after frame, so the common case is no mark at all.
        if (!SameClones(_skeletonClones, clones))
        {
            _skeletonClones = clones;
            prototype.RenderClones = clones;

            // The recorder walks the geometry-dirty QUEUE, not the IsGeometryValid flag: InvalidateRender only clears the
            // flag, so without the mark the prototype was recorded once at birth (clones still null) and never again -
            // the group kept an empty set for good and the card drew once at the origin.
            prototype.InvalidateRender(false);
            RenderDirty.MarkGeometry(prototype);
        }

        SyncLoadingState();
    }

    private static bool SameClones(List<Matrix4x4F> a, List<Matrix4x4F> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Count != b.Count) return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }

        return true;
    }

    private static void AddClone(List<Matrix4x4F> clones, Rect slot, Thickness inset) =>
        clones.Add(Matrix4x4F.Translation((float)(slot.X + inset.Left), (float)(slot.Y + inset.Top), 0f));

    // The one card every slot is a clone of. Built from the theme's template; the panel owns no skeleton visual or
    // animation of its own - the whole look and breathe live in the template.
    private UIComponent EnsureSkeletonPrototype(DataTemplate template)
    {
        if (_skeletonPrototype != null) return _skeletonPrototype;

        _skeletonPrototype = template.Build(this).RootComponent as UIComponent;
        if (_skeletonPrototype == null) return null;

        _skeletonSet.Add(_skeletonPrototype);   // panel-owned, not a generator container
        AddVisualChild(_skeletonPrototype);
        return _skeletonPrototype;
    }

    private void HideSkeletons()
    {
        if (_skeletonClones == null && _skeletonPrototype == null) return;

        _skeletonClones = null;
        if (_skeletonPrototype != null)
        {
            _skeletonPrototype.RenderClones = null;
            _skeletonPrototype.Visibility = Visibility.Collapsed;
        }

        SyncLoadingState();
    }

    // The LIST-level loading state (ItemsControl.IsLoadingItems): true exactly while cards are on screen. The theme keys
    // the skeleton shimmer off it - ONE trigger per list starts/stops the pulse on the shared skeleton brush - so a
    // screenful of cards costs one animation, not one per card (which is also one property write + one brush-changed
    // fan-out per card per frame). The panel owns the STATE, the theme owns the look.
    private void SyncLoadingState()
    {
        if (Owner is { } owner) owner.IsLoadingItems = ActiveSkeletonCount > 0;
    }

    private static void ArrangeSkeletonUnused((UIComponent card, Rect rect) slot)
    {
        var m = (IMeasurableComponent)slot.card;
        m.Measure(new Size(slot.rect.Width, slot.rect.Height));
        m.Arrange(slot.rect);
    }

    // Where each item last sat, keyed by the data item: containers are recycled onto other items.
    private readonly Dictionary<object, Vector2> _lastItemPos = new();
    private int _animatedGap = -1;   // the gap the last animated pass ran for
    private static readonly TimeSpan LayoutMoveDuration = TimeSpan.FromSeconds(0.18);

    // Rows promoted for the duration of an open gap. A promotion costs a transform slot, so it is not left behind: the
    // moment the gap closes they all go back to being ordinary children.
    private readonly HashSet<UIComponent> _promoted = new();

    // Makes a row carry its own subtree (or stop), marking it so the render side re-bakes; idempotent.
    private static void AsMotionNode(UIComponent row, bool on)
    {
        if (row.IsRenderMotionNode == on) return;

        row.IsRenderMotionNode = on;
        RenderDirty.MarkTransform(row);
        RenderDirty.MarkSubtreeGeometry(row);   // its subtree is baked in a DIFFERENT space now - re-take the record
    }

    /// <summary>Animates tiles that layout just moved from their previous place to the new one via the render transform;
    /// layout stays the authority. New items and scrolling animate nothing.</summary>
    protected void AnimateLayoutMoves(Func<int, Rect> slotRect)
    {
        // Gap closed: nothing is traveling any more, so give the slots back.
        if (DropGapIndex < 0 && _promoted.Count > 0)
        {
            foreach (var row in _promoted) AsMotionNode(row, false);
            _promoted.Clear();
        }

        var items = Owner?.Items;
        if (items == null) return;

        // Only a MOVED DROP GAP animates. Layout moves tiles for other reasons too - a resize reflows the whole grid, a
        // narrower window changes the column count - and sliding hundreds of tiles into place then is both expensive and
        // visually noisy. Those passes still record where everything ended up, so the next gap move measures from the
        // truth rather than from a position two layouts old.
        var animate = DropGapIndex != _animatedGap;
        _animatedGap = DropGapIndex;

        foreach (var index in Owner.ItemContainerGenerator.RealizedIndices)
        {
            if (index < 0 || index >= items.Count) continue;
            var item = items[index];
            if (item == null) continue;

            var now = slotRect(SlotOf(index)).Location;
            var moved = _lastItemPos.TryGetValue(item, out var before) && before != now;
            _lastItemPos[item] = now;
            if (!moved) continue;

            if (Owner.ItemContainerGenerator.ContainerFromIndex(index) is not UIComponent container) continue;

            // A ROW TRAVELS AS ONE THING. Promoted to a motion node, its subtree is baked relative to IT and the shader
            // applies its slot matrix to everything beneath - so the parts that live only in a shared-mesh arena travel
            // with it. Without this the row rode an ANCESTOR's slot: the row's own movement never reached its drag grip,
            // and the grip stayed where the row had been while the rest of it left. Measured at 23 px apart mid-slide.
            AsMotionNode(container, true);
            _promoted.Add(container);

            // ...and the record still has to be re-taken, because a gap shift moves only the rows BELOW it and one
            // matrix cannot say that: a slot moves the whole list or none of it.
            RenderDirty.MarkSubtreeGeometry(container);

            if (!animate) continue;
            if (container.RenderTransform is not { } transform)
            {
                transform = new Transform();
                container.RenderTransform = transform;
            }

            // Start where it WAS and run to zero: the element is already arranged at its new place, so the transform only
            // carries the leftover distance. FillBehavior.Stop leaves nothing behind once it lands.
            transform.BeginAnimation(Transform.TranslateXProperty,
                new DoubleAnimation { From = before.X - now.X, To = 0, Duration = LayoutMoveDuration, FillBehavior = FillBehavior.Stop });
            transform.BeginAnimation(Transform.TranslateYProperty,
                new DoubleAnimation { From = before.Y - now.Y, To = 0, Duration = LayoutMoveDuration, FillBehavior = FillBehavior.Stop });
        }
    }

    private UIComponent _gapCard;   // the drop placeholder; separate from the skeleton pool ON PURPOSE - see below

    /// <summary>Puts the drop placeholder in the open gap. Not a loading skeleton and not counted in
    /// <c>IsLoadingItems</c>. Call from ArrangeVirtualized.</summary>
    protected void ReconcileDropPlaceholder(Func<int, Rect> slotRect)
    {
        var gap = DropGapIndex;
        var template = Owner?.DropPlaceholderTemplate;
        if (gap < 0 || template == null)
        {
            if (_gapCard != null) _gapCard.Visibility = Visibility.Collapsed;
            return;
        }

        if (_gapCard == null)
        {
            _gapCard = template.Build(this).RootComponent as UIComponent;
            if (_gapCard == null) return;
            _skeletonSet.Add(_gapCard);   // same "not a container" exemption the skeleton prototype gets
            AddVisualChild(_gapCard);
        }

        _gapCard.Visibility = Visibility.Visible;
        var rect = slotRect(gap).Deflate(ItemMargin());
        var measurable = (IMeasurableComponent)_gapCard;
        measurable.Measure(new Size(rect.Width, rect.Height));
        measurable.Arrange(rect);
    }

    // Drop all skeleton state - called from Revirtualize, which detaches every visual child (the prototype among them),
    // so a stale reference would otherwise point at a card no longer in the tree. Also forgets the item margin (the
    // ItemTemplate may have changed).
    private void ResetSkeletons()
    {
        // Hide FIRST: that is what drops IsLoadingItems (SyncLoadingState) and so fires the theme's ExitAction, which
        // stops the shared pulse in the static AnimationManager. Dropping the reference below without it would leave the
        // list reporting "loading" forever and the pulse ticking with no card on screen.
        HideSkeletons();
        _skeletonPrototype = null;
        _prototypeSize = default;
        _skeletonSet.Clear();
        _itemMarginKnown = false;
    }

    // A tab switch (or any subtree removal) detaches the panel while loading skeletons are still on screen. Detach alone
    // changes nothing about the list's loading state, so the theme's ExitAction would never fire and the shared pulse
    // would keep ticking in the static AnimationManager for a list nobody sees (the "switch tabs mid-load -> FPS never
    // recovers" report). Hide now to fire the stop; a re-attach + re-fill re-shows them and the pulse restarts.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HideSkeletons();
    }

    // The margin a real item's template leaves around each tile - read ONCE from a realized item so a skeleton card
    // lines up EXACTLY with the real tiles (never a hardcoded guess). Cached; ResetSkeletons re-reads it after a
    // regenerate, since a new ItemTemplate can have a different margin.
    private Thickness _itemMargin;
    private bool _itemMarginKnown;

    private Thickness ItemMargin()
    {
        if (_itemMarginKnown) return _itemMargin;
        var container = Owner?.ItemContainerGenerator.AnyRealizedContainer();
        if (container == null) return default;   // nothing realized yet - stay uncached, try again next frame
        _itemMargin = FindItemMargin(container);
        _itemMarginKnown = true;
        return _itemMargin;
    }

    // The item VISUAL (the ItemTemplate's root) carries the tile margin; the container/presenter chrome around it does
    // not. First descendant with a non-zero margin wins.
    private static Thickness FindItemMargin(IUIComponent node)
    {
        if (node is IMeasurableComponent m && !IsZero(m.EffectiveMargin)) return m.EffectiveMargin;
        foreach (var child in node.VisualChildren)
        {
            var found = FindItemMargin(child);
            if (!IsZero(found)) return found;
        }
        return default;
    }

    private static bool IsZero(Thickness t) => t.Left == 0 && t.Top == 0 && t.Right == 0 && t.Bottom == 0;

    /// <summary>Called when the scroll axis is unbounded (no viewport) so everything has to be realized. Override to log.</summary>
    protected virtual void OnNoViewport()
    {
        System.Diagnostics.Debug.WriteLine(
            $"[Adamantium] {GetType().Name} has no bounded viewport on its scroll axis - realizing all {Owner?.Items.Count} items (not virtualizing). Wrap the ItemsControl in a sized ScrollViewer.");
    }

    private void RaiseMetrics() => ScrollMetricsChanged?.Invoke(this, EventArgs.Empty);

    private static Vector2 ClampOffset(Vector2 offset, Size extent, Size viewport)
    {
        var maxX = Math.Max(0, extent.Width - viewport.Width);
        var maxY = Math.Max(0, extent.Height - viewport.Height);
        return new Vector2(Math.Clamp(offset.X, 0, maxX), Math.Clamp(offset.Y, 0, maxY));
    }
}
