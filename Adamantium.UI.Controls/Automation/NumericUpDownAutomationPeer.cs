using System;
using System.Collections.Generic;
using System.Globalization;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="NumericUpDown"/>: a spinner whose value is its number, written and read in the
/// invariant culture. Its text box and buttons are parts of it, not children.</summary>
public class NumericUpDownAutomationPeer : UIComponentAutomationPeer, IValueProvider
{
    public NumericUpDownAutomationPeer(NumericUpDown owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Spinner;

    public string Value => ((NumericUpDown)Owner).Value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public bool IsReadOnly => ((NumericUpDown)Owner).IsReadOnly;

    /// <summary>Writes the number as a current value, clamped to the range as typing it would be.</summary>
    public void SetValue(string value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"'{AutomationId}' is read-only.");
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw new FormatException($"'{value}' is not a number.");
        }

        Owner.SetCurrentValue(NumericUpDown.ValueProperty, number);
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
