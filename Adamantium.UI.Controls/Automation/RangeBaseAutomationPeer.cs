using System;
using System.Collections.Generic;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a control holding one number between two limits. Its thumb and track are parts of it, not
/// children.</summary>
public class RangeBaseAutomationPeer : UIComponentAutomationPeer, IRangeValueProvider
{
    public RangeBaseAutomationPeer(RangeBase owner) : base(owner)
    {
    }

    public double Value => ((RangeBase)Owner).Value;

    public double Minimum => ((RangeBase)Owner).Minimum;

    public double Maximum => ((RangeBase)Owner).Maximum;

    public double SmallChange => ((RangeBase)Owner).SmallChange;

    public double LargeChange => ((RangeBase)Owner).LargeChange;

    public virtual bool IsReadOnly => false;

    /// <summary>Writes the number as a current value, so a binding on the control keeps working.</summary>
    public void SetValue(double value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"'{AutomationId}' is read-only.");
        }

        if (value < Minimum || value > Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Outside {Minimum} .. {Maximum}.");
        }

        Owner.SetCurrentValue(RangeBase.ValueProperty, value);
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
