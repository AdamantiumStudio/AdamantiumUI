using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls;

/// <summary>
/// Holds the open <see cref="Popup"/>s of a window and lays their children out on the window-wide overlay (NOT part of
/// the content visual tree, so it draws on top). Each frame it re-evaluates every popup's position from its target's
/// CURRENT world position - so a popup follows a moving target - and arranges the child there, clamped inside the window.
/// The renderer consumes <see cref="Roots"/> (the laid-out children) as a flat list of subtrees to draw last.
/// </summary>
public class PopupLayer
{
    private readonly List<Popup> _popups = [];
    private readonly Dictionary<IUIComponent, Rect> _lastRect = new();   // last arranged slot per popup child (arrange gate)
    private readonly Dictionary<IUIComponent, Size<double>> _lastRoom = new();   // last room a StaysBesideTarget child was measured in
    private readonly HashSet<IUIComponent> _flippedAcross = [];
    // _popups/_lastRect are READ on the render (loop) thread (UpdateLayout/Roots) and MUTATED on the pump thread
    // (Popup.Open/Close -> Add/Remove: a dialog opened from a command, a light-dismissed menu). Without this a popup
    // opened/closed mid-render corrupts the enumeration ("Collection was modified"). Guard every touch.
    private readonly object _sync = new();
    private PointerPressedEventHandler _overlayPress;

    /// <summary>The laid-out child of every open popup (declaration order = back-to-front), for the overlay to render.</summary>
    public IReadOnlyList<IUIComponent> Roots
    {
        get
        {
            // Read ChildValue (a lock-free FIELD), NOT Child (a property whose GetValue takes the component lock). Taking a
            // component lock while holding _sync inverts against Popup.Close, whose IsOpen SetValue holds the component lock
            // then wants _sync (via Remove) - a deadlock the render thread hit reading roots exactly as a DropDown closed.
            lock (_sync) return _popups.Select(p => p.ChildValue).OfType<IUIComponent>().ToList();
        }
    }

    public bool HasPopups { get { lock (_sync) return _popups.Count > 0; } }

    /// <summary>The window's client size from the last layout pass - what a popup caps its scrollable content to so a menu
    /// taller than the window scrolls instead of clipping. Zero until the first sized pass.</summary>
    public Size<double> WindowSize { get; private set; }

    /// <summary>The window this layer belongs to. A popup's child is rendered DETACHED - it has no visual path back to
    /// the window - so anything that has to walk out of an overlay (the focus ring looking for the layer to draw itself
    /// on, most of all) is told the way back when the popup is put on the layer. Set by the window that owns the layer.</summary>
    public IPopupHost Owner { get; init; }

    public void Add(Popup popup)
    {
        lock (_sync)
        {
            if (_popups.Contains(popup)) return;
            _popups.Add(popup);
        }
        // HERE, not in Popup.Open: being ON the layer is what makes an overlay root belong to a window, and there is more
        // than one way onto it - an OverlayWindow is added to the layer DIRECTLY, never through IsOpen. Registering in
        // the IsOpen path alone left a dialog's content with no route back to the window, so the keyboard could move
        // around inside it with no focus ring anywhere: the ring asks for the window's adorner layer and got null.
        if (Owner != null && popup.ChildValue is { } overlayRoot) Popup.RegisterOverlayRoot(overlayRoot, Owner, popup);
        // Its render units were disposed when it last closed, but its components are still geometry-VALID (closing doesn't
        // invalidate layout), so a clean reopen would record nothing and the cache would "reuse" the disposed units (the
        // fill + border vanish, only re-dirtied text rebuilds). Mark the whole subtree dirty so the next layout re-measures
        // it and the render cache re-records its units.
        if (popup.ChildValue is { } child) InvalidateSubtree(child);
        // Attaching gives the content the normal lifecycle (OnAttached, triggers); its layout stays this layer's job, see
        // RemeasureIfDirty.
        AttachToOwner(popup.ChildValue as UIComponent);

        _overlayPress ??= (_, e) => Popup.PressedInOverlay(Owner, e);
        (popup.ChildValue as IInputComponent)?.AddHandler(PointerEvents.PreviewPointerPressedEvent, _overlayPress, handledEventsToo: true);
    }

