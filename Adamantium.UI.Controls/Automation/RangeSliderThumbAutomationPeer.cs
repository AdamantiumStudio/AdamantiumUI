using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>One handle of a <see cref="RangeSlider"/>, holding its lower or upper bound. A bound is kept off the other
/// one as dragging keeps it.</summary>
public class RangeSliderThumbAutomationPeer : UIComponentAutomationPeer, IRangeValueProvider
{
    private readonly RangeSlider _slider;
    private readonly bool _upper;

    public RangeSliderThumbAutomationPeer(RangeSlider slider, Thumb thumb, bool upper) : base(thumb)
    {
        _slider = slider;
        _upper = upper;
    }

    public override AutomationControlType ControlType => AutomationControlType.Thumb;

    public override string AutomationId => _upper ? "Upper" : "Lower";

    public override bool IsEnabled => _slider.IsEnabled;

    public double Value => _upper ? _slider.UpperValue : _slider.LowerValue;

    public double Minimum => _slider.Minimum;

    public double Maximum => _slider.Maximum;

    public double SmallChange => _slider.SmallChange;

    public double LargeChange => _slider.LargeChange;

    public bool IsReadOnly => false;

    public void SetValue(double value)
    {
        if (value < Minimum || value > Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Outside {Minimum} .. {Maximum}.");
        }

        _slider.SetCurrentValue(_upper ? RangeSlider.UpperValueProperty : RangeSlider.LowerValueProperty, value);
    }

    public override AutomationPeer GetParent() => _slider.GetAutomationPeer();

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
