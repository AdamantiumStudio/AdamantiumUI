using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="Ribbon"/>: tabs to choose from. Its children stand as they are drawn - "File", the
/// strip's own commands, the tab headers, the band's minimize button and the open tab's groups - and the groups of a
/// minimized band, or its drawer, while they are dropped down over the content. Expanded is the band shown, collapsed
/// the band minimized to its strip.</summary>
public class RibbonAutomationPeer : UIComponentAutomationPeer, ISelectionProvider, IExpandCollapseProvider
{
    public RibbonAutomationPeer(Ribbon owner) : base(owner)
    {
    }

    public ExpandCollapseState ExpandCollapseState =>
        ((Ribbon)Owner).IsMinimized ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded;

    public void Expand() => Owner.SetCurrentValue(Ribbon.IsMinimizedProperty, false);

    public void Collapse() => Owner.SetCurrentValue(Ribbon.IsMinimizedProperty, true);

    public override AutomationControlType ControlType => AutomationControlType.Tab;

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => true;

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        [.. GetChildren().Where(child => child.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider { IsSelected: true })];

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var ribbon = (Ribbon)Owner;
        var children = new List<AutomationPeer>(base.ChildrenCore());
        foreach (var part in (string[])["PART_Flyout", "PART_Drawer"])
        {
            if (ribbon.GetTemplateChild(part) is Popup { IsOpen: true, Child: Base.UIComponent shown })
            {
                Collect(shown, children);
            }
        }

        return children;
    }
}
