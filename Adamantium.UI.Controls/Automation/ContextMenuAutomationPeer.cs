using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ContextMenu"/>: a menu of its rows.</summary>
public class ContextMenuAutomationPeer : ItemsControlAutomationPeer
{
    public ContextMenuAutomationPeer(ContextMenu owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.Menu;

    protected override AutomationControlType ItemControlType => AutomationControlType.MenuItem;
}
