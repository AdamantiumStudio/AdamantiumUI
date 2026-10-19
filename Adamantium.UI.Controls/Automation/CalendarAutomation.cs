using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

internal static class CalendarAutomation
{
    public static IReadOnlyList<AutomationPeer> Shown(IReadOnlyList<AutomationPeer> peers, UIComponent owner) =>
        [.. peers.Where(peer => peer is not UIComponentAutomationPeer { Owner: var element } || !IsHidden(element, owner))];

    private static bool IsHidden(IUIComponent element, UIComponent owner)
    {
        for (var at = element; at != null && at != owner; at = at.VisualParent)
        {
            if (at.Visibility != Visibility.Visible)
            {
                return true;
            }
        }

        return false;
    }
}
