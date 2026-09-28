using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>The screen-space layer that places an <see cref="InfiniteCanvas"/>'s <see cref="CanvasPane"/>s by their
/// <see cref="CanvasPane.Placement"/>; <see cref="Inset"/> says what docked panes take away.</summary>
public class CanvasChromeLayer : Panel
{
    // The panes declared in the canvas's template; their template order is their drawing order.
    private readonly List<CanvasPane> _panes = new();

    /// <summary>The layer itself is not a hit target, so presses between panes reach the plane; its panes are still tested,
    /// which <c>IsHitTestVisible</c> would prevent.</summary>
    public override bool HitTestCore(Vector2 localPoint) => false;

    public static readonly AdamantiumProperty PaneInsetProperty = AdamantiumProperty.Register(nameof(PaneInset),
        typeof(Thickness), typeof(CanvasChromeLayer),
        new PropertyMetadata(new Thickness(10), PropertyMetadataOptions.AffectsArrange));

    public static readonly AdamantiumProperty PaneGapProperty = AdamantiumProperty.Register(nameof(PaneGap),
        typeof(Double), typeof(CanvasChromeLayer),
        new PropertyMetadata(8.0, PropertyMetadataOptions.AffectsArrange));

    /// <summary>How far a pane sits from the edge it is placed against.</summary>
    public Thickness PaneInset
    {
        get => GetValue<Thickness>(PaneInsetProperty);
        set => SetValue(PaneInsetProperty, value);
    }

    /// <summary>The space between two panes sharing one placement.</summary>
    public Double PaneGap
    {
        get => GetValue<Double>(PaneGapProperty);
        set => SetValue(PaneGapProperty, value);
    }

    private InfiniteCanvas _owner;

    /// <summary>The canvas whose viewport and selection this layer places against. Setting it tells the panes, which
    /// may well have arrived before it did.</summary>
    public InfiniteCanvas Owner
    {
        get => _owner;
        set
        {
            if (ReferenceEquals(_owner, value)) return;

            _owner = value;
            Gather();
        }
    }

    /// <summary>Puts the layer in step with the panes it should be showing: the canvas's own, and the one it may be
    /// holding up right now. Cheap to call - it returns having done nothing when nothing has changed.</summary>
    public void Sync()
    {
        Gather();
        InvalidateMeasure();
    }

    // WHOSE THEY ARE, told to each pane that has not been told yet.
    private void Gather()
    {
        var changed = false;

        foreach (var child in Children)
        {
            if (child is not CanvasPane pane) continue;

            if (!ReferenceEquals(pane.Layer, this))
            {
                pane.Layer = this;
                changed = true;
            }

            if (!ReferenceEquals(pane.Canvas, Owner))
            {
                pane.Canvas = Owner;
                changed = true;
            }

            if (!_panes.Contains(pane))
            {
                _panes.Add(pane);
                changed = true;
            }
        }

        // ...and let go of any that left, so a template swap does not leave the layer placing panes that are gone.
        for (var i = _panes.Count - 1; i >= 0; i--)
        {
            if (Children.Contains(_panes[i])) continue;

            _panes[i].Layer = null;
            _panes[i].Canvas = null;
            _panes.RemoveAt(i);
            changed = true;
        }

        if (changed) SyncSelection();
    }

    /// <summary>The panes this layer places, in the order the template wrote them.</summary>
    public IReadOnlyList<CanvasPane> Panes => _panes;

    /// <summary>Lets go of every pane - what a canvas does when its template is taken away.</summary>
    public void Release()
    {
        foreach (var pane in _panes)
        {
            pane.Layer = null;
            pane.Canvas = null;
        }

        _panes.Clear();
    }

    /// <summary>Show or hide the panes that FOLLOW the selection. Called when the selection changes, and deliberately
    /// not from the arrange pass: visibility is a layout input, and writing one while laying out is how a pass ends up
    /// invalidating itself.</summary>
    public void SyncSelection()
    {
        var anything = Owner is { } canvas && canvas.SelectionBounds is not null;

        foreach (var pane in _panes)
        {
            if (pane.Placement != CanvasPanePlacement.Selection) continue;

            // A CURRENT value: a pane may have its own reason bound here - the node list follows whether it was asked
            // for - and the Local slot the plain setter writes would mask that binding for good.
            pane.SetCurrentValue(CanvasPane.IsNeededProperty, anything);
        }
    }

    /// <summary>What the DOCKED panes take out of the viewport, as a margin. The canvas asks so that "fit to view" and
    /// "home" center on the part of the plane a person can actually see.</summary>
    public Thickness Inset()
    {
        double left = 0, top = 0, right = 0, bottom = 0;

        foreach (var pane in _panes)
        {
            var taken = pane.Reserved();

            // The LARGEST claim on a side, not the sum: two panes docked to the same edge sit one above the other, so
            // together they take the width of the wider, not of both.
            if (taken.Left > left) left = taken.Left;
            if (taken.Top > top) top = taken.Top;
            if (taken.Right > right) right = taken.Right;
            if (taken.Bottom > bottom) bottom = taken.Bottom;
        }

        return new Thickness(left, top, right, bottom);
    }

