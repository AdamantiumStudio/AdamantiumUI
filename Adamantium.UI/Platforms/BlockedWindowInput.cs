using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms;

/// <summary>What a window a modal one has begun to keep from input lets go of.</summary>
internal static class BlockedWindowInput
{
    /// <summary>Lets go of the pointer, when something in the window captured it, and of the keyboard focus.</summary>
    internal static void LetGo(IWindow window)
    {
        if (Mouse.Pointer.Captured is IUIComponent captured && RootOf(captured) == window)
        {
            Mouse.Pointer.Capture(null);
        }

        FocusManager.LeaveWindow(window);
    }

    private static IUIComponent RootOf(IUIComponent node)
    {
        while (node?.VisualParent is { } parent)
        {
            node = parent;
        }

        return node;
    }
}
