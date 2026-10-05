using System;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonGroup"/>: a group called by its header, whose children are its commands. A
/// group the band has collapsed to one button opens and closes its commands, and wears that button's key tip.</summary>
public class RibbonGroupAutomationPeer : ItemsControlAutomationPeer, IExpandCollapseProvider
{
    private readonly RibbonGroup _group;

    public RibbonGroupAutomationPeer(RibbonGroup owner) : base(owner)
    {
        _group = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Group;

    public override string AccessKey => base.AccessKey is { Length: > 0 } own
        ? own
        : _group.IsCollapsed && _group.GetTemplateChild("PART_CollapsedButton") is Base.UIComponent button
            ? KeyTipService.GetKeyTip(button) ?? string.Empty
            : string.Empty;

    public ExpandCollapseState ExpandCollapseState =>
        _group.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.ExpandCollapse && !_group.IsCollapsed ? null : base.GetPattern(pattern);

    public void Expand()
    {
        if (!_group.IsCollapsed)
        {
            throw new InvalidOperationException($"'{Name}' is not collapsed; its commands are already in view.");
        }

        _group.SetCurrentValue(RibbonGroup.IsDropDownOpenProperty, true);
    }

    public void Collapse() => _group.SetCurrentValue(RibbonGroup.IsDropDownOpenProperty, false);

    protected override AutomationControlType ItemControlType => AutomationControlType.Button;

    protected override string NameCore() => _group.Header as string;
}
