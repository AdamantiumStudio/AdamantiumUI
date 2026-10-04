using System;
using System.Collections.Generic;
using System.Globalization;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="NumericUpDown"/>: a spinner whose value is its number, written and read in the
/// invariant culture, or set as a number in its range. Its text box and buttons are parts of it, not children.</summary>
public class NumericUpDownAutomationPeer : UIComponentAutomationPeer, IValueProvider, IRangeValueProvider
{
    private readonly NumericUpDown _numeric;

    public NumericUpDownAutomationPeer(NumericUpDown owner) : base(owner)
    {
        _numeric = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Spinner;

    public string Value => _numeric.Value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public bool IsReadOnly => _numeric.IsReadOnly;

    double IRangeValueProvider.Value => _numeric.Value ?? double.NaN;

    public double Minimum => _numeric.Minimum;

    public double Maximum => _numeric.Maximum;

    public double SmallChange => _numeric.SmallChange;

    public double LargeChange => _numeric.LargeChange;

    /// <summary>Writes the number as a current value, clamped to the range as typing it would be.</summary>
    public void SetValue(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new FormatException($"'{value}' is not a number.");
        }

        Write(number);
    }

    /// <summary>Writes the number as a current value; it must lie in the range.</summary>
    public void SetValue(double value)
    {
        if (value < Minimum || value > Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Outside {Minimum} .. {Maximum}.");
        }

        Write(value);
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];

    private void Write(double number)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"'{AutomationId}' is read-only.");
        }

        Owner.SetCurrentValue(NumericUpDown.ValueProperty, number);
    }
}
