using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Docking;

/// <summary>What covers a <see cref="DockingArea"/> while it is <see cref="DockingArea.IsBusy"/>: it takes the pointer and
/// the keyboard, so nothing in the panes and documents under it can be changed. A control, so a theme draws it - a
/// shade with a busy indicator.</summary>
public class DockingBusyOverlay : TemplatedUIComponent
{
    public DockingBusyOverlay()
    {
        // The focus moves here when the area turns busy, so a field being typed in stops taking keys.
        Focusable = true;
    }

    // Tab would carry the focus on into a pane under the overlay.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            e.Handled = true;
        }
    }
}
