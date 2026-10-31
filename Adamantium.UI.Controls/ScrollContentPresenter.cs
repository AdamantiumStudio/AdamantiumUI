using System;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Animation;

namespace Adamantium.UI.Controls;

/// <summary>
/// The default physical <see cref="IScrollableContent"/>: it hosts a <see cref="ScrollViewer"/>'s content, measures
/// it unbounded on the scrollable axes (so it reports its full extent), and shows a window onto it by translating the
/// child by -<see cref="Offset"/> and clipping to the viewport (<see cref="UIComponent.ClipToBounds"/> -&gt; a
/// renderer scissor). A virtualizing panel can replace it later by implementing the same seam.
/// </summary>
public class ScrollContentPresenter : ContentPresenter, IScrollableContent
{
    // Pan (touch-style content drag): grab anywhere on the content and drag to scroll, no scrollbars needed. A small
    // threshold keeps a plain click on interactive content from being swallowed as a pan.
    private const double PanThreshold = 4;
    private bool _isPanning;
    private Vector2<double> _panStartPoint;
    private Vector2<double> _panStartOffset;

    private Pointer _finger;
    private Vector2<double> _fingerStart;
    private Vector2<double> _fingerStartOffset;
    private bool _fingerScrolling;

    private Size<double> _extent;
    private Size<double> _viewport;
    private Vector2<double> _offset;
    private readonly PanInertia _inertia;

    /// <summary>Flick velocity decay per second - higher stops a fling sooner (coasts less), lower glides further.</summary>
    public double InertiaFriction
    {
        get => _inertia.Friction;
        set => _inertia.Friction = value;
    }

    /// <summary>Wheel ease-to-target rate per second - higher is snappier, lower glides longer to the target.</summary>
    public double InertiaSmoothRate
    {
        get => _inertia.SmoothRate;
        set => _inertia.SmoothRate = value;
    }

    /// <summary>Speed (px/s) below which a flick is treated as stopped.</summary>
    public double MinFlickSpeed
    {
        get => _inertia.MinFlickSpeed;
        set => _inertia.MinFlickSpeed = value;
    }

    /// <summary>Release speed (px/s) needed to start a flick.</summary>
    public double FlickStartSpeed
    {
        get => _inertia.FlickStartSpeed;
        set => _inertia.FlickStartSpeed = value;
    }

    /// <summary>Enables inertial scrolling (smooth wheel + flick momentum). On by default; a ScrollViewer/theme can
    /// turn it off to get instant scrolling back.</summary>
    public bool IsInertiaEnabled { get; set; } = true;

    // When CanContentScroll is on and the hosted content contains an IScrollableContent (a virtualizing panel), this
    // presenter delegates the scroll mechanism to it instead of physically translating: the panel realizes only the
    // visible window. WPF's CanContentScroll, on the clean IScrollableContent seam.
    private IScrollableContent _inner;

    /// <summary>Delegate scrolling to an inner virtualizing panel (item scrolling) instead of pixel-translating the content.</summary>
    public bool CanContentScroll { get; set; }

    private bool Delegating => CanContentScroll && _inner != null;

    private Size<double> _measuredSlot;
    private Size<double> _answered;
    private bool _measuring;

    public ScrollContentPresenter()
    {
        _inertia = new PanInertia(() => Offset, SetOffsetCore);

        // The whole point of the presenter: the overflowing content is scissored to this viewport (see the renderer's
        // per-unit clip, which intersects every ClipToBounds ancestor).
        ClipToBounds = true;
        // (Focusable=false comes from ContentPresenter's metadata override - a passive presenter is never a focus target.)

        // A finger is watched whatever took its press: one that began on a button still scrolls.
        AddHandler(PointerEvents.PointerPressedEvent, new PointerPressedEventHandler(OnFingerPressed), handledEventsToo: true);
        AddHandler(PointerEvents.PointerMovedEvent, new PointerEventHandler(OnFingerMoved), handledEventsToo: true);
        AddHandler(PointerEvents.PointerReleasedEvent, new PointerReleasedEventHandler(OnFingerReleased), handledEventsToo: true);
        AddHandler(PointerEvents.PointerCanceledEvent, new PointerEventHandler(OnFingerGone), handledEventsToo: true);
    }

    /// <summary>This clip is a VIEWPORT, so it binds adorners too: a focus ring on a row scrolled half out of the list
    /// must be cut by the list, the way the row itself is. See <see cref="IUIComponent.ClipsAdorners"/> for why that is
    /// the exception rather than the rule.</summary>
    public override bool ClipsAdorners => true;

    public Size<double> Extent => Delegating ? _inner.Extent : _extent;

    public Size<double> Viewport => _viewport;

    public Vector2<double> Offset => Delegating ? _inner.Offset : _offset;

