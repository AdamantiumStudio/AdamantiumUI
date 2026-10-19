using System;
using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DateTimeFieldSegment"/>: a spinner whose value is the part's number - the day, the
/// month, the hour; AM is 0 and PM 1, an era its number in the calendar - NaN while it is empty. The day of the week is
/// shown, not entered: its value is read-only.</summary>
public class DateTimeFieldSegmentAutomationPeer : UIComponentAutomationPeer, IRangeValueProvider
{
    private readonly DateTimeFieldSegment _segment;

    public DateTimeFieldSegmentAutomationPeer(DateTimeFieldSegment owner) : base(owner)
    {
        _segment = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Spinner;

    public double Value => _segment.Owner?.ValueOf(_segment.Kind) ?? double.NaN;

    public bool IsReadOnly => _segment.Owner == null || _segment.Kind == DateTimeSegmentKind.DayOfWeek;

    public double Minimum => _segment.Owner?.RangeOf(_segment.Kind).Minimum ?? 0;

    public double Maximum => _segment.Owner?.RangeOf(_segment.Kind).Maximum ?? 0;

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

        _segment.Owner.Write(_segment, (int)value);
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