    public void Remove(Popup popup)
    {
        lock (_sync)
        {
            _popups.Remove(popup);
            if (popup.ChildValue is IUIComponent c)   // ChildValue = lock-free field (see Roots): no component lock under _sync
            {
                _lastRect.Remove(c);
                _lastRoom.Remove(c);
            }
        }
        if (popup.ChildValue is not { } overlayRoot) return;
        if (_overlayPress != null)
        {
            (overlayRoot as IInputComponent)?.RemoveHandler(PointerEvents.PreviewPointerPressedEvent, _overlayPress);
        }

        // Take the ring down BEFORE the way back out is forgotten - after that nothing inside can reach the layer.
        (Owner as Adorners.IAdornerHost)?.AdornerLayer.ClearFocusWithin(overlayRoot);
        Popup.UnregisterOverlayRoot(overlayRoot);
        (overlayRoot as UIComponent)?.DetachFromRoot();
    }

    // The root the content joins is the owning WINDOW's - the same one that already measures, arranges and draws it on
    // this layer. Nothing becomes anyone's visual child: only the answer to "which root do I live in" changes, so a walk
    // from the root still does not reach overlay content.
    private void AttachToOwner(UIComponent content)
    {
        if (content == null) return;

        var root = (Owner as IUIComponent)?.RootVisual ?? Owner as IRootVisualComponent;
        content.AttachToRoot(root, ownsLayout: true);
    }

    /// <summary>
    /// Re-evaluate every open popup's position from its target's CURRENT world position (so it follows a moving target)
    /// and lay its child out there, clamped inside the window. Called each frame, after the main layout, before render.
    /// </summary>
    public void UpdateLayout(Size<double> windowSize)
    {
        if (!IsFinitePositive(windowSize.Width) || !IsFinitePositive(windowSize.Height)) return;   // window not sized yet
        WindowSize = windowSize;

        // Iterate a snapshot: a popup can open/close (Add/Remove on the pump thread) while this lays out on the render thread.
        Popup[] snapshot;
        lock (_sync) snapshot = _popups.ToArray();
        _flippedAcross.Clear();

        foreach (var popup in snapshot)
        {
            if (popup.ChildValue is not MeasurableUIComponent child) continue;

            // One tick per frame while the popup is on the layer. This subtree is laid out HERE and nowhere else - the
            // window's LayoutManager never runs over it (it is the layer's, by LayoutRoot), so its LayoutUpdated never
            // fires for it. Anything that must wait for the content - a menu opened from the keyboard cannot step into
            // rows that do not exist yet - waits on this and retries until its condition holds.
            popup.NotifyLayerPass();
            LayoutManager.For(child).SettleOverlay();

            // Full-window overlay (e.g. a modal dialog scrim): cover the whole window at the origin - no edge, no target.
            if (popup.FillWindow)
            {
                var filled = RemeasureIfDirty(child, windowSize);
                ArrangeIfNeeded(child, new Rect(0, 0, windowSize.Width, windowSize.Height), filled);
                continue;
            }

            // Edge-docked (SlidePanel): pin to a window edge; cross axis fills the window, main axis content or fill.
            if (popup.DockEdge is { } edge)
            {
                LayoutDocked(edge, popup.DockFill, child, windowSize);
                continue;
            }

            // Measure unconstrained for the content's own size - or within the room on its side of the target, for one
            // that stays beside it, again whenever that room changes - only when dirty; NaN or non-positive sizes must not
            // reach arrange or the renderer.
            var available = new Size<double>(double.PositiveInfinity, double.PositiveInfinity);
            var roomChanged = false;
            if (popup.StaysBesideTarget)
            {
                available = RoomBeside(popup, windowSize);
                lock (_sync)
                {
                    roomChanged = !_lastRoom.TryGetValue(child, out var lastRoom) || lastRoom != available;
                    _lastRoom[child] = available;
                }
            }

            var remeasured = RemeasureIfDirty(child, available, roomChanged);
            var size = child.DesiredSize;

            if (!IsFinitePositive(size.Width) || !IsFinitePositive(size.Height)) continue;

            // Never larger than the window (a degenerate huge content can't blow up the render target).
            size = new Size<double>(Math.Min(size.Width, windowSize.Width), Math.Min(size.Height, windowSize.Height));
            var pos = ComputePosition(popup, size, windowSize, popup.FlipToFit && InFlippedCard(popup), out var across);
            if (across) _flippedAcross.Add(child);
            if (double.IsNaN(pos.X) || double.IsNaN(pos.Y)) continue;

            ArrangeIfNeeded(child, new Rect(pos.X, pos.Y, size.Width, size.Height), remeasured);
        }
    }

