using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="RibbonGallery"/>: a list of choices, one picked at a time, that opens to show them
/// all. Unnamed, it is called by its group.</summary>
public class RibbonGalleryAutomationPeer : ItemsControlAutomationPeer, ISelectionProvider, IExpandCollapseProvider
{
    private readonly RibbonGallery _gallery;

    public RibbonGalleryAutomationPeer(RibbonGallery owner) : base(owner)
    {
        _gallery = owner;
    }

    public override AutomationControlType ControlType => AutomationControlType.List;

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => false;

    public IReadOnlyList<AutomationPeer> GetSelection() =>
        [.. GetChildren().Where(child => child.GetPattern(PatternId.SelectionItem) is ISelectionItemProvider { IsSelected: true })];

    public ExpandCollapseState ExpandCollapseState =>
        _gallery.IsDropDownOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand() => _gallery.SetCurrentValue(RibbonGallery.IsDropDownOpenProperty, true);

    public void Collapse() => _gallery.SetCurrentValue(RibbonGallery.IsDropDownOpenProperty, false);

    protected override string NameCore() =>
        _gallery.GetVisualAncestors().OfType<RibbonGroup>().FirstOrDefault()?.Header as string;
}
