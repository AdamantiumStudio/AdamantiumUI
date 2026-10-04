namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="ContentControl"/>: called by its content when that is text.</summary>
public class ContentControlAutomationPeer : UIComponentAutomationPeer
{
    public ContentControlAutomationPeer(ContentControl owner) : base(owner)
    {
    }

    protected override string NameCore() => ((ContentControl)Owner).Content as string;
}
