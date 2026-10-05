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

    /// <summary>The value in the invariant culture; empty while the objects disagree.</summary>
    public string Value => _row.IsMixed ? string.Empty : Convert.ToString(_row.Value, CultureInfo.InvariantCulture) ?? string.Empty;

    public bool IsReadOnly => _row.IsReadOnly;

    public ToggleState ToggleState => _row.IsMixed
        ? ToggleState.Indeterminate
        : _row.Value as bool? == true ? ToggleState.On : ToggleState.Off;

    public ExpandCollapseState ExpandCollapseState =>
        _row.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public override object GetPattern(PatternId pattern) => pattern switch
    {
        PatternId.Toggle when _row.ValueType != typeof(bool) => null,
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

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>();
        Collect(_row, children);
        return children;
    }
}
