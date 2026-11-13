using Adamantium.UI.Core;
using Adamantium.UI.Core.Controls;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Controls.Internals;

internal static class WindowInternals
{
    public static void SetHandle(this IWindow window, IntPtr windowHandle)
    {
        if (window is IWindowInternals windowBase)
        {
            windowBase.SetHandle(windowHandle);
        }
    }

    public static void RaiseWindowMoving(this IWindow window)
    {
        if (window is IWindowInternals windowBase)
        {
            windowBase.RaiseWindowMoving();
        }
    }

    public static void RaiseWindowMoveCompleted(this IWindow window)
    {
        if (window is IWindowInternals windowBase)
        {
            windowBase.RaiseWindowMoveCompleted();
        }
    }

    public static void SetSurfaceHandle(this IWindow window, IntPtr surfaceHandle, IntPtr displayHandle = default)
    {
        if (window is IWindowInternals windowBase)
        {
            windowBase.SetSurface(surfaceHandle, displayHandle);
        }
    }

    public static void OnSourceInitialized(this IWindow window)
    {
        if (window is IWindowInternals windowBase)
        {
            windowBase.OnSourceInitialized();
        }
    }

    public static void SetIsActive(this IWindow window, bool isActive)
    {
        if (window is IWindowInternals windowBase)
        {
            windowBase.SetIsActive(isActive);
        }
    }

    /// <summary>The window became the active one: the application's, with the keyboard back where it was in it.</summary>
    public static void BecomeActive(this IWindow window, IUIApplication application)
    {
        window.SetIsActive(true);
        application.SetActiveWindow(window);
        FocusManager.EnterWindow(window);
    }

    /// <summary>The window is no longer the active one: the keyboard leaves it, remembered for when it is back.</summary>
    public static void StopBeingActive(this IWindow window, IUIApplication application)
    {
        window.SetIsActive(false);
        application.InactivateWindow(window);
        // The one focused element must not stay pointing into a window the keys no longer reach - see LeaveWindow.
        FocusManager.LeaveWindow(window);
    }
}
