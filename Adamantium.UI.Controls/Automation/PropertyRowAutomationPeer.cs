using System;
using System.Collections.Generic;
using System.Globalization;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="PropertyRow"/>: a property called by its name, whose value is written the way its
/// editor writes it - converted to the property's type, to every object the row stands for. A true-or-false property
/// is toggled, a composite one opens to its parts; its editor is its child.</summary>
public class PropertyRowAutomationPeer : UIComponentAutomationPeer, IValueProvider, IToggleProvider, IExpandCollapseProvider
{
    private readonly PropertyRow _row;

    public PropertyRowAutomationPeer(PropertyRow owner) : base(owner)
    {
        _row = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.DataItem;

    /// <summary>The value as its line reads it for automation - the invariant culture's text, a date or a length in
    /// the line's own form; empty while the objects disagree.</summary>
    public string Value => _row.IsMixed
        ? string.Empty
        : (_row.Definition is { } line ? line.AutomationTextOf(_row.Value) : Convert.ToString(_row.Value, CultureInfo.InvariantCulture))
          ?? string.Empty;

    public bool IsReadOnly => _row.IsReadOnly;

    public ToggleState ToggleState => _row.IsMixed || _row.Value is not bool
        ? ToggleState.Indeterminate
        : (bool)_row.Value ? ToggleState.On : ToggleState.Off;

    public ExpandCollapseState ExpandCollapseState =>
        _row.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public override object GetPattern(PatternId pattern) => pattern switch
    {
        PatternId.Toggle when (_row.ValueType is { } type ? Nullable.GetUnderlyingType(type) ?? type : null) != typeof(bool) => null,
        PatternId.ExpandCollapse when !_row.HasChildren => null,
        _ => base.GetPattern(pattern)
    };

    public void SetValue(string value)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException($"'{Name}' is read-only.");
        }

        if (_row.Owner?.Write(_row, value) != true)
        {
            throw new InvalidOperationException($"'{Name}' refused '{value}'.");
        }
    }

    public void Toggle() => _row.Owner?.ToggleRow(_row);

    public void Expand()
    {
        if (!_row.IsExpanded)
        {
            _row.Owner?.ToggleComposite(_row);
        }
    }

    public void Collapse()
    {
        if (_row.IsExpanded)
        {
            _row.Owner?.ToggleComposite(_row);
        }
    }

    protected override string NameCore() => _row.Definition?.Header as string ?? TextOf(_row);

    /// <summary>The row's editor goes by the property's name.</summary>
    protected internal override string NameForChild(AutomationPeer child) => Name is { Length: > 0 } name ? name : null;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>();
        Collect(_row, children);
        return children;
    }
}
