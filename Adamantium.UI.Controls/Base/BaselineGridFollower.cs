using System;
using Adamantium.Graphics.Fonts;
using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Base;

// A text on its baseline grid, from where its text stands: the phase of the grid at the top of the text, read as the text
// is measured, and followed once places settle - the text measured again where it moved, unless its own height moved it.
// The surface is the element the text is drawn in, the owner itself for a block; the text starts textTop below it, and
// remeasure lays it out again.
internal sealed class BaselineGridFollower(MeasurableUIComponent owner, Func<MeasurableUIComponent> surface,
    Func<double> textTop, Action remeasure)
{
    private double _phase = double.NaN;
    private IUIComponent _origin;
    private LayoutManager _manager;
    private double _top = double.NaN;
    private double _height = double.NaN;
    private bool _held;
    private BaselineGrid _last;

    private bool HasGrid => owner.BaselineGridStep > 0 && !double.IsInfinity(owner.BaselineGridStep);

    private MeasurableUIComponent Surface => surface() ?? owner;

    public void Measure()
    {
        _origin = null;
        _phase = PhaseAt(Top());
        Follow(owner.IsAttachedToVisualTree);
    }

    // The grid to lay the text on: the one it was laid on while it is the same, so the layout can tell nothing changed.
    public BaselineGrid Grid()
    {
        var step = owner.BaselineGridStep;
        if (!HasGrid || double.IsNaN(_phase))
        {
            return _last = null;
        }

        return _last is { } last && last.Step.Equals(step) && last.Offset.Equals(_phase)
                                 && last.FirstLineOnly == owner.BaselineGridFirstLineOnly
            ? last
            : _last = new BaselineGrid(step, _phase, owner.BaselineGridFirstLineOnly);
    }

    public void Follow(bool attached)
    {
        var follows = attached && HasGrid;
        if (follows && _manager == null)
        {
            _manager = LayoutManager.For(owner);
            _manager.PlacesSettled += OnSettled;
        }
        else if (!follows && _manager != null)
        {
            _manager.PlacesSettled -= OnSettled;
            _manager = null;
            _origin = null;
        }
    }

    private double Top()
    {
        var top = Surface.TopOnBaselineGrid(ref _origin);
        return double.IsNaN(top) ? top : top + textTop();
    }

    // Laid out where it no longer stands (a place no parent told it while measuring): measured again there - unless it
    // moved by its own new height, as a text centered or at the bottom does, which can never settle on the grid.
    private void OnSettled(object sender, EventArgs e)
    {
        if (double.IsNaN(_phase))
        {
            return;
        }

        var top = Top();
        var height = Surface.RenderSize.Height;
        var moved = top - _top;
        var grown = height - _height;
        _top = top;
        _height = height;
        var phase = PhaseAt(top);
        if (double.IsNaN(phase) || SamePhase(phase, _phase))
        {
            _held = false;
            return;
        }

        var followsItsHeight = Math.Abs(grown) > 0.01
                               && (Math.Abs(moved + grown) < 0.5 || Math.Abs(moved + grown / 2) < 0.5)
                               && PlacedByItsHeight();
        if (followsItsHeight || (_held && Math.Abs(moved) < 0.01))
        {
            _held = true;
            return;
        }

        _held = false;
        remeasure();
    }

    // Centered or at the bottom of a slot taller than it, itself or an element up to the grid's start: its height moves it.
    private bool PlacedByItsHeight()
    {
        for (IUIComponent element = Surface; element != null && !ReferenceEquals(element, _origin);
             element = element.VisualParent)
        {
            if (element is MeasurableUIComponent
                {
                    VerticalAlignment: VerticalAlignment.Center or VerticalAlignment.Bottom,
                    PreviousArrangeSlot: { } slot,
                } measurable
                && slot.Height - measurable.EffectiveMargin.Top - measurable.EffectiveMargin.Bottom
                > measurable.Bounds.Height + 0.5)
            {
                return true;
            }
        }

        return false;
    }

    private double PhaseAt(double top)
    {
        var step = owner.BaselineGridStep;
        var phase = (owner.BaselineGridOffset - top) % step;
        return phase < 0 ? phase + step : phase;
    }

    private bool SamePhase(double a, double b)
    {
        var step = owner.BaselineGridStep;
        var apart = ((a - b) % step + step) % step;
        return !double.IsNaN(apart) && Math.Min(apart, step - apart) <= 0.01;
    }
}