    public CanvasChromeLayer()
    {
        // Listen for panes, which are built after the layer is found, and gather them here rather than while laying out.
        Children.CollectionChanged += (_, _) => Gather();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var height = Double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;

        foreach (var child in Children) child.Measure(new Size(width, height));

        // Nothing of its own: the layer is a place, not a thing with a size. Asking for room would push the canvas's
        // own measure around, and the canvas is the one that decides how big the plane's window is.
        return new Size();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {

        var inset = PaneInset;
        var gap = PaneGap;

        // Each slot runs its own stack, so two panes placed against the same edge sit one after the other instead of
        // on top of each other.
        double topLeft = inset.Top, topRight = inset.Top;
        double bottomLeft = finalSize.Height - inset.Bottom, bottomRight = finalSize.Height - inset.Bottom;
        double topCenter = inset.Left, bottomCenter = inset.Left;
        double left = 0, right = 0;

        var leftTotal = Total(CanvasPanePlacement.Left, gap, true);
        var rightTotal = Total(CanvasPanePlacement.Right, gap, true);
        left = Math.Max(inset.Top, (finalSize.Height - leftTotal) / 2);
        right = Math.Max(inset.Top, (finalSize.Height - rightTotal) / 2);

        foreach (var pane in _panes)
        {
            if (pane.Visibility == Visibility.Collapsed) continue;

            var size = pane.DesiredSize;
            var x = 0.0;
            var y = 0.0;

            switch (pane.Placement)
            {
                case CanvasPanePlacement.TopLeft:
                    x = inset.Left;
                    y = topLeft;
                    topLeft += size.Height + gap;
                    break;

                case CanvasPanePlacement.Left:
                    x = inset.Left;
                    y = left;
                    left += size.Height + gap;
                    break;

                case CanvasPanePlacement.BottomLeft:
                    x = inset.Left;
                    y = bottomLeft - size.Height;
                    bottomLeft -= size.Height + gap;
                    break;

                case CanvasPanePlacement.TopRight:
                    x = finalSize.Width - inset.Right - size.Width;
                    y = topRight;
                    topRight += size.Height + gap;
                    break;

                case CanvasPanePlacement.Right:
                    x = finalSize.Width - inset.Right - size.Width;
                    y = right;
                    right += size.Height + gap;
                    break;

                case CanvasPanePlacement.BottomRight:
                    x = finalSize.Width - inset.Right - size.Width;
                    y = bottomRight - size.Height;
                    bottomRight -= size.Height + gap;
                    break;

                case CanvasPanePlacement.TopCenter:
                    x = Centered(CanvasPanePlacement.TopCenter, finalSize.Width, gap) + topCenter - inset.Left;
                    y = inset.Top;
                    topCenter += size.Width + gap;
                    break;

                case CanvasPanePlacement.BottomCenter:
                    x = Centered(CanvasPanePlacement.BottomCenter, finalSize.Width, gap) + bottomCenter - inset.Left;
                    y = finalSize.Height - inset.Bottom - size.Height;
                    bottomCenter += size.Width + gap;
                    break;

                case CanvasPanePlacement.Selection:
                    var over = AboveSelection(size, finalSize, inset);
                    x = over.X;
                    y = over.Y;
                    break;

                default:
                    // Asked to open at a POINT - here is where both the room and the pane's own size are known, so
                    // here is where that point becomes the fraction everything else is kept in.
                    if (pane.TakePending(out var asked))
                        pane.Anchor = CanvasPane.AnchorFor(asked, finalSize, size);

                    var put = CanvasPane.PlaceOf(pane.Anchor, finalSize, size);
                    x = put.X;
                    y = put.Y;
                    break;
            }

            // Never off the edge, whatever the arithmetic said: a pane that cannot be reached is a pane that is gone,
            // and there is no edge on the plane itself to find it by.
            x = Math.Clamp(x, 0, Math.Max(0, finalSize.Width - size.Width));
            y = Math.Clamp(y, 0, Math.Max(0, finalSize.Height - size.Height));

            pane.Arrange(new Rect(x, y, size.Width, size.Height));
        }

        return finalSize;
    }

    // Above the selection frame if it fits there, below it if it does not - which is what a context bar has to do, or
    // it covers the very thing it is about as soon as the selection is near the top of the viewport.
    private Vector2 AboveSelection(Size size, Size room, Thickness inset)
    {
        if (Owner is not { } canvas || canvas.SelectionBounds is not { } world)
        {
            return new Vector2(inset.Left, inset.Top);
        }

        // Against the FRAME and not the box it is round: the frame stands off the selection by half a grip, and a bar
        // measured from the box would sit that much into the grips along the top of it.
        var frame = canvas.FrameOf(world);

        var x = frame.X + frame.Width / 2 - size.Width / 2;
        var y = frame.Y - size.Height - PaneGap;

        if (y < inset.Top) y = frame.Y + frame.Height + PaneGap;

        return new Vector2(x, y);
    }

    private double Total(CanvasPanePlacement placement, double gap, bool vertical)
    {
        var total = 0.0;
        var count = 0;

        foreach (var pane in _panes)
        {
            if (pane.Placement != placement || pane.Visibility == Visibility.Collapsed) continue;

            total += vertical ? pane.DesiredSize.Height : pane.DesiredSize.Width;
            count++;
        }

        return count > 1 ? total + gap * (count - 1) : total;
    }

    private double Centered(CanvasPanePlacement placement, double room, double gap) =>
        Math.Max(0, (room - Total(placement, gap, false)) / 2);

}
