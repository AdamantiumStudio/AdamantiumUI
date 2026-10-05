using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonApplicationMenu"/>: "File", a button that opens the menu. Open, its children
/// are what the menu shows; closed, its items.</summary>
public class RibbonApplicationMenuAutomationPeer : ItemsControlAutomationPeer, IExpandCollapseProvider
{
    private readonly RibbonApplicationMenu _menu;

    public RibbonApplicationMenuAutomationPeer(RibbonApplicationMenu owner) : base(owner)
    {
        _menu = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Button;

    public override string AccessKey => base.AccessKey is { Length: > 0 } own
        ? own
        : _menu.GetTemplateChild("PART_Button") is Base.UIComponent button
            ? KeyTipService.GetKeyTip(button) ?? string.Empty
            : string.Empty;

    public ExpandCollapseState ExpandCollapseState =>
        _menu.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => _menu.SetCurrentValue(RibbonApplicationMenu.IsOpenProperty, true);

    public void Collapse() => _menu.SetCurrentValue(RibbonApplicationMenu.IsOpenProperty, false);

    protected override AutomationControlType ItemControlType => AutomationControlType.MenuItem;

    protected override string NameCore() =>
        _menu.Header as string ?? (_menu.GetTemplateChild("PART_Button") is Base.UIComponent button ? TextOf(button) : null);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        if (_menu.GetTemplateChild("PART_Popup") is not Popup { IsOpen: true, Child: Base.UIComponent shown })
        {
            return base.ChildrenCore();
        }

        var children = new List<AutomationPeer>();
        Collect(shown, children);
        return children;
    }
}
