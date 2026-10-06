using Adamantium.UI.Controls.Base;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a question put in place - discard the unsaved work, clear the canvas: a pane called by what it
/// asks, holding its answers.</summary>
public class QuestionAutomationPeer : PaneAutomationPeer
{
    public QuestionAutomationPeer(UIComponent owner) : base(owner)
    {
    }

    protected override string NameCore() => TextOf(Owner);
}
