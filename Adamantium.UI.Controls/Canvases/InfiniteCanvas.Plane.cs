using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media.Animation;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Canvases;

// THE PLANE'S EDGES: a plane with bounds keeps the camera on it - what a document's pages need - and one without them
// is the endless plane it always was.
public partial class InfiniteCanvas
{
    /// <summary>The plane's edges, in world units. Empty - the default - leaves it endless. With edges the camera stays
    /// on it: wider than the viewport, it is scrolled no further than its edges; narrower, it stands in the middle, and
    /// shorter, at the top, and does not move that way at all. A finger or a dragging mouse pulls it a little past an
    /// edge, against a growing pull, and it springs back when let go.</summary>
    public static readonly AdamantiumProperty PlaneBoundsProperty = AdamantiumProperty.Register(nameof(PlaneBounds),
        typeof(Rect), typeof(InfiniteCanvas),
        new PropertyMetadata(Rect.Empty, PropertyMetadataOptions.AffectsRender, OnPlaneBoundsChanged));

    /// <summary>What the mouse wheel does: zooms (the default), scrolls as through a document, or nothing.</summary>
    public static readonly AdamantiumProperty WheelModeProperty = AdamantiumProperty.Register(nameof(WheelMode),
        typeof(CanvasWheelMode), typeof(InfiniteCanvas), new PropertyMetadata(CanvasWheelMode.Zoom));

    // How far one notch of the wheel scrolls, in screen pixels - three lines, as a scroll viewer's.
    private const double WheelScrollPixels = 48;

    // How much of a flick's step a plane already past its edge still takes: the band holds it back hard.
    private const double PastEdgeStep = 0.25;

    // How the band pulls: the further past the edge, the less a move of the hand moves the plane - never past a
    // viewport's length however far it is pulled.
    private const double BandStiffness = 0.55;

    // The wheel's scroll, gliding to where the notches add up to - written like any other move, so kept on the plane.
    private readonly PanInertia _glide;

    // The way back from past an edge: it glides how far the plane is past its edge down to nothing, measured against
    // the edge as it is at each step - so a zoom or a resize meanwhile does not leave it aimed at an edge long gone.
    private readonly PanInertia _springBack;

    // The viewport the camera is kept against. Set by the arrange pass, which is the one that knows it first.
    private Size<double> _viewport;

    // Where the hand has put the plane, edges ignored: what a hand past the edge is pulling against.
    private Vector2<double> _handOffset;

    // Whether the offset being written is one the hand stretched past the edge, or one springing back to it.
    private bool _stretching;

    // Whether a hand - a finger or a dragging mouse - is taking the plane, stretch and all: the camera is not pulled
    // onto the plane meanwhile.
    private bool _takingByHand;

    // Whether the coast running is a hand's flick, which meets the edge with the band rather than stopping dead.
    private bool _handCoast;

    public Rect PlaneBounds
    {
        get => GetValue<Rect>(PlaneBoundsProperty);
        set => SetValue(PlaneBoundsProperty, value);
    }

    public CanvasWheelMode WheelMode
    {
        get => GetValue<CanvasWheelMode>(WheelModeProperty);
        set => SetValue(WheelModeProperty, value);
    }

    /// <summary>Whether the plane has edges (<see cref="PlaneBounds"/> with a size).</summary>
    public bool HasPlaneBounds => PlaneBounds is { Width: > 0, Height: > 0 };

    /// <summary>The offset nearest <paramref name="offset"/> that keeps the camera on the plane at
    /// <paramref name="scale"/>: unchanged for an endless plane, and before the canvas has a viewport.</summary>
    public Vector2<double> ClampToPlane(Vector2<double> offset, Double scale)
    {
        var bounds = PlaneBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || scale <= 0) return offset;

        var room = Room(scale, out _, out _);
        if (room.Width <= 0 || room.Height <= 0) return offset;

