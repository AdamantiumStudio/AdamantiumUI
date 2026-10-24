using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms;

/// <summary>Where the keyboard goes when a window is activated, on every platform.</summary>
internal static class ActivatedWindowFocus
{
    /// <summary>Leaves the focus where it is when it is already in one of <paramref name="window"/>'s popups - a
    /// backstage opened before the window first came up, say, which the window does not know as its own; otherwise puts
    /// it back where it was in the window, or, for a window seen for the first time, on its first stop. Unspecified, not
    /// Tab: activation is not a keyboard move, so no ring lights up.</summary>
    public static void Restore(IWindow window)
    {
        if (FocusManager.Focused is IUIComponent focused && window is IPopupHost host
                                                         && ReferenceEquals(Popup.HostOf(focused), host))
        {
            return;
        }

        if (!FocusManager.TryRestoreFocus(window))
        {
            KeyboardNavigation.MoveInto(window, NavigationMethod.Unspecified);
        }
    }
}
