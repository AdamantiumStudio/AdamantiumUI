using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Text;

/// <summary>The <c>PART_TextPresenter</c> of a <see cref="TextBoxBase"/>: a thin, clipped, non-focusable surface that
/// defers measure, render and hit-testing to its <see cref="Owner"/>.</summary>
public sealed class TextPresenter : InputUIComponent
{
    internal TextBoxBase Owner { get; set; }

    private bool _selecting;
    private Size _lastArrangeSize;

    public TextPresenter()
    {
        // NOTE: clipping is done by the hosting ScrollContentPresenter (it scissors the overflow to the viewport) - we
        // deliberately do NOT set ClipToBounds here.
        Focusable = false;     // focus belongs to the owning TextBox
    }

    // The scroll surface: report the FULL content size (all lines, full width) so the enclosing ScrollViewer's
    // ScrollContentPresenter knows the extent and provides the viewport/scrollbars. It measures us unbounded on the axes
    // it lets scroll (so we report our natural extent) and constrained on a disabled axis (so wrapping fits the viewport);
    // MeasureSurface already interprets an infinite length along the lines as "no wrap" and a finite one as the wrap length.
    protected override Size MeasureOverride(Size availableSize)
        => Owner == null ? new Size() : Owner.MeasureSurface(availableSize);

    // The owner's lines stand on a baseline grid from where this surface is: moved, it lays them out again.
    protected internal override bool MeasuresByPlace => BaselineGridStep > 0 && !double.IsInfinity(BaselineGridStep);

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Re-render only when the SLOT SIZE changes (content/box resized, e.g. the floating-strip toggle), not on a mere
        // reposition (scrolling moves us via the arrange rect's ORIGIN - the same rendered units just shift, no redraw).
        if (finalSize != _lastArrangeSize) { _lastArrangeSize = finalSize; InvalidateRender(false); }
        Owner?.ArrangeSurface(finalSize.Width);
        return finalSize;
    }

    protected override void OnRender(IDrawingContext context)
    {
        if (Owner == null) return;
        Owner.RenderSurface(context.ForControl(this), RenderSize);
    }

    // Non-input geometry still needs to be hit for the click-to-place-caret / drag-select gestures.
    public override bool HitTestCore(Vector2 localPoint) => true;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Owner == null || e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        var p = e.GetPosition(this);
        if (e.ClickCount >= 3)
        {
            Owner.SurfaceSelectAll();
            e.Handled = true;
            return;
        }
        if (e.ClickCount == 2)
        {
            Owner.SurfaceSelectWord(p.X, p.Y);
            e.Handled = true;
            return;
        }

        var shift = (Keyboard.Modifiers & (InputModifiers.LeftShift | InputModifiers.RightShift)) != 0;
        Owner.SurfacePointerDown(p.X, p.Y, shift);
        _selecting = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>Abandon a selection drag in progress, for an owner that has decided the press means something else (see
    /// <see cref="NumericUpDown"/>'s scrub). Without it the drag stays armed with no way to end - the button-up goes to
    /// whoever took the capture - and the next hover would carry on selecting.</summary>
    internal void CancelSelection()
    {
        _selecting = false;
        ReleasePointerCaptures();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_selecting)
        {
            var p = e.GetPosition(this);
            Owner?.SurfacePointerMove(p.X, p.Y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_selecting || e.ChangedButton != MouseButtons.Left)
        {
            return;
        }

        _selecting = false;
        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
        }
    }
}