    // The offset our LAST arrange actually translated the content to (see IScrollableContent.RealizedOffset). When we
    // delegate to a virtualizing panel, that's the offset ITS window was realized for; a physical (non-delegating) content
    // arranges to _offset directly, so it's just _offset.
    public Vector2<double> RealizedOffset => Delegating ? _inner.RealizedOffset : _offset;

    public bool CanScrollHorizontally { get; set; } = true;

    public bool CanScrollVertically { get; set; } = true;

    /// <summary>Which axes a content drag pans (set by the <see cref="ScrollViewer"/>). None = drag does nothing.</summary>
    public PanningMode PanningMode { get; set; } = PanningMode.None;

    /// <summary>Whether a finger scrolls the content - see <see cref="ScrollViewer.IsTouchScrollEnabled"/>.</summary>
    public bool IsTouchScrollEnabled { get; set; } = true;

    public event EventHandler ScrollMetricsChanged;

    public void SetOffset(Vector2<double> offset)
    {
        _inertia.Stop();
        SetOffsetCore(offset);
    }

    // The actual offset set, WITHOUT canceling inertia - used by the inertia ticker itself.
    private void SetOffsetCore(Vector2<double> offset)
    {
        if (Delegating)
        {
            _inner.SetOffset(offset);   // the panel clamps, re-windows (realizes/rebinds entering rows), raises metrics
            InvalidateArrange();        // ...and WE re-translate the panel by the new -offset (transform-only scroll)
            return;
        }

        var clamped = ClampOffset(offset, _extent, _viewport);
        if (clamped == _offset) return;
        _offset = clamped;
        InvalidateArrange();   // reposition the child; the metrics event is raised from the arrange
    }

    /// <summary>Smoothly scrolls by <paramref name="delta"/> (the wheel path): eases toward an accumulating target, so
    /// rapid notches add up instead of jumping. Instant set when inertia is off.</summary>
    public void AnimateScrollBy(Vector2<double> delta)
    {
        if (!IsInertiaEnabled)
        {
            SetOffset(Offset + delta);
            return;
        }
        _inertia.GlideTo(ClampOffset(_inertia.Target + delta, Extent, Viewport));
    }

    // --- Pan: drag the content itself to scroll (the no-scrollbars / touch path) ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // A finger scrolls by its own rule (OnFingerPressed); PanningMode is the mouse's and a pen's.
        if (e.Handled || PanningMode == PanningMode.None || e.ChangedButton != MouseButtons.Left ||
            e.Pointer.Type == PointerType.Touch)
        {
            return;
        }

