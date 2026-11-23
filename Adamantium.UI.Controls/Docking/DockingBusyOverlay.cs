using Adamantium.UI.Controls.Base;

namespace Adamantium.UI.Controls.Docking;

/// <summary>What covers a <see cref="DockingArea"/> while it is <see cref="DockingArea.IsBusy"/>: it takes the pointer,
/// and the area under it is disabled, so nothing in its panes and documents can be changed. A control, so a theme draws
/// it - a shade with a busy indicator.</summary>
public class DockingBusyOverlay : TemplatedUIComponent
{
}
