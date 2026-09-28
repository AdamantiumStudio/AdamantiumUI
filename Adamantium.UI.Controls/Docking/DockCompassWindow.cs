using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Docking;

/// <summary>The compass's own window - topmost, input-transparent, never focused, see-through per pixel - so it can draw
/// over the window being dragged.</summary>
public class DockCompassWindow : Window
{
    public DockCompassWindow()
    {
        UseTransparentComposition = true;
        Topmost = true;
        // No frame and nothing to grab: an overlay has no caption, no buttons and no resize borders. Set here rather
        // than left to the template, because these decide the NATIVE window, not what is drawn inside it.
        ResizeMode = WindowResizeMode.NoResize;
        ShowWindowBorder = false;    // a shape floating over other windows, not a window with a frame
        TransparentToInput = true;   // it is a read-out of a gesture, never a thing to click
        // Not activating also keeps it out of the task bar and Alt-Tab: the platform worker turns it into a tool window.
        ActivateOnShow = false;      // taking focus mid-drag would end the drag it is there to serve
    }

}


