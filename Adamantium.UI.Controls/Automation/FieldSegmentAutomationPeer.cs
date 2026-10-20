using System;
using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="FieldSegment"/>: a spinner whose value is the part's number - the day, the month,
/// the hours; AM is 0 and PM 1, an era its number in the calendar - NaN while it is empty. The day of the week is shown,
/// not entered: its value is read-only.</summary>
public class FieldSegmentAutomationPeer : UIComponentAutomationPeer, IRangeValueProvider
{
    private readonly FieldSegment _segment;

    public FieldSegmentAutomationPeer(FieldSegment owner) : base(owner)
    {
        _segment = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Spinner;

    public double Value => _segment.Host?.ValueOf(_segment) ?? double.NaN;

    public bool IsReadOnly => _segment.Host == null || _segment.Host.IsReadOnly(_segment);

    public double Minimum => _segment.Host?.RangeOf(_segment).Minimum ?? 0;

    public double Maximum => _segment.Host?.RangeOf(_segment).Maximum ?? 0;

    public double SmallChange => 1;

    public double LargeChange => 1;

    /// <summary>Writes the part; it must be a whole number in its range.</summary>
    public void SetValue(double value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"'{AutomationId}' is read-only.");
        }

        if (value != Math.Floor(value))
        {
            throw new ArgumentException($"{value} is not a whole number.", nameof(value));
        }

        var (minimum, maximum) = _segment.Host.RangeOf(_segment);
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Outside {minimum} .. {maximum}.");
        }

        _segment.Host.Write(_segment, (long)value);
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