        _inertia.Stop();
        _inertia.ResetTracking();
        _isPanning = false;
        _panStartPoint = e.GetPosition(this);
        _panStartOffset = Offset;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!ReferenceEquals(e.Pointer.Captured, this) || ReferenceEquals(e.Pointer, _finger))
        {
            return;
        }

        var raw = e.GetPosition(this) - _panStartPoint;
        if (!_isPanning)
        {
            // Hold off until the drag clears the threshold, so a plain click isn't consumed as a (zero) pan.
            if (Math.Abs(raw.X) < PanThreshold && Math.Abs(raw.Y) < PanThreshold) return;
            _isPanning = true;
        }

        // Restrict to the allowed axis (the other stays put). Dragging the content one way reveals what's behind it,
        // i.e. the offset moves opposite the pointer.
        var delta = PanningMode switch
        {
            PanningMode.HorizontalOnly => new Vector2<double>(raw.X, 0),
            PanningMode.VerticalOnly => new Vector2<double>(0, raw.Y),
            _ => raw
        };
        SetOffsetCore(_panStartOffset - delta);   // pan drives the offset directly (Down already stopped inertia)
        _inertia.Track(e.Timestamp);
        e.Handled = true;
    }

    // --- A finger: scrolls along the axes that have somewhere to go, taking the finger from what it began on ---

    private void OnFingerPressed(object sender, PointerPressedEventArgs e)
    {
        // A second finger leaves the first one's scroll alone. A first finger whose lift went elsewhere - an outer scroll
        // area took it, or it lifted outside - is gone from the surface, and the new one replaces it.
        if (!IsTouchScrollEnabled || e.Pointer.Type != PointerType.Touch ||
            _finger != null && PointerDevice.Current.Pointers.Contains(_finger))
        {
            return;
        }

        _inertia.Stop();
        _inertia.ResetTracking();
        _finger = e.Pointer;
        _fingerStart = e.GetPosition(this);
        _fingerStartOffset = Offset;
        _fingerScrolling = false;
    }

    private void OnFingerMoved(object sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _finger))
        {
            return;
        }

        if (e.Pointer.IsInGesture)
        {
            _fingerStart = e.GetPosition(this);
            _fingerStartOffset = Offset;
            _inertia.ResetTracking();
            return;
        }

        if (!_fingerScrolling)
        {
            // What takes the finger's moves itself - a slider's thumb, a text selection, a carried row, a scroll area
            // inside this one that took it first - keeps the gesture; so does what a held finger holds.
            if (e.Handled || e.Pointer.IsHolding ||
                e.Pointer.Captured is ScrollContentPresenter inner && !ReferenceEquals(inner, this))
            {
                _finger = null;
                return;
            }

            if (!PlatformSettings.ExceedsDragThreshold(Scrollable(e.GetPosition(this) - _fingerStart), this))
            {
                return;
            }

            // Taken from what the finger began on: a button lets go of the press and is not clicked.
            _fingerScrolling = true;
            e.Pointer.Capture(this);
        }

        SetOffsetCore(_fingerStartOffset - Scrollable(e.GetPosition(this) - _fingerStart));
        _inertia.Track(e.Timestamp);
        e.Handled = true;
    }

    private void OnFingerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _finger))
        {
            return;
        }

        var scrolled = _fingerScrolling;
        _finger = null;
        _fingerScrolling = false;
        if (!scrolled)
        {
            return;
        }

        e.Handled = true;   // a scroll, not a tap
        if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
        if (IsInertiaEnabled)
        {
            _inertia.Release(e.Timestamp);
        }
    }

    private void OnFingerGone(object sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _finger)) return;

        _finger = null;
        _fingerScrolling = false;
    }

    /// <summary>Whether <paramref name="pointer"/> may carry <paramref name="element"/> - a dragged row, a tab - now. The
    /// mouse and a pen always may. A finger may once it holds, or where no scroll area around the element could scroll
    /// under it: until then, the finger's move is a scroll.</summary>
    internal static bool LetsFingerCarry(Pointer pointer, IUIComponent element)
    {
        if (pointer.Type != PointerType.Touch || pointer.IsHolding)
        {
            return true;
        }

        for (var at = element?.VisualParent; at != null; at = at.VisualParent)
        {
            if (at is ScrollContentPresenter { IsTouchScrollEnabled: true } presenter &&
                presenter.Scrollable(Vector2<double>.One) != Vector2<double>.Zero)
            {
                return false;
            }
        }

        return true;
    }

    // A drag along the axes this content can actually move on; the others stay put.
    private Vector2<double> Scrollable(Vector2<double> drag)
    {
        var extent = Extent;
        return new Vector2<double>(
            CanScrollHorizontally && extent.Width > _viewport.Width ? drag.X : 0,
            CanScrollVertically && extent.Height > _viewport.Height ? drag.Y : 0);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.ChangedButton != MouseButtons.Left || ReferenceEquals(e.Pointer, _finger))
        {
            return;
        }

        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
        }

        if (_isPanning)
        {
            e.Handled = true;   // it was a pan, not a click
            if (IsInertiaEnabled)
            {
                _inertia.Release(e.Timestamp);
            }
        }
        _isPanning = false;
    }

    protected override Size<double> MeasureOverride(Size<double> availableSize)
    {
        // The content stands at its own top, wherever it is scrolled to.
        (VisualChildren.FirstOrDefault() as Base.MeasurableUIComponent)?.PlaceAt(0, double.NaN);
        if (CanContentScroll)
        {
            // Measure the content at the VIEWPORT (not unbounded) so an inner virtualizing panel realizes only what's
            // visible; then delegate the scroll surface to it. base (ContentPresenter) builds + measures the content.
            _measuring = true;
            try
            {
                base.MeasureOverride(availableSize);
            }
            finally
            {
                _measuring = false;
            }

            ResolveInner();
            if (_inner != null)
            {
                // An unbounded axis is the parent ASKING for our natural size (a Grid star row probes its child that way
                // before it resolves the row), not telling us the whole content is on screen. Taking it for a viewport
                // wipes the scroll range and zeroes the offset every pass - the list then refuses to scroll. The viewport
                // is what ARRANGE hands us; measure only answers with a desired size.
                if (!double.IsInfinity(availableSize.Width)) _viewport = new Size<double>(availableSize.Width, _viewport.Height);
                if (!double.IsInfinity(availableSize.Height)) _viewport = new Size<double>(_viewport.Width, availableSize.Height);
                RaiseMetricsChanged();
                // The content capped by the slot, as on the physical path below: a list shorter than its popup's
                // MaxHeight shrinks to its rows instead of padding out to the cap.
                _measuredSlot = availableSize;
                _answered = ContentInSlot(availableSize);
                return _answered;
            }
        }

        // Physical path: measure the content unbounded on every axis the viewer allows scrolling, so it reports its
        // natural extent; constrained on a disabled axis so it fits.
        var constraint = new Size<double>(
            CanScrollHorizontally ? double.PositiveInfinity : availableSize.Width,
            CanScrollVertically ? double.PositiveInfinity : availableSize.Height);

        var extent = base.MeasureOverride(constraint);
        if (extent != _extent)
        {
            _extent = extent;
            RaiseMetricsChanged();
        }

        // Desired size = the content, capped by the viewport (an unbounded axis reports the full extent). NOT the whole
        // viewport: a viewer measured content-to-fit but capped by MaxHeight (e.g. a DropDown popup) must shrink to its
        // content when it's shorter than the cap, not pad out to the cap. A viewer that should FILL a fixed slot still
        // does - its Stretch alignment fills at ARRANGE regardless of this (smaller) desired size.
        return new Size<double>(
            double.IsInfinity(availableSize.Width) ? extent.Width : Math.Min(extent.Width, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? extent.Height : Math.Min(extent.Height, availableSize.Height));
    }

    protected override Size<double> ArrangeOverride(Size<double> finalSize)
    {
        _viewport = finalSize;

        if (Delegating)
        {
            // The virtualizing panel arranges at absolute slots: keep it viewport-sized and slide it by the offset, so a
            // scroll is one translation here.
            var offset = _inner.Offset;
            _lastTranslatedInnerOffset = offset;   // so OnInnerMetricsChanged only re-invalidates on a REAL offset change
            if (VisualChildren.FirstOrDefault() is IMeasurableComponent inner)
            {
                // The element WE translate (the ItemsPresenter hosting the virtualizing panel) is the render MOTION NODE:
                // its subtree's batched instances bake in ITS space + reference its transform-table slot, so this
                // per-scroll-frame arrange marks ONE node (a 64-byte matrix rewrite + replay) instead of the global
                // transform flag (which re-baked every instance in the window - the scroll full-walk).
                if (inner is Base.UIComponent innerUi) innerUi.IsRenderMotionNode = true;
                inner.Arrange(new Rect(-offset.X, -offset.Y, finalSize.Width, finalSize.Height));
            }
            RaiseMetricsChanged();
            return finalSize;
        }

        _offset = ClampOffset(_offset, _extent, finalSize);

        if (VisualChildren.FirstOrDefault() is IMeasurableComponent content)
        {
            // Arrange the child at its full extent (at least the viewport), shifted up/left by the offset; the
            // ClipToBounds scissor trims the overflow to the viewport.
            var width = Math.Max(_extent.Width, finalSize.Width);
            var height = Math.Max(_extent.Height, finalSize.Height);
            content.Arrange(new Rect(-_offset.X, -_offset.Y, width, height));
        }

        RaiseMetricsChanged();
        return finalSize;
    }

    // Find the inner virtualizing panel (first IScrollableContent in the content subtree) and track its metrics.
    private void ResolveInner()
    {
        var found = FindInner(this);
        if (ReferenceEquals(found, _inner)) return;
        if (_inner != null) _inner.ScrollMetricsChanged -= OnInnerMetricsChanged;
        _inner = found;
        if (_inner != null) _inner.ScrollMetricsChanged += OnInnerMetricsChanged;
    }

    private static IScrollableContent FindInner(IUIComponent root) => ScrollableContent.FindIn(root);

    private Vector2<double> _lastTranslatedInnerOffset;

    private void OnInnerMetricsChanged(object sender, EventArgs e)
    {
        // Re-translate after the panel re-clamps its offset, but only on a real change: this also fires from the panel's
        // own arrange and would loop.
        if (Delegating && _inner.Offset != _lastTranslatedInnerOffset) InvalidateArrange();
        if (Delegating && !_measuring && ContentInSlot(_measuredSlot) != _answered) InvalidateMeasure();
        RaiseMetricsChanged();
    }

    private Size<double> ContentInSlot(Size<double> slot) => new(
        double.IsInfinity(slot.Width) ? _inner.Extent.Width : Math.Min(_inner.Extent.Width, slot.Width),
        double.IsInfinity(slot.Height) ? _inner.Extent.Height : Math.Min(_inner.Extent.Height, slot.Height));

    private void RaiseMetricsChanged() => ScrollMetricsChanged?.Invoke(this, EventArgs.Empty);

    private static Vector2<double> ClampOffset(Vector2<double> offset, Size<double> extent, Size<double> viewport)
    {
        var maxX = Math.Max(0, extent.Width - viewport.Width);
        var maxY = Math.Max(0, extent.Height - viewport.Height);
        return new Vector2<double>(Math.Clamp(offset.X, 0, maxX), Math.Clamp(offset.Y, 0, maxY));
    }
}
