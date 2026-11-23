using System;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Generators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Panels;

public class StackPanel : VirtualizingPanel
{
   // Realize a couple of extra items on each side of the viewport so a fast scroll doesn't flash empty rows.
   private const int Buffer = 2;

   private double _itemExtent = 1;   // measured (uniform) item size along the stacking axis
   private int _lastFirst;           // remembered window start -> the probe index next pass
   private double _lastViewport;     // last FINITE scroll-axis viewport -> window size when a measure comes in infinite (tab-entry)
   private const double DefaultViewport = 1080.0;   // fallback viewport (px) before any real one is known
   private const int ParallelArrangeThreshold = 64;   // arrange tiles across cores only above this many realized (else thread overhead > win)
   private readonly List<int> _arrangeIndexBuf = [];  // reused each arrange - no per-frame List alloc over the window
   private readonly List<double> _itemStarts = [];    // non-virtualized: cumulative main-axis start per item (mixed heights)

   public static readonly AdamantiumProperty OrientationProperty = AdamantiumProperty.Register(nameof(Orientation),
      typeof(Orientation), typeof(StackPanel),
      new PropertyMetadata(Orientation.Vertical,
         PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

   /// <summary>The gap BETWEEN children along the stacking axis - not after the last one. The container's business
   /// rather than each child's: a margin per child repeats one number, trails a gap off the last one, and doubles up
   /// when a middle child is collapsed. Here a collapsed child takes neither slot nor gap.</summary>
   public static readonly AdamantiumProperty SpacingProperty = AdamantiumProperty.Register(nameof(Spacing),
      typeof(double), typeof(StackPanel),
      new PropertyMetadata(0.0,
         PropertyMetadataOptions.AffectsMeasure | PropertyMetadataOptions.AffectsArrange));

   /// <summary>Item extent PLUS the gap - one item's start to the next. EVERY place that turns an index into a position
   /// uses this and not the extent: the realized window, a slot's rectangle, the drop target under the pointer, and the
   /// answer for an item that is not realized. One of them disagreeing is a row drawn where the hit-test is not.
   /// </summary>
   private double Pitch => _itemExtent + Math.Max(0, Spacing);

   /// <summary>Items taller than the uniform extent, as (index, extra), ascending; null when all are uniform. Positions
   /// stay a binary search over these.</summary>
   protected virtual IReadOnlyList<(int Index, double Extra)> ItemExtentExceptions => null;

   // How much taller everything BEFORE this slot is than the uniform stack would make it. The exceptions are few and
   // sorted, so this is a short scan over them - never over the items.
   private double ExtraBefore(int slot)
   {
      var exceptions = ItemExtentExceptions;
      if (exceptions == null) return 0;

      double extra = 0;
      for (var i = 0; i < exceptions.Count && exceptions[i].Index < slot; i++) extra += OnGrid(exceptions[i].Extra);
      return extra;
   }

   private double ExtraAt(int index)
   {
      var exceptions = ItemExtentExceptions;
      if (exceptions == null) return 0;

      for (var i = 0; i < exceptions.Count; i++)
      {
         if (exceptions[i].Index == index) return OnGrid(exceptions[i].Extra);
         if (exceptions[i].Index > index) break;
      }

      return 0;
   }

   // The step of the baseline grid rows that lay out by their place stand on, or 0: every row then keeps the same phase.
   private double _gridPitch;

   private double OnGrid(double extent) => _gridPitch > 0 ? Math.Ceiling(extent / _gridPitch - 1e-6) * _gridPitch : extent;

   private double StartOf(int slot) => slot * Pitch + ExtraBefore(slot);

   private double ExtentAt(int index) => _itemExtent + ExtraAt(index);

   // The inverse of StartOf: which slot the given distance along the stack falls in. Walks the exceptions (few, sorted)
   // and divides inside the uniform stretch between them, so it stays arithmetic rather than a search over the items.
   private int SlotAt(double along)
   {
      var pitch = Pitch;
      if (pitch <= 0) return 0;

      var exceptions = ItemExtentExceptions;
      if (exceptions == null) return (int)Math.Floor(along / pitch);

      double extra = 0;
      for (var i = 0; i < exceptions.Count; i++)
      {
         var start = exceptions[i].Index * pitch + extra;
         if (along < start) break;   // the answer is in the uniform stretch before this exception

         extra += OnGrid(exceptions[i].Extra);
         if (along < start + pitch + OnGrid(exceptions[i].Extra)) return exceptions[i].Index;
      }

      return (int)Math.Floor((along - extra) / pitch);
   }


   /// <summary>Items stack at a uniform extent, so where the n-th one sits is arithmetic - and stays answerable for an
   /// item that has been virtualized away, which is exactly when someone needs to scroll to it.</summary>
   public override bool TryGetItemRect(int index, out Rect rect)
   {
      rect = default;
      if (!IsItemsHost || _itemExtent <= 0 || index < 0) return false;

      var vertical = Orientation == Orientation.Vertical;
      if (ShowsGroups)
      {
         // Not virtualized, a row is where it was stacked: one not stacked yet - added since - is nowhere to tell of.
         if (!IsVirtualizing && (index >= _itemStarts.Count || Owner.ItemContainerGenerator.ContainerFromIndex(index) == null))
            return false;

         rect = GroupedItemRect(index, vertical ? RenderSize.Width : RenderSize.Height);
         return true;
      }

      var along = StartOf(index);
      var extent = ExtentAt(index);
      rect = vertical
         ? new Rect(0, along, RenderSize.Width, extent)
         : new Rect(along, 0, extent, RenderSize.Height);
      return true;
   }

   /// <summary>Along the stack an arrow is the next child; ACROSS it there is nothing - a line of children has no
   /// second dimension - so the answer is null and the question goes to whatever holds this stack.</summary>
   public override IUIComponent Navigate(IUIComponent from, FocusNavigationDirection direction)
   {
      if (!IsArrow(direction)) return base.Navigate(from, direction);
      if (IsVertical(direction) != (Orientation == Orientation.Vertical)) return null;

      return Neighbor(from, IsForward(direction));
   }

   public Orientation Orientation
   {
      get => GetValue<Orientation>(OrientationProperty);
      set => SetValue(OrientationProperty, value);
   }

   public double Spacing
   {
      get => GetValue<double>(SpacingProperty);
      set => SetValue(SpacingProperty, value);
   }

   // ---- Plain container layout (unchanged behavior: a StackPanel used with explicit Children) ------------------

   protected override Size<double> MeasurePlain(Size<double> availableSize)
   {
      double childAvailableWidth = double.PositiveInfinity;
      double childAvailableHeight = double.PositiveInfinity;

      if (Orientation == Orientation.Vertical)
      {
         childAvailableWidth = availableSize.Width;

         if (!double.IsNaN(Width))
         {
            childAvailableWidth = Width;
         }

         childAvailableWidth = Math.Min(childAvailableWidth, MaxWidth);
         childAvailableWidth = Math.Max(childAvailableWidth, MinWidth);
      }
      else
      {
         childAvailableHeight = availableSize.Height;

         if (!double.IsNaN(Height))
         {
            childAvailableHeight = Height;
         }

         childAvailableHeight = Math.Min(childAvailableHeight, MaxHeight);
         childAvailableHeight = Math.Max(childAvailableHeight, MinHeight);
      }

      double measuredWidth = 0;
      double measuredHeight = 0;
      var gap = Math.Max(0, Spacing);
      var stacked = 0;   // children that actually take a slot - a collapsed one takes neither slot nor gap

      foreach (var child in Children)
      {
         if (child is MeasurableUIComponent { Visibility: not Visibility.Collapsed } placedChild)
         {
            if (Orientation == Orientation.Vertical)
            {
               placedChild.PlaceAt(measuredHeight + gap * stacked, placedChild.DesiredSize.Height);
            }
            else
            {
               placedChild.PlaceAt(0, double.NaN);
            }
         }

         child.Measure(new Size<double>(childAvailableWidth, childAvailableHeight));
         if (child.Visibility == Visibility.Collapsed) continue;
         Size<double> size = child.DesiredSize;
         stacked++;

         if (Orientation == Orientation.Vertical)
         {
            measuredHeight += size.Height;
            measuredWidth = Math.Max(measuredWidth, size.Width);
         }
         else
         {
            measuredWidth += size.Width;
            measuredHeight = Math.Max(measuredHeight, size.Height);
         }
      }

      // BETWEEN, so one fewer gap than children and none at all for a single child.
      var gaps = gap * Math.Max(0, stacked - 1);
      if (Orientation == Orientation.Vertical) measuredHeight += gaps;
      else measuredWidth += gaps;

      return new Size<double>(measuredWidth, measuredHeight);
   }

   protected override Size<double> ArrangePlain(Size<double> finalSize)
   {
      var horizontal = Orientation == Orientation.Horizontal;

      // Cross-axis = the CONTENT extent (DesiredSize: the tallest/widest child, or an explicit Width/Height), clamped to
      // the slot - never the full slot on its own. A stack only occupies what it stacks, so it must not REPORT (and
      // therefore hit-test) cross space it doesn't use: a horizontal Stretch stack otherwise swallowed the whole window
      // height for input, blocking everything beneath it. That clamp is the panel's RETURN value below.
      double cross = horizontal
         ? Math.Min(finalSize.Height, DesiredSize.Height)
         : Math.Min(finalSize.Width, DesiredSize.Width);

      // Children, however, get the FULL cross slot so a Stretch child fills the panel's width (a full-width row - a
      // TreeView/list item that spans the viewport, clickable + highlight edge-to-edge); a non-Stretch child sizes to its
      // content within that slot (its own Arrange aligns it). Clamping the CHILD to `cross` - as this used to - is what
      // pinned every Stretch child to content width. Guard infinity (an unconstrained arrange) back to the content extent.
      double childCross = horizontal
         ? (double.IsInfinity(finalSize.Height) ? cross : finalSize.Height)
         : (double.IsInfinity(finalSize.Width) ? cross : finalSize.Width);

      double main = 0;
      var gap = Math.Max(0, Spacing);
      var placed = false;
      foreach (var child in Children)
      {
         if (child.Visibility == Visibility.Collapsed) continue;
         if (placed) main += gap;   // between the previous child and this one, never after the last
         placed = true;

         if (horizontal)
         {
            child.Arrange(new Rect(main, 0, child.DesiredSize.Width, childCross));
            main += child.DesiredSize.Width;
         }
         else
         {
            child.Arrange(new Rect(0, main, childCross, child.DesiredSize.Height));
            main += child.DesiredSize.Height;
         }
      }

      return horizontal ? new Size<double>(main, cross) : new Size<double>(cross, main);
   }

   // ---- Virtualized layout (items host): realize only the visible window, scroll along Orientation -------------

   protected override Size<double> MeasureVirtualized(Size<double> availableSize, Vector2<double> offset)
   {
      var vertical = Orientation == Orientation.Vertical;
      var count = Owner.Items.Count;
      if (count == 0)
      {
         foreach (var c in Owner.ItemContainerGenerator.SetWindow(0, -1)) ParkContainer(c);
         SetHeaderWindow(0, -1);
         return new Size<double>();
      }

      var crossAvailable = vertical ? availableSize.Width : availableSize.Height;
      var mainViewport = vertical ? availableSize.Height : availableSize.Width;
      var childConstraint = vertical
         ? new Size<double>(crossAvailable, double.PositiveInfinity)
         : new Size<double>(double.PositiveInfinity, crossAvailable);

      if (ShowsGroups)
      {
         return IsVirtualizing
            ? MeasureGroupedWindow(vertical, count, mainViewport, childConstraint, offset)
            : MeasureGroupedAll(vertical, count, childConstraint);
      }

      // No groups (any more): headers a grouped pass made are put away.
      SetHeaderWindow(0, -1);

      // Non-virtualized host (a small mixed-height menu): realize + measure EVERY item at its OWN extent and stack them by
      // cumulative offset, so a short row (a separator) takes only its own height instead of a uniform cell.
      if (!IsVirtualizing)
      {
         foreach (var c in Owner.ItemContainerGenerator.SetWindow(0, count - 1)) ParkContainer(c);   // realize all
         _itemStarts.Clear();
         double runningMain = 0, crossMaxAll = 0;
         for (var i = 0; i < count; i++)
         {
            var container = (IMeasurableComponent)RealizeInWindow(i);
            if (container is MeasurableUIComponent placedRow)
            {
               placedRow.PlaceAt(vertical ? runningMain + (i > 0 ? Math.Max(0, Spacing) : 0) : 0,
                  vertical ? placedRow.DesiredSize.Height : double.NaN);
            }

            container.Measure(childConstraint);
            if (i > 0) runningMain += Math.Max(0, Spacing);   // between rows only
            _itemStarts.Add(runningMain);
            runningMain += vertical ? container.DesiredSize.Height : container.DesiredSize.Width;
            crossMaxAll = Math.Max(crossMaxAll, vertical ? container.DesiredSize.Width : container.DesiredSize.Height);
         }
         return vertical ? new Size<double>(crossMaxAll, runningMain) : new Size<double>(runningMain, crossMaxAll);
      }

      // Probe the item at the last window start for the uniform extent, never an exception item. Keep the last good size
      // if the probe measures zero mid-rebind, or the extent collapses and mis-clamps the offset.
      var probeIndex = Math.Clamp(_lastFirst, 0, count - 1);
      while (ExtraAt(probeIndex) != 0 && probeIndex + 1 < count) probeIndex++;
      var probe = (IMeasurableComponent)RealizeInWindow(probeIndex);
      if (vertical && probe is MeasurableUIComponent placedProbe)
      {
         var probeSlot = SlotOf(probeIndex);
         placedProbe.PlaceAt(StartOf(probeSlot), ExtentAt(probeSlot));
      }

      probe.Measure(childConstraint);
      var probeExtent = vertical ? probe.DesiredSize.Height : probe.DesiredSize.Width;
      // Rows laid out on a baseline grid each stand at the same phase of it, so each lays out as the probe did.
      var gridStep = BaselineGridStep;
      _gridPitch = vertical && gridStep > 0 && !double.IsInfinity(gridStep) && LaysOutByPlace(probe) ? gridStep : 0;
      if (probeExtent > 0 && _gridPitch > 0)
      {
         var gap = Math.Max(0, Spacing);
         probeExtent = OnGrid(probeExtent + gap) - gap;
      }

      if (probeExtent > 0 && ExtraAt(probeIndex) == 0) _itemExtent = probeExtent;

      // An infinite viewport is the ScrollViewer's extent probe: realize a window sized to the last real viewport (or a
      // default screenful), not every item, and still return the full extent.
      double effectiveViewport;
      if (double.IsInfinity(mainViewport))
      {
         OnNoViewport();
         effectiveViewport = UnboundedWindow(vertical, _lastViewport, DefaultViewport);
      }
      else
      {
         effectiveViewport = mainViewport;
         _lastViewport = mainViewport;
      }

      var mainOffset = vertical ? offset.Y : offset.X;
      var first = Math.Max(0, SlotAt(mainOffset) - Buffer);
      var last = Math.Min(count - 1, SlotAt(mainOffset + effectiveViewport) + 1 + Buffer);
      _lastFirst = first;

      // Reconcile the realized set to exactly [first,last]: containers leaving the window are rebound in place to the
      // ones entering (no collapse/null churn). Only true surplus (window shrank, e.g. resize/list edge) is hidden.
      foreach (var c in Owner.ItemContainerGenerator.SetWindow(first, last)) ParkContainer(c);

      double crossMax = 0;
      for (var i = first; i <= last; i++)
      {
         var container = (IMeasurableComponent)RealizeInWindow(i);
         if (container is MeasurableUIComponent row)
         {
            var slot = SlotOf(i);
            row.PlaceAt(vertical ? StartOf(slot) : 0, vertical ? ExtentAt(slot) : double.NaN);
         }

         container.Measure(childConstraint);
         crossMax = Math.Max(crossMax, vertical ? container.DesiredSize.Width : container.DesiredSize.Height);
      }

      // count pitches less the trailing gap: the last item ends the extent, the gap after it does not exist. Plus what
      // the odd ones out add on top - the stack is uniform EXCEPT for them, and the scrollbar has to know it.
      var mainExtent = count * Pitch + ExtraBefore(count) - Math.Max(0, Spacing);
      return vertical ? new Size<double>(crossMax, mainExtent) : new Size<double>(mainExtent, crossMax);
   }

   protected override void ArrangeVirtualized(Size<double> finalSize, Vector2<double> offset)
   {
      var vertical = Orientation == Orientation.Vertical;
      var cross = vertical ? finalSize.Width : finalSize.Height;

      if (ShowsGroups)
      {
         ArrangeGrouped(vertical, cross);
         return;
      }

      // Non-virtualized host: arrange each item at its cumulative start (from measure) with its OWN main extent.
      if (!IsVirtualizing)
      {
         var count = Owner.Items.Count;
         for (var i = 0; i < count && i < _itemStarts.Count; i++)
         {
            if (Owner.ItemContainerGenerator.ContainerFromIndex(i) is not IMeasurableComponent container) continue;
            var start = _itemStarts[i];
            var mainSize = vertical ? container.DesiredSize.Height : container.DesiredSize.Width;
            container.Arrange(vertical ? new Rect(0, start, cross, mainSize) : new Rect(start, 0, mainSize, cross));
         }
         return;
      }

      // ABSOLUTE slots (no -offset): the scroll offset is applied once, by the ScrollContentPresenter translating this
      // viewport-sized panel and clipping (same physical-scroll seam as WrapPanel). A tile's rect is constant across
      // scroll, so Arrange short-circuits for tiles that kept their index; only the rebound row re-runs ArrangeCore.
      // Snapshot indices into a REUSED buffer (not a fresh ToList): the window scan runs every scroll frame.
      _arrangeIndexBuf.Clear();
      _arrangeIndexBuf.AddRange(Owner.ItemContainerGenerator.RealizedIndices);

      // SLOT, not index: while a drop gap is open everything from it on moves along by one, opening one item-sized hole.
      Rect SlotRect(int slot) => vertical
         ? new Rect(0, StartOf(slot), cross, ExtentAt(slot))
         : new Rect(StartOf(slot), 0, ExtentAt(slot), cross);

      void ArrangeAt(int index)
      {
         if (Owner.ItemContainerGenerator.ContainerFromIndex(index) is not IMeasurableComponent container) return;
         container.Arrange(SlotRect(SlotOf(index)));
      }

      // Each tile's slot is CONSTANT from its index (absolute stack, no cumulative dependency), so the tiles' Arrange are
      // INDEPENDENT - fan them across cores when there are enough to amortize the thread overhead (same pattern + safety
      // as WrapPanel: the only shared write a tile arrange makes is RenderDirty.MarkGeometry, which is locked). Small
      // windows stay sequential.
      if (_arrangeIndexBuf.Count >= ParallelArrangeThreshold)
      {
         ParallelArrange.Run(this, _arrangeIndexBuf.Count, Owner.ItemContainerGenerator.IndexFromContainer,
            i => ArrangeAt(_arrangeIndexBuf[i]));
      }
      else
      {
         foreach (var index in _arrangeIndexBuf)
         {
            ArrangeAt(index);
         }
      }

      AnimateLayoutMoves(SlotRect);
      ReconcileDropPlaceholder(SlotRect);
   }

   // ---- Groups: each a header line, then its items --------------------------------------------------------------

   private readonly GroupLines _groupLines = new();
   private readonly List<double> _headerStarts = [];   // non-virtualized: cumulative start per header place

   // Virtualized: items at the uniform extent as without groups - rows of another height (ItemExtentExceptions) with
   // theirs - a header's extent by its level, and the window the items and headers whose lines it crosses.
   private Size<double> MeasureGroupedWindow(bool vertical, int count, double mainViewport, Size<double> childConstraint,
      Vector2<double> offset)
   {
      var probeIndex = Math.Clamp(_lastFirst, 0, count - 1);
      while (ExtraAt(probeIndex) != 0 && probeIndex + 1 < count) probeIndex++;
      var probe = (IMeasurableComponent)RealizeInWindow(probeIndex);
      probe.Measure(childConstraint);
      var probeExtent = vertical ? probe.DesiredSize.Height : probe.DesiredSize.Width;
      if (probeExtent > 0 && ExtraAt(probeIndex) == 0) _itemExtent = probeExtent;

      ProbeHeaderExtents(childConstraint, vertical);
      var gap = Math.Max(0, Spacing);
      var gapPlace = DropGapPlace(out var gapSlot);
      _groupLines.Build(Owner.GroupHeaderPlaces, count, 1, HeaderExtents, gap, Pitch, ItemExtentExceptions, gapPlace,
         gapSlot);

      double effectiveViewport;
      if (double.IsInfinity(mainViewport))
      {
         OnNoViewport();
         effectiveViewport = UnboundedWindow(vertical, _lastViewport, DefaultViewport);
      }
      else
      {
         effectiveViewport = mainViewport;
         _lastViewport = mainViewport;
      }

      var mainOffset = vertical ? offset.Y : offset.X;
      var lead = Buffer * Pitch;
      var window = _groupLines.Window(mainOffset - lead, mainOffset + effectiveViewport + lead);
      _lastFirst = window.FirstItem;

      // A folded group's rows are inside the window's span, not in what it shows: none of them is made.
      var folded = _groupLines.FoldedItems;
      foreach (var c in Owner.ItemContainerGenerator.SetWindow(window.FirstItem, window.LastItem, folded)) ParkContainer(c);
      double crossMax = 0;
      var range = 0;
      for (var i = window.FirstItem; i <= window.LastItem; i++)
      {
         // Past a folded group's rows in one step - a folded group can be thousands of them.
         while (range < folded.Count && folded[range].Last < i) range++;
         if (range < folded.Count && folded[range].Contains(i))
         {
            i = folded[range].Last;
            continue;
         }

         var container = (IMeasurableComponent)RealizeInWindow(i);
         container.Measure(childConstraint);
         crossMax = Math.Max(crossMax, vertical ? container.DesiredSize.Width : container.DesiredSize.Height);
      }

      ShowHeadersInWindow(window, childConstraint, vertical);
      for (var place = window.FirstPlace; place <= window.LastPlace; place++)
      {
         var shown = Owner.GroupHeaderGenerator.HeaderAt(place);
         crossMax = Math.Max(crossMax, vertical ? shown.DesiredSize.Width : shown.DesiredSize.Height);
      }

      var mainExtent = Math.Max(0, _groupLines.Extent - gap);
      return vertical ? new Size<double>(crossMax, mainExtent) : new Size<double>(mainExtent, crossMax);
   }

   // Not virtualized: every item and every header at its own extent, stacked in order - a group's headers before its
   // first item; a folded group's items are put away, and stand where its header does.
   private Size<double> MeasureGroupedAll(bool vertical, int count, Size<double> childConstraint)
   {
      var places = Owner.GroupHeaderPlaces;
      foreach (var c in Owner.ItemContainerGenerator.SetWindow(0, count - 1)) ParkContainer(c);
      SetHeaderWindow(0, places.Count - 1);
      _itemStarts.Clear();
      _headerStarts.Clear();

      var gap = Math.Max(0, Spacing);
      double running = 0, crossMax = 0;
      var stacked = 0;
      var place = 0;
      var shown = true;
      var headerStart = 0.0;
      for (var i = 0; i < count; i++)
      {
         for (; place < places.Count && places[place].BeforeItem == i; place++)
         {
            var header = RealizeHeaderInWindow(place);
            header.Measure(childConstraint);
            if (stacked++ > 0) running += gap;
            headerStart = running;
            _headerStarts.Add(running);
            running += vertical ? header.DesiredSize.Height : header.DesiredSize.Width;
            crossMax = Math.Max(crossMax, vertical ? header.DesiredSize.Width : header.DesiredSize.Height);
            shown = places[place].IsExpanded;
         }

         if (!shown)
         {
            // Put away once, not shown and hidden again every pass.
            if (Owner.ItemContainerGenerator.ContainerFromIndex(i) is { Visibility: Visibility.Visible } folded)
               ParkContainer(folded);
            _itemStarts.Add(headerStart);
            continue;
         }

         var container = (IMeasurableComponent)RealizeInWindow(i);
         container.Measure(childConstraint);
         if (stacked++ > 0) running += gap;
         _itemStarts.Add(running);
         running += vertical ? container.DesiredSize.Height : container.DesiredSize.Width;
         crossMax = Math.Max(crossMax, vertical ? container.DesiredSize.Width : container.DesiredSize.Height);
      }

      return vertical ? new Size<double>(crossMax, running) : new Size<double>(running, crossMax);
   }

   private Rect GroupedItemRect(int index, double cross)
   {
      var vertical = Orientation == Orientation.Vertical;
      double along, extent;
      if (IsVirtualizing)
      {
         along = _groupLines.ItemStart(index, out _);
         extent = _groupLines.ItemExtent(index) - Math.Max(0, Spacing);
      }
      else if (index < _itemStarts.Count && Owner.ItemContainerGenerator.ContainerFromIndex(index) is IMeasurableComponent item)
      {
         along = _itemStarts[index];
         extent = vertical ? item.DesiredSize.Height : item.DesiredSize.Width;
      }
      else
      {
         along = 0;
         extent = 0;
      }

      return vertical ? new Rect(0, along, cross, extent) : new Rect(along, 0, extent, cross);
   }

   private void ArrangeGrouped(bool vertical, double cross)
   {
      Rect Line(double start, double extent) => vertical ? new Rect(0, start, cross, extent) : new Rect(start, 0, extent, cross);

      foreach (var index in Owner.ItemContainerGenerator.RealizedIndices)
      {
         if (Owner.ItemContainerGenerator.ContainerFromIndex(index) is not IMeasurableComponent container) continue;
         if (container.Visibility != Visibility.Visible) continue;   // a folded group's, put away
         container.Arrange(GroupedItemRect(index, cross));
      }

      foreach (var place in Owner.GroupHeaderGenerator.RealizedPlaces)
      {
         var header = Owner.GroupHeaderGenerator.HeaderAt(place);
         if (IsVirtualizing)
         {
            header.Arrange(Line(_groupLines.HeaderStart(place), _groupLines.HeaderExtent(place) - Math.Max(0, Spacing)));
         }
         else if (place < _headerStarts.Count)
         {
            header.Arrange(Line(_headerStarts[place], vertical ? header.DesiredSize.Height : header.DesiredSize.Width));
         }
      }

      if (IsVirtualizing)
      {
         AnimateLayoutMoves(index => GroupedItemRect(index, cross));
         ShowDropPlaceholder(_groupLines.TryGetGapStart(out var gapStart, out _)
            ? Line(gapStart, Pitch - Math.Max(0, Spacing))
            : null);
      }
   }

   // A uniform stack derives every slot from an index, exactly as the grid does, so the hole is one multiplication.
   public override bool SupportsDropGap => IsVirtualizing;

   /// <summary>Which slot a drop lands in, from the STACK's own arithmetic rather than from where the containers
   /// currently sit - the gap moves those, and an index read off them would chase itself. See VirtualizingPanel.
   /// Grouped, from the lines as they stand: nothing before the gap moves for it. A stack that does not virtualize opens
   /// no gap, but still says which group a drop lands in, from where it stacked each row.</summary>
   public override bool TryGetDropSlot(Vector2<double> point, out DropSlot slot)
   {
      slot = DropSlot.None;
      if (!IsItemsHost) return false;

      var main = Orientation == Orientation.Vertical ? point.Y : point.X;
      if (!IsVirtualizing)
      {
         return ShowsGroups && TryGetStackedDropSlot(main, out slot);
      }

      if (_itemExtent <= 1) return false;

      if (ShowsGroups)
      {
         if (!_groupLines.TryGetDropSlot(main, 0, out var inGroup)) return false;
         slot = DropSlotOf(inGroup);
         return true;
      }

      slot = new DropSlot(Math.Clamp(SlotAt(Math.Max(0, main)), 0, Owner.Items?.Count ?? 0));
      return true;
   }

   // Not virtualized, rows of their own heights: the group whose header starts last at or before the point; on its
   // header, the start of an open group - of its first inner group, for an outer one - or the end of a folded one; among
   // its rows, before the first whose middle is past the point.
   private bool TryGetStackedDropSlot(double along, out DropSlot slot)
   {
      slot = DropSlot.None;
      var places = Owner.GroupHeaderPlaces;
      var count = Owner.Items.Count;
      if (places.Count == 0 || _headerStarts.Count != places.Count || _itemStarts.Count != count) return false;

      var vertical = Orientation == Orientation.Vertical;
      var place = 0;
      while (place + 1 < places.Count && _headerStarts[place + 1] <= along) place++;

      int GroupEnd(int at) => at + 1 < places.Count ? places[at + 1].BeforeItem : count;
      var header = Owner.GroupHeaderGenerator.HeaderAt(place);
      var headerEnd = _headerStarts[place] + (header == null ? 0 : vertical ? header.DesiredSize.Height : header.DesiredSize.Width);
      int slotInGroup;
      if (along < headerEnd)
      {
         while (places[place].IsExpanded && GroupEnd(place) == places[place].BeforeItem && place + 1 < places.Count &&
                places[place + 1].BeforeItem == places[place].BeforeItem)
         {
            place++;
         }

         slotInGroup = places[place].IsExpanded ? 0 : GroupEnd(place) - places[place].BeforeItem;
      }
      else
      {
         var first = places[place].BeforeItem;
         slotInGroup = GroupEnd(place) - first;
         for (var index = first; index < GroupEnd(place); index++)
         {
            if (Owner.ItemContainerGenerator.ContainerFromIndex(index) is not IMeasurableComponent row) continue;
            var extent = vertical ? row.DesiredSize.Height : row.DesiredSize.Width;
            if (along < _itemStarts[index] + extent / 2)
            {
               slotInGroup = index - first;
               break;
            }
         }
      }

      var start = places[place].BeforeItem;
      slot = DropSlotOf(new GroupDropSlot(place, start, GroupEnd(place) - start, slotInGroup));
      return true;
   }
}
