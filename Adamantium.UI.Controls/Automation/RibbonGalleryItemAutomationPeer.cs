using System.Collections.Generic;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonGalleryItem"/>: one choice of a gallery, picked the way a click picks it -
/// selected, or invoked in a gallery with an <see cref="RibbonGallery.ItemCommand"/>.</summary>
public class RibbonGalleryItemAutomationPeer : ContentControlAutomationPeer, ISelectionItemProvider, IInvokeProvider
{
    private readonly RibbonGalleryItem _cell;

    public RibbonGalleryItemAutomationPeer(RibbonGalleryItem owner) : base(owner)
    {
        _cell = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.ListItem;

    public bool IsSelected => _cell.IsSelected;

    public AutomationPeer SelectionContainer => ItemsOwnerPeer();

    public void Select() => Gallery()?.PickFromContainer(_cell);

    public void Invoke() => Gallery()?.PickFromContainer(_cell);

    public override object GetPattern(PatternId pattern)
    {
        var runs = Gallery()?.ItemCommand != null;
        return pattern switch
        {
            PatternId.Invoke => runs ? this : null,
            PatternId.SelectionItem => runs ? null : this,
            _ => base.GetPattern(pattern)
        };
    }

    protected override string NameCore() => base.NameCore() ?? TextOf(Owner);

    protected override IReadOnlyList<AutomationPeer> ChildrenCore() => [];

    // Told to the cell, not found by walking: a drop-down cell lives in a popup with no ancestors to find.
    private RibbonGallery Gallery() => _cell.Owner;
}
