using System.Collections.Generic;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a window: the root of its tree, called by its title, and found by its class name unless it is
/// given an id or a name. Its open popups are among its children, except the list of a drop-down and a submenu, which
/// belong to the control that opened them.</summary>
public class WindowAutomationPeer : UIComponentAutomationPeer
{
    public WindowAutomationPeer(WindowBase owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Window;

    public override string AutomationId => base.AutomationId is { Length: > 0 } id ? id : ClassName;

    protected override string NameCore() => ((WindowBase)Owner).Title;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>(base.ChildrenCore());
        foreach (var root in ((WindowBase)Owner).PopupRoots)
        {
            var shownFor = Popup.PopupOf(root)?.TemplatedParent;
            if (shownFor is DropDown or MenuItem || root is not UIComponent popup)
            {
                continue;
            }

            if (shownFor is ContextMenu menu)
            {
                children.Add(menu.GetAutomationPeer());
            }
            else if (popup.GetAutomationPeer() is { } peer)
            {
                children.Add(peer);
            }
            else
            {
                Collect(popup, children);
            }
        }

        return children;
    }
}