        var width = bounds.Width * scale;
        var height = bounds.Height * scale;
        var x = width <= room.Width
            ? room.X + (room.Width - width) / 2 - bounds.X * scale
            : Math.Clamp(offset.X, room.Right - (bounds.X + bounds.Width) * scale, room.X - bounds.X * scale);
        var y = height <= room.Height
            ? room.Y - bounds.Y * scale
            : Math.Clamp(offset.Y, room.Bottom - (bounds.Y + bounds.Height) * scale, room.Y - bounds.Y * scale);

        return new Vector2<double>(x, y);
    }

    // The viewport the arrange pass gave, or the rendered one before it.
    private Size<double> Viewport => _viewport is { Width: > 0, Height: > 0 } ? _viewport : RenderSize;

    // What the plane is seen through at `scale`: the viewport less what docked panes take and, along an axis the plane
    // overflows, less the scroll bar across it - so its last strip can be scrolled out from under the bar. Empty before
    // there is a viewport.
    private Rect Room(Double scale, out bool across, out bool down)
    {
        across = false;
        down = false;
        var size = Viewport;
        if (size.Width <= 0 || size.Height <= 0) return Rect.Empty;

        var taken = _chromeLayer?.Inset() ?? new Thickness(0);
        var room = new Rect(taken.Left, taken.Top, Math.Max(1, size.Width - taken.Left - taken.Right),
            Math.Max(1, size.Height - taken.Top - taken.Bottom));
        var bounds = PlaneBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return room;

        across = _horizontalBar != null && bounds.Width * scale > room.Width + 0.5;
        down = _verticalBar != null && bounds.Height * scale > room.Height + 0.5;
        if (across) room.Height = Math.Max(1, room.Height - Thick(_horizontalBar));
        if (down) room.Width = Math.Max(1, room.Width - Thick(_verticalBar));

        return room;
    }

    private static Object CoerceOffset(AdamantiumComponent component, Object value)
    {
        if (component is not InfiniteCanvas canvas || value is not Vector2<double> offset || canvas._stretching)
        {
            return value;
        }

        return canvas.ClampToPlane(offset, canvas.Scale);
    }

    // Puts the camera back on the plane where it stands, after what it is kept against changed - the viewport, the
    // panels, the scale, the edges. A written move, so the shown offset and the one kept are the same.
    private void Reclamp()
    {
        if (!HasPlaneBounds || _stretching || _takingByHand) return;

        var held = ClampToPlane(Offset, Scale);
        if (held != Offset) SetCurrentValue(OffsetProperty, held);
    }

    private static void OnPlaneBoundsChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not InfiniteCanvas canvas) return;

        canvas.StopCoasting();
        canvas.Reclamp();
        canvas._chromeLayer?.InvalidateArrange();
        canvas.SyncScrollBars();
    }

    /// <summary>The panels were placed again: what docked ones take may have changed, and the camera and the scroll
    /// bars are kept against the room they leave.</summary>
    internal void OnChromeArranged()
    {
        var inset = _chromeLayer?.Inset() ?? new Thickness(0);
        if (Same(inset, _clampedInset)) return;

        _clampedInset = inset;
        Reclamp();
        SyncScrollBars();
    }

    // Writes an offset as it is, past the edge - a stretch, or a step back from one.
    private void StretchTo(Vector2<double> offset)
    {
        _stretching = true;

        try
        {
            SetCurrentValue(OffsetProperty, offset);
        }
        finally
        {
            _stretching = false;
        }
    }

    // How far the plane stands past its edge now.
    private Vector2<double> Overshoot() => Offset - ClampToPlane(Offset, Scale);

    // The plane `over` past its edge as the edge is now.
    private void Overshoot(Vector2<double> over) => StretchTo(ClampToPlane(Offset, Scale) + over);

    // The hand's offset as the band shows it: on the plane as it is, past an edge pulled back the harder the further -
    // along an axis the plane overflows; one it fits along stays where it stands.
    private Vector2<double> Stretched(Vector2<double> wanted)
    {
        var held = ClampToPlane(wanted, Scale);
        var room = Room(Scale, out _, out _);
        var bounds = PlaneBounds;
        var x = bounds.Width * Scale > room.Width ? Band(wanted.X - held.X, room.Width) : 0;
        var y = bounds.Height * Scale > room.Height ? Band(wanted.Y - held.Y, room.Height) : 0;

        return held + new Vector2<double>(x, y);
    }

    private static Double Band(Double past, Double span) =>
        past == 0 || span <= 0 ? 0 : Math.Sign(past) * (1 - 1 / (Math.Abs(past) * BandStiffness / span + 1)) * span;

    // What the band shows as `shown` past the edge, the hand pulled this far: its inverse, so a plane caught while it
    // springs back is taken where it is.
    private static Double Unband(Double shown, Double span)
    {
        if (shown == 0 || span <= 0) return 0;

        var left = 1 - Math.Min(Math.Abs(shown) / span, 0.999);
        return Math.Sign(shown) * (1 / left - 1) * span / BandStiffness;
    }

    // A hand took the plane: what it pulls is the plane where it stands, a stretch included - and the camera is caught
    // without being pulled onto the plane first, which would jump it under the hand.
    private void TakeByHand()
    {
        var held = ClampToPlane(Offset, Scale);
        var room = Room(Scale, out _, out _);
        _handOffset = held + new Vector2<double>(Unband(Offset.X - held.X, room.Width),
            Unband(Offset.Y - held.Y, room.Height));
        _handCoast = false;
        _takingByHand = true;

        try
        {
            CatchCamera();
        }
        finally
        {
            _takingByHand = false;
        }
    }

    // A hand moved the plane by a screen distance: on a plane with edges, past them it stretches.
    private void MoveByHand(Vector2<double> by)
    {
        if (!HasPlaneBounds)
        {
            Travel(_travel + by);
            return;
        }

        _handOffset += by;
        _travel += by;
        StretchTo(Stretched(_handOffset));
    }

    // A pinch scaled about `anchor`: on a plane with edges the hand's own offset is scaled with it, so a stretch held
    // past an edge stays held rather than snapping back at every change of a finger's spread.
    private void ScaleByHand(Vector2<double> anchor, Double factor)
    {
        if (!HasPlaneBounds)
        {
            SetScaleAt(anchor, Scale * factor);
            return;
        }

        var world = (anchor - _handOffset) * (1.0 / Scale);
        _holdingPoint = true;

        try
        {
            SetCurrentValue(ScaleProperty, Math.Clamp(Scale * factor, MinScale, MaxScale));
        }
        finally
        {
            _holdingPoint = false;
        }

        _handOffset = anchor - world * Scale;
        StretchTo(Stretched(_handOffset));
    }

    // The hand let go: a flick alone coasts on through the band; with a pinch's zoom coasting too it goes the ordinary
    // way, kept on the plane, since the two would each place the plane their own way; anything else springs back to
    // the edge.
    private void LetGoByHand(bool panCoasts, bool zoomCoasts)
    {
        _handCoast = panCoasts && !zoomCoasts && HasPlaneBounds;
        if (!_handCoast) SpringBack();
    }

    // A coast came to rest by itself: one a hand threw past an edge glides back to it.
    private void CoastSettled()
    {
        if (_handCoast)
        {
            _handCoast = false;
            SpringBack();
        }

        RememberLayout();
    }

    private void SpringBack()
    {
        if (HasPlaneBounds && Overshoot() != Vector2<double>.Zero) _springBack.GlideTo(Vector2<double>.Zero);
    }

    // THE SCROLL BARS: the camera shown another way. A bar's range is how far the plane reaches past what it is seen
    // through, in screen pixels, its thumb that room, its value how far the camera has come from the plane's start;
    // moving one moves the camera, and the camera moves it. Shown only along an axis there is somewhere to go.
    private ScrollBar _horizontalBar;
    private ScrollBar _verticalBar;
    private bool _syncingBars;

    private void TakeScrollBars()
    {
        _horizontalBar = GetTemplateChild("PART_HorizontalScrollBar") as ScrollBar;
        _verticalBar = GetTemplateChild("PART_VerticalScrollBar") as ScrollBar;
        if (_horizontalBar != null) _horizontalBar.Scroll += OnHorizontalScroll;
        if (_verticalBar != null) _verticalBar.Scroll += OnVerticalScroll;
        SyncScrollBars();
    }

    private void LeaveScrollBars()
    {
        if (_horizontalBar != null) _horizontalBar.Scroll -= OnHorizontalScroll;
        if (_verticalBar != null) _verticalBar.Scroll -= OnVerticalScroll;
        _horizontalBar = null;
        _verticalBar = null;
    }

    private void SyncScrollBars()
    {
        if (_horizontalBar == null && _verticalBar == null) return;

        var size = Viewport;
        var bounds = PlaneBounds;
        var scale = Scale;
        var room = Room(scale, out var across, out var down);

        _syncingBars = true;

        try
        {
            // Each bar runs along the room it scrolls, the horizontal one under it, the vertical one beside it.
            Show(_horizontalBar, across, bounds.Width * scale - room.Width, room.Width,
                room.X - (bounds.X * scale + Offset.X),
                new Thickness(room.X, 0, size.Width - room.Right, Math.Max(0, size.Height - room.Bottom - Thick(_horizontalBar))));
            Show(_verticalBar, down, bounds.Height * scale - room.Height, room.Height,
                room.Y - (bounds.Y * scale + Offset.Y),
                new Thickness(0, room.Y, Math.Max(0, size.Width - room.Right - Thick(_verticalBar)), size.Height - room.Bottom));
        }
        finally
        {
            _syncingBars = false;
        }
    }

    private static double Thick(ScrollBar bar) => bar?.BarThickness ?? 0;

    private static void Show(ScrollBar bar, bool shown, double range, double viewport, double value, Thickness margin)
    {
        if (bar == null) return;

        bar.SetCurrentValue(VisibilityProperty, shown ? Visibility.Visible : Visibility.Collapsed);
        if (!shown) return;

        bar.SetCurrentValue(RangeBase.MinimumProperty, 0.0);
        bar.SetCurrentValue(RangeBase.MaximumProperty, range);
        bar.SetCurrentValue(ScrollBar.ViewportSizeProperty, viewport);
        bar.SetCurrentValue(RangeBase.LargeChangeProperty, viewport * 0.9);
        bar.SetCurrentValue(RangeBase.SmallChangeProperty, WheelScrollPixels);
        bar.SetCurrentValue(RangeBase.ValueProperty, Math.Clamp(value, 0, range));
        bar.SetCurrentValue(MarginProperty, margin);
    }

    private void OnHorizontalScroll(object sender, ScrollEventArgs e)
    {
        if (_syncingBars) return;

        StopCoasting();
        var room = Room(Scale, out _, out _);
        SetCurrentValue(OffsetProperty, new Vector2<double>(room.X - PlaneBounds.X * Scale - e.NewValue, Offset.Y));
        RememberScrolled(e.ScrollEventType);
    }

    private void OnVerticalScroll(object sender, ScrollEventArgs e)
    {
        if (_syncingBars) return;

        StopCoasting();
        var room = Room(Scale, out _, out _);
        SetCurrentValue(OffsetProperty, new Vector2<double>(Offset.X, room.Y - PlaneBounds.Y * Scale - e.NewValue));
        RememberScrolled(e.ScrollEventType);
    }

    // What the layout keeps is where a scroll finished: a dragged thumb when it is let go, a step or a page at once.
    private void RememberScrolled(ScrollEventType kind)
    {
        if (kind != ScrollEventType.ThumbTrack) RememberLayout();
    }

    // The wheel in Scroll mode: a notch moves three lines down or up, across with Shift or a tilted wheel; the moves
    // add up, gliding, and stop at the edges.
    private void ScrollByWheel(int delta, bool across)
    {
        var step = delta / 120.0 * WheelScrollPixels;
        var by = across ? new Vector2<double>(step, 0) : new Vector2<double>(0, step);

        // The glide is left running: the next notch adds to where it is going.
        _zoom.Stop();
        _coast.Stop();
        _zoomCoast.Stop();
        _springBack.Stop();
        _glide.GlideTo(ClampToPlane(_glide.Target + by, Scale));
    }
}
