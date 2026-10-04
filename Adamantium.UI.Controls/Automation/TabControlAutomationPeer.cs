using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="TabControl"/>: its tabs - every one, a virtualizing strip's unbuilt ones as
/// stand-ins - then the button that lists the tabs that do not fit, then the page of the tab selected.</summary>
public class TabControlAutomationPeer : ItemsControlAutomationPeer, ISelectionProvider
{
    public TabControlAutomationPeer(TabControl owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Tab;

    protected override AutomationControlType ItemControlType => AutomationControlType.TabItem;

    public bool CanSelectMultiple => false;

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        [.. base.ChildrenCore().Where(child => child.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider { IsSelected: true })];

    protected override UIComponent ContainerAt(int index) => ((TabControl)Owner).ContainerOfTab(index) as UIComponent;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var tabs = (TabControl)Owner;
        var children = new List<AutomationPeer>(base.ChildrenCore());
        if (tabs.GetTemplateChild("PART_TabOverflow") is UIComponent overflow && overflow.GetAutomationPeer() is { } more)
        {
            children.Add(more);
        }

        if (tabs.GetTemplateChild("PART_SelectedContentHost") is UIComponent page)
        {
            Collect(page, children);
        }

        return children;
    }
}
