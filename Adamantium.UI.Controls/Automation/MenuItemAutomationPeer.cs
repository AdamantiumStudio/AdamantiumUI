using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="MenuItem"/>. A row with a submenu expands and holds the submenu's rows as children;
/// a row without one is pressed, and a checkable row toggles - each the way choosing the row does.</summary>
public class MenuItemAutomationPeer : ItemsControlAutomationPeer, IInvokeProvider, IToggleProvider, IExpandCollapseProvider
{
    public MenuItemAutomationPeer(MenuItem owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.MenuItem;

    protected override AutomationControlType ItemControlType => AutomationControlType.MenuItem;

    public ToggleState ToggleState => ((MenuItem)Owner).IsChecked ? ToggleState.On : ToggleState.Off;

    public ExpandCollapseState ExpandCollapseState => ((MenuItem)Owner) switch
    {
        { HasItems: false } => ExpandCollapseState.LeafNode,
        { IsSubmenuOpen: true } => ExpandCollapseState.Expanded,
        _ => ExpandCollapseState.Collapsed
    };

    public override AutomationPeer GetParent() => ItemsOwnerPeer() ?? base.GetParent();

    public override object GetPattern(PatternId pattern)
    {
        var row = (MenuItem)Owner;
        return pattern switch
        {
            PatternId.Invoke when row.HasItems => null,
            PatternId.Toggle when !row.IsCheckable => null,
            PatternId.ExpandCollapse when !row.HasItems => null,
            _ => base.GetPattern(pattern)
        };
    }

    public void Invoke() => ((MenuItem)Owner).Invoke();

    public void Toggle() => ((MenuItem)Owner).Invoke();

    public void Expand() => Owner.SetCurrentValue(MenuItem.IsSubmenuOpenProperty, true);

    public void Collapse() => Owner.SetCurrentValue(MenuItem.IsSubmenuOpenProperty, false);

    protected override string NameCore() => ((MenuItem)Owner).Header as string ?? TextOf(Owner);
}
