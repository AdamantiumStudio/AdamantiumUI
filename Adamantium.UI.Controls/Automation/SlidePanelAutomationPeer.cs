using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="SlidePanel"/>: a pane called by its header that slides open and shut. Its UI lives in
/// the overlay, so its children are what it shows there while open, and it is off the screen while shut.</summary>
public class SlidePanelAutomationPeer : ContentControlAutomationPeer, IExpandCollapseProvider
{
    private readonly SlidePanel _panel;

    public SlidePanelAutomationPeer(SlidePanel owner) : base(owner)
    {
        _panel = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.Pane;

    public override bool IsOffscreen => !_panel.IsOpen;

    public ExpandCollapseState ExpandCollapseState =>
        _panel.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => _panel.SetCurrentValue(SlidePanel.IsOpenProperty, true);

    public void Collapse() => _panel.SetCurrentValue(SlidePanel.IsOpenProperty, false);

    protected override string NameCore() => _panel.Header as string;

    protected override IReadOnlyList<AutomationPeer> ChildrenCore()
    {
        var children = new List<AutomationPeer>();
        if (_panel.GetTemplateChild("PART_Popup") is Popup { IsOpen: true, Child: Base.UIComponent shown })
        {
            Collect(shown, children);
        }

        return children;
    }
}