    // Dock a child to a window edge. The CROSS axis (along the edge) ALWAYS fills the window. The MAIN axis (thickness,
    // the slide direction) is the child's own content/explicit size, unless <paramref name="fill"/> is set - then it
    // fills the window too (a full-window panel). Pinned to the edge; clamped to the window.
    private void LayoutDocked(Dock edge, bool fill, MeasurableUIComponent child, Size<double> win)
    {
        var horizontal = edge is Dock.Left or Dock.Right;   // main (thickness) axis is horizontal

        // Cross axis constrained to the window; main axis to the window when filling, else free (content/explicit).
        var measureW = horizontal ? (fill ? win.Width : double.PositiveInfinity) : win.Width;
        var measureH = horizontal ? win.Height : (fill ? win.Height : double.PositiveInfinity);
        var remeasured = RemeasureIfDirty(child, new Size<double>(measureW, measureH));

        var d = child.DesiredSize;
        var w = horizontal ? (fill ? win.Width : d.Width) : win.Width;
        var h = horizontal ? win.Height : (fill ? win.Height : d.Height);
        if (!IsFinitePositive(w) || !IsFinitePositive(h)) return;
        w = Math.Min(w, win.Width);
        h = Math.Min(h, win.Height);

        var x = edge == Dock.Right ? win.Width - w : 0;
        var y = edge == Dock.Bottom ? win.Height - h : 0;
        ArrangeIfNeeded(child, new Rect(x, y, w, h), remeasured);
    }

    // Re-measures a dirty overlay subtree and returns whether it did. A deep dirty node does not dirty its ancestors, so
    // the whole subtree is invalidated first for the measure to reach it.
    private static bool RemeasureIfDirty(MeasurableUIComponent child, Size<double> available, bool force = false)
    {
        if (!force && !NeedsLayout(child)) return false;
        InvalidateSubtree(child);
        child.Measure(available, force: true);
        return true;
    }

    // Arrange only when the slot changed or the content was just re-measured (which invalidated arrange). Arrange self-
    // gates internally too, but skipping the call keeps a STATIC docked panel from re-arranging every frame for nothing.
    private void ArrangeIfNeeded(MeasurableUIComponent child, Rect rect, bool remeasured)
    {
        // Re-arrange on a slot change, a re-measure, OR when any element inside went arrange-dirty on its own without a
        // re-measure - a SCROLL (the ScrollContentPresenter slides its content by -offset). Like the measure path, a deep
        // dirty node does NOT invalidate its ancestors, so find it by walking the subtree, then invalidate the whole subtree
        // so the forced re-arrange cascades down to it instead of gating at a still-valid ancestor.
        var arrangeDirty = NeedsArrange(child);
        lock (_sync)
            if (!remeasured && !arrangeDirty && _lastRect.TryGetValue(child, out var last) && last == rect) return;
        if (arrangeDirty) InvalidateArrangeSubtree(child);
        child.Arrange(rect, force: arrangeDirty);
        lock (_sync) _lastRect[child] = rect;
        LayoutManager.For(child).OverlayArranged();
    }

    private static bool NeedsArrange(IUIComponent node)
    {
        if (node == null) return false;
        if (node.Visibility == Visibility.Collapsed) return false;   // never arranged either - see NeedsLayout
        if (node is IMeasurableComponent { IsArrangeValid: false }) return true;
        foreach (var child in node.VisualChildren)
            if (NeedsArrange(child)) return true;
        return false;
    }

    private static void InvalidateArrangeSubtree(IUIComponent node)
    {
        if (node == null) return;
        (node as IMeasurableComponent)?.InvalidateArrange();
        foreach (var child in node.VisualChildren) InvalidateArrangeSubtree(child);
    }

    // True if any element in the detached subtree needs re-measuring (its IsMeasureValid was cleared by a content change
    // or by the open invalidation). Cheap - flag reads only, early-out on the first dirty node.
    private static bool NeedsLayout(IUIComponent node)
    {
        if (node == null) return false;
        // A collapsed subtree is never measured, so it would read dirty forever; becoming visible notifies the parent.
        if (node.Visibility == Visibility.Collapsed) return false;
        if (node is IMeasurableComponent { IsMeasureValid: false }) return true;

        foreach (var child in node.VisualChildren)
            if (NeedsLayout(child)) return true;
        return false;
    }

    // All four walks below tolerate an empty child: they run while content is attached or taken down.
    private static void InvalidateSubtree(IUIComponent node)
    {
        if (node == null) return;
        (node as IMeasurableComponent)?.InvalidateMeasure();
        foreach (var child in node.VisualChildren)
            InvalidateSubtree(child);
    }


