using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="DateTimeField"/>: an edit whose value is its date and time, written and read in the
/// invariant culture's round-trip form (<c>2026-10-19T14:05:00</c>); its parts are its children, each a spinner.</summary>
public class DateTimeFieldAutomationPeer : UIComponentAutomationPeer, IValueProvider
{
    private readonly DateTimeField _field;

    public DateTimeFieldAutomationPeer(DateTimeField owner) : base(owner)
    {
        _field = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Edit;

    public string Value => _field.Value?.ToString("s", CultureInfo.InvariantCulture) ?? string.Empty;

    public bool IsReadOnly => false;

    /// <summary>Writes the date and time as a current value; empty clears it.</summary>
    public void SetValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Owner.SetCurrentValue(DateTimeField.ValueProperty, null);
            return;
        }

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new FormatException($"'{value}' is not a date and time.");
        }

        Owner.SetCurrentValue(DateTimeField.ValueProperty, date);
    }

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() =>
        [.. _field.Segments.Select(segment => segment.GetAutomationPeer()).Where(peer => peer != null)];
}
