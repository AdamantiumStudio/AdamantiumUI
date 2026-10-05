using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonApplicationMenuItem"/>: a row of the "File" menu, pressed to run its command;
/// a row with a page is also selected, which shows the page.</summary>
public class RibbonApplicationMenuItemAutomationPeer : ButtonAutomationPeer, ISelectionItemProvider
{
    private readonly RibbonApplicationMenuItem _row;

    public RibbonApplicationMenuItemAutomationPeer(RibbonApplicationMenuItem owner) : base(owner)
    {
        _row = owner;
    }

    public bool IsSelected => _row.IsSelected;

    public AutomationPeer SelectionContainer => ItemsOwnerPeer();

    public override object GetPattern(PatternId pattern) =>
        pattern == PatternId.SelectionItem && _row.PageContent == null ? null : base.GetPattern(pattern);

    public void Select()
    {
        if (!IsSelected)
        {
            _row.PerformClick();
        }
    }
}
