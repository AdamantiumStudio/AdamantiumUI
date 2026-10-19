using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls;

/// <summary>The title over one group of a <see cref="RibbonGallery"/>'s drop-down: the group's key, shown through
/// <see cref="RibbonGallery.GroupHeaderTemplate"/>. It takes a row of its own, the width of the cells.</summary>
public class RibbonGalleryGroupHeader : ContentControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new CaptionAutomationPeer(this);
}
