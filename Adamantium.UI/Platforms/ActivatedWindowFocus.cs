using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms;

/// <summary>Where the keyboard goes when a window is activated, on every platform.</summary>
internal static class ActivatedWindowFocus
{
    /// <summary>Puts the focus back where it was in <paramref name="window"/> - in one of its popups too, a backstage
    /// opened before the window first came up, say; a window that has nowhere to go back to starts at its first stop.
    /// Unspecified, not Tab: activation is not a keyboard move, so no ring lights up.</summary>
    public static void Restore(IWindow window)
    {
        if (!FocusManager.TryRestoreFocus(window))
        {
            KeyboardNavigation.MoveInto(window, NavigationMethod.Unspecified);
        }
    }
}
