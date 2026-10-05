using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonTabHeader"/>: a tab of the ribbon, selected the way a click selects it - on a
/// minimized band that also drops the tab's groups down.</summary>
public class RibbonTabHeaderAutomationPeer : ContentControlAutomationPeer, ISelectionItemProvider
{
    private readonly RibbonTabHeader _header;

    public RibbonTabHeaderAutomationPeer(RibbonTabHeader owner) : base(owner)
    {
        _header = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.TabItem;

    public bool IsSelected => _header.IsSelected;

    public AutomationPeer SelectionContainer => OwningRibbon()?.GetAutomationPeer();

    public void Select()
    {
        if (!IsSelected)
        {
            OwningRibbon()?.ClickTab(_header);
        }
    }

    protected override string NameCore() => base.NameCore() ?? TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];

    private Ribbon OwningRibbon() => _header.GetVisualAncestors().OfType<Ribbon>().FirstOrDefault();
}
