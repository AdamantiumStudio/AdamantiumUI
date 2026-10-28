namespace Adamantium.UI.Core;

public interface IWindowWorkerService
{
    public void SetWindow(IWindow window);

    public void SetTitle(string title);

    /// <summary>Move the OS window so its top-left sits at these SCREEN coordinates. Backs <c>Window.Left</c>/<c>Top</c>,
    /// which used to be managed values nobody acted on - assigning them changed a number and left the window where it
    /// was. Size and z-order are untouched, and the window is not activated: moving a window is not focusing it.</summary>
    public void SetPosition(double left, double top);

    /// <summary>Resize the OS window so its CLIENT area becomes this big, in logical (DIP) units. Backs
    /// <c>Window.ClientWidth</c>/<c>ClientHeight</c>, which - unlike Left/Top - used to be managed values nobody acted
    /// on: assigning them changed a number and left the window the size it was created. Position and z-order are
    /// untouched, and the window is not activated.</summary>
    public void SetSize(double clientWidth, double clientHeight);

    /// <summary>The window's outer rectangle as it is when neither maximized nor minimized, in physical screen pixels:
    /// what a maximized window goes back to, and so what is remembered for it. Empty when unknown.</summary>
    public Rect RestoreBounds { get; }

    /// <summary>Re-apply the window's overlay traits - topmost, click-through, transparency - to the live OS window.
    /// Called whenever one of them changes, so setting <c>Topmost</c> on an open window actually raises it instead of
    /// changing a number nobody reads again.</summary>
    public void UpdateOverlayTraits();

    /// <summary>Keeps the window above <paramref name="owner"/> and minimized with it; null frees it.</summary>
    public void SetOwner(IWindow owner) { }

    /// <summary>Applies <see cref="IWindow.IsModal"/> as it is now, done by the time it returns: while a window is modal,
    /// the application's other windows that were there when it became modal take no input; one that something else
    /// keeps from input stays so. Called before the window is hidden or destroyed, so activation
    /// goes back to its owner.</summary>
    public void UpdateModal() { }

    public void ShowWindow(WindowState windowState);

    public void HideWindow();

    /// <summary>Bring the window to the foreground (un-minimizing it first if needed). Used to activate a newly opened
    /// window and to focus an already-open single-instance window on a repeat request.</summary>
    public void Activate();

    /// <summary>Raises the window without activating it, so a drag's mouse capture survives.</summary>
    public void RaiseWithoutActivation();

    /// <summary>Acquire (true) or release (false) the OS-level mouse capture for this window, so a press-drag keeps
    /// receiving move/up even when the pointer leaves the window. Platform-specific (Win32 SetCapture/ReleaseCapture).
    /// The shared logic that decides WHEN to call this lives in PointerDevice.SyncOsMouseCapture, so every platform gets
    /// the same behavior.</summary>
    public void SetMouseCapture(bool capture);

    /// <summary>A desktop point to a point of the window's client area, both in physical pixels.</summary>
    public PixelPoint ScreenToClient(PixelPoint screen);

    /// <summary>A point of the window's client area to a desktop point, both in physical pixels.</summary>
    public PixelPoint ClientToScreen(PixelPoint client);

    /// <summary>Enters or leaves relative mouse mode (hidden, centered cursor producing raw deltas); on leave the cursor
    /// reappears at <paramref name="restoreScreen"/>.</summary>
    public void SetRelativeMouseMode(bool enabled, PixelPoint restoreScreen);

    /// <summary>Turns the OS input method on or off for the window.</summary>
    public void SetInputMethodEnabled(bool enabled);

    /// <summary>Start an OS-driven move of the window from the current cursor (custom-chrome caption drag). Runs the
    /// native modal move loop, so Aero Snap and snap-to-edges work. Call from a title bar's mouse-down. No-op on
    /// platforms without a native move loop.</summary>
    public void BeginMoveDrag();

    /// <summary>The same, for a window held at <paramref name="grab"/>, a logical point of it - one not under the pointer
    /// yet, such as a panel just torn off.</summary>
    public void BeginMoveDrag(Mathematics.Vector2<double> grab) => BeginMoveDrag();

    public IUIContext UIContext { get; }
}