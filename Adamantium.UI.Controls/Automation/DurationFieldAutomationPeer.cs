using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DurationField"/>: an edit whose value is its length, written and read in the
/// invariant constant form (<c>1.02:30:00</c>, <c>-00:00:05.2500000</c>); its parts are its children, each a spinner.</summary>
public class DurationFieldAutomationPeer : UIComponentAutomationPeer, IValueProvider
{
    private readonly DurationField _field;

    public DurationFieldAutomationPeer(DurationField owner) : base(owner)
    {
        _field = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Edit;

    public string Value => _field.Value?.ToString("c", CultureInfo.InvariantCulture) ?? string.Empty;

    public bool IsReadOnly => false;

    /// <summary>Writes the length as a current value; empty clears it.</summary>
    public void SetValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Owner.SetCurrentValue(DurationField.ValueProperty, null);
            return;
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var length))
        {
            throw new FormatException($"'{value}' is not a length of time.");
        }

        Owner.SetCurrentValue(DurationField.ValueProperty, length);
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() =>
        [.. _field.Segments.Select(segment => segment.GetAutomationPeer()).Where(peer => peer != null)];
}
