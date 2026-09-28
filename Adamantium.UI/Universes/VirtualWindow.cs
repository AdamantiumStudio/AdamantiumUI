using Adamantium.Multiverse;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;

namespace Adamantium.UI.Universes;

/// <summary>
/// A window with no OS window of its own: drawn inside a host surface - the designer's preview target. It IS a
/// <see cref="Window"/>, so it carries every window property and takes the theme's window style and template: a previewed
/// window shows its title bar, its caption content and its window commands exactly as the running one does.
/// </summary>
public class VirtualWindow : Window, IVirtualWindow
{
    public UniverseOutput RootWindow { get; set; }

    // No OS window, so no platform worker: everything a window asks of it (title, position, size, activation) does nothing.
    protected override IWindowWorkerService CreateWindowWorker(IUIContext context) => null;

    public override void Show()
    {
        Visibility = Visibility.Visible;
    }

    public override void Hide()
    {
        Visibility = Visibility.Collapsed;
    }
}