    private static bool IsFinitePositive(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && v > 0;

    private static Size<double> RoomBeside(Popup popup, Size<double> windowSize)
    {
        var unbounded = double.PositiveInfinity;
        if (popup.EffectiveTarget is not { } target) return new Size<double>(unbounded, unbounded);

        var box = TargetBox(target);
        return popup.Placement switch
        {
            PlacementMode.Top => new Size<double>(unbounded, Math.Max(0, box.Y + popup.VerticalOffset)),
            PlacementMode.Left => new Size<double>(Math.Max(0, box.X + popup.HorizontalOffset), unbounded),
            PlacementMode.Right => new Size<double>(Math.Max(0, windowSize.Width - box.Right - popup.HorizontalOffset), unbounded),
            PlacementMode.Bottom => new Size<double>(unbounded, Math.Max(0, windowSize.Height - box.Bottom - popup.VerticalOffset)),
            _ => new Size<double>(unbounded, unbounded)
        };
    }

    private static Rect TargetBox(IUIComponent target)
    {
        var world = target.WorldTransform;
        var size = target.RenderSize;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        Take(0, 0);
        Take(size.Width, 0);
        Take(0, size.Height);
        Take(size.Width, size.Height);
        return new Rect(minX, minY, maxX - minX, maxY - minY);

        void Take(double x, double y)
        {
            var corner = Vector3F.TransformCoordinate(new Vector3F((float)x, (float)y, 0), world);
            minX = Math.Min(minX, corner.X);
            minY = Math.Min(minY, corner.Y);
            maxX = Math.Max(maxX, corner.X);
            maxY = Math.Max(maxY, corner.Y);
        }
    }

    private bool InFlippedCard(Popup popup)
    {
        IUIComponent node = popup.EffectiveTarget;
        while (node?.VisualParent != null) node = node.VisualParent;
        return node != null && _flippedAcross.Contains(node);
    }

    private static Vector2<double> ComputePosition(Popup popup, Size<double> size, Size<double> windowSize, bool swapped, out bool flippedAcross)
    {
        double tx = 0, ty = 0, tw = 0, th = 0;
        if (popup.EffectiveTarget is { } target)
        {
            var box = TargetBox(target);
            tx = box.X; ty = box.Y; tw = box.Width; th = box.Height;
        }

        var cx = Align(tx, tw, size.Width);
        var cy = Align(ty, th, size.Height);
        var dx = popup.HorizontalOffset;
        var dy = popup.VerticalOffset;
        var across = false;
        double x, y;
        switch (popup.Placement)
        {
            case PlacementMode.Top:      x = cx + dx; y = Pick(ty - size.Height + dy, ty + th - dy, size.Height, windowSize.Height, out _); break;
            case PlacementMode.Left:     x = Side(tx - size.Width + dx, tx + tw - dx); y = cy + dy; break;
            case PlacementMode.Right:    x = Side(tx + tw + dx, tx - size.Width - dx); y = cy + dy; break;
            case PlacementMode.Center:   x = tx + (tw - size.Width) / 2 + dx; y = ty + (th - size.Height) / 2 + dy; break;
            case PlacementMode.Relative: (x, y) = AtTargetPoint(dx, dy); break;
            default:                     x = cx + dx; y = Pick(ty + th + dy, ty - size.Height - dy, size.Height, windowSize.Height, out _); break;   // Bottom
        }

        flippedAcross = across;

        // Edge-aware flip: keep the natural side while the popup fits there OR overruns the window no more than on the
        // other side; otherwise flip, its offset mirrored. So near the bottom edge a Bottom popup opens upward, and when it
        // fits NEITHER side it opens on the roomier one (then the clamp below keeps it fully in the window).
        double Pick(double primary, double flipped, double extent, double room, out bool took)
        {
            took = popup.FlipToFit && Overrun(flipped, extent, room) < Overrun(primary, extent, room);
            return took ? flipped : primary;
        }

        double Side(double own, double other)
        {
            var at = swapped ? Pick(other, own, size.Width, windowSize.Width, out var back)
                : Pick(own, other, size.Width, windowSize.Width, out back);
            across = back != swapped;
            return at;
        }

        static double Overrun(double at, double extent, double room) => Math.Max(0, -at) + Math.Max(0, at + extent - room);

        (double X, double Y) AtTargetPoint(double ox, double oy)
        {
            if (popup.EffectiveTarget is not { } at)
            {
                return (tx + ox, ty + oy);
            }

            var point = Vector3F.TransformCoordinate(new Vector3F((float)ox, (float)oy, 0), at.WorldTransform);
            return (point.X, point.Y);
        }

        double Align(double start, double length, double extent) => popup.PlacementAlignment switch
        {
            PlacementAlignment.Start => start,
            PlacementAlignment.End => start + length - extent,
            _ => start + (length - extent) / 2
        };

        // Never leave the window.
        x = Math.Clamp(x, 0, Math.Max(0, windowSize.Width - size.Width));
        y = Math.Clamp(y, 0, Math.Max(0, windowSize.Height - size.Height));
        return new Vector2<double>((float)x, (float)y);
    }
}
