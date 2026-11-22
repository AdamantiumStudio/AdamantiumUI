using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a strip of commands: a tool bar of what it holds.</summary>
public class ToolBarAutomationPeer : UIComponentAutomationPeer
{
    public ToolBarAutomationPeer(UIComponent owner) : base(owner)
    {
    }

    public override AutomationControlType ControlType => AutomationControlType.ToolBar;

    // A tool bar is called by its header.
    protected override string NameCore() => (Owner as ToolBar)?.Header as string;

    // A contextual bar's context, as its label says it - the label itself is no element of its own - unless the
    // application gives its own help text.
    public override string HelpText =>
        AutomationProperties.GetHelpText(Owner) is { Length: > 0 } own
            ? own
            : Owner is ToolBar { HasContextualHeader: true, ContextualHeader: var header } ? header?.ToString() ?? string.Empty
            : string.Empty;
}
