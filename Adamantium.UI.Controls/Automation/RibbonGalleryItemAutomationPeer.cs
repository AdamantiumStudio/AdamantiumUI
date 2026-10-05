using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonGalleryItem"/>: one choice of a gallery, picked the way a click picks it.</summary>
public class RibbonGalleryItemAutomationPeer : ContentControlAutomationPeer, ISelectionItemProvider
{
    private readonly RibbonGalleryItem _cell;

    public RibbonGalleryItemAutomationPeer(RibbonGalleryItem owner) : base(owner)
    {
        _cell = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.ListItem;

    public bool IsSelected => _cell.IsSelected;

    public AutomationPeer SelectionContainer => ItemsOwnerPeer();

    public void Select() => ((ItemsOwnerPeer()?.Owner) as RibbonGallery)?.PickFromContainer(_cell);

    protected override string NameCore() => base.NameCore() ?? TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];
}
