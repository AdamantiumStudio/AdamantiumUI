namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of an <see cref="InfoBar"/>: a group named by its title, which holds its actions and its close button.
/// </summary>
public class InfoBarAutomationPeer : GroupAutomationPeer
{
    public InfoBarAutomationPeer(InfoBar owner) : base(owner)
    {
    }

    protected override string NameCore() => ((InfoBar)Owner).Title;
}
