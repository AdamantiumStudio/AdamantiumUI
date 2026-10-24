using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core;

public interface IWindow : IRootVisualComponent, IContentControl
{
    void Show();
    void Close();
    void Hide();

    /// <summary>Bring the window to the foreground and give it focus.</summary>
    void Activate();

    /// <summary>Raise the window above the others WITHOUT taking focus - safe to call mid-drag, unlike
    /// <see cref="Activate"/>, which would make the OS revoke the capture the drag depends on.</summary>
    void BringToFront();

    /// <summary>The PLATFORM moved the window - a caption drag, Aero Snap, a monitor going away - and reports where it
    /// ended up, WITHOUT the window moving itself again in response. The OS move loop is invisible to managed code, so
    /// without this Left/Top only ever hold what was last assigned to them, and anything that reads a window's
    /// position afterwards (a layout being saved) writes down where it used to be.</summary>
    void UpdatePositionFromPlatform(double left, double top);

    /// <summary>The platform reports the client size it gave the window. Only a size the window did not ask for ends
    /// <see cref="SizeToContent"/>.</summary>
    void UpdateClientSizeFromPlatform(double clientWidth, double clientHeight);

    /// <summary>Record where the OS just put this window, callable from ANY thread - see
    /// <see cref="IRootVisualComponent.LivePosition"/>. Separate from
    /// <see cref="UpdatePositionFromPlatform"/> because that one writes bindable properties and must be marshaled to
    /// the loop thread, which is exactly the delay anything drawn from the position cannot afford.</summary>
    void UpdateLivePosition(double left, double top);
        
    bool IsActive { get; }

    IntPtr Handle { get; }
    
    IntPtr SurfaceHandle { get; }

    /// <summary>The connection the surface belongs to, where the platform's Vulkan surface needs one beside it: the
    /// wl_display on Wayland. Zero elsewhere.</summary>
    IntPtr SurfaceDisplayHandle => IntPtr.Zero;
    
    bool IsClosed { get; }

    /// <summary>Whether the window is up on the desktop: shown, with its first frame, and not hidden since. A window
    /// with no OS window of its own is up for as long as it is open.</summary>
    bool IsDisplayed => !IsClosed;

    MSAALevel MSAALevel { get; set; }

    /// <summary>Toggles the GPU analytic anti-aliasing (coverage fringe on fills + feathered strokes). True = on.
    /// Independent of <see cref="MSAALevel"/>, so the two can be compared in any combination.</summary>
    bool AnalyticAntialiasing { get; set; }

    WindowState State { get; set; }

    /// <summary>Which sides of the window follow what its content asks for; <see cref="Core.SizeToContent.Manual"/>
    /// keeps the size the window is given.</summary>
    SizeToContent SizeToContent { get; set; }

    /// <summary>When true the OS frame is removed (WM_NCCALCSIZE reclaims the non-client area) and the window draws its
    /// own chrome (title bar) - the modern borderless look. WS_THICKFRAME is kept so native resize borders, Aero Snap,
    /// the drop shadow and maximize-to-work-area still work. Read by the platform worker.</summary>
    bool UseCustomChrome { get; }

    /// <summary>How the user may resize the window (honored by the custom-chrome hit-test). Default CanResize.</summary>
    WindowResizeMode ResizeMode { get; }

    /// <summary>The window this one belongs to: it opens over its middle, is closed with it and, where the platform does
    /// that, stays above it and is minimized with it. Null for none.</summary>
    IWindow Owner { get; set; }

    /// <summary>Whether the window is shown modally: the application's other windows, those that were there when it was
    /// shown, take no input until it closes or is hidden.</summary>
    bool IsModal { get; }

    /// <summary>Shows the window modally over its <see cref="Owner"/>; the task ends when it closes or is hidden.</summary>
    Task ShowDialogAsync();

    /// <summary>Counts <paramref name="window"/> among the windows this one owns, to be closed with it; false once
    /// <see cref="Closing"/> has let this window close, when it takes no more. Called by the owned window's
    /// <see cref="Owner"/>.</summary>
    bool AddOwnedWindow(IWindow window);

    /// <summary>Stops counting <paramref name="window"/> among the windows this one owns.</summary>
    void RemoveOwnedWindow(IWindow window);

    /// <summary>Frees the window from its owner, which closes without it; a modal window stops being modal.</summary>
    void LeaveOwner();

    // Overlay traits. Settable, and they take effect on a window that is already open: a property that can only be set
    // before the window exists is one that silently does nothing afterwards, which is worse than not having it.

    /// <summary>Stays above other windows.</summary>
    bool Topmost { get; set; }

    /// <summary>Clicks pass through to whatever is behind.</summary>
    bool TransparentToInput { get; set; }

    /// <summary>False shows the window without giving it focus. Read when the window is shown.</summary>
    bool ActivateOnShow { get; set; }

    /// <summary>Uniform translucency of the whole window, 0..1.</summary>
    double WindowOpacity { get; set; }

    /// <summary>Whether the window has the OS frame around it - the ambient drop shadow and the 1px accent outline the
    /// desktop draws. Its OWN property because it is its own decision: a window can be see-through and still framed, or
    /// opaque and frameless. Tying it to how the window is composed makes one of those impossible to ask for.</summary>
    bool ShowWindowBorder { get; set; }

    /// <summary>Per-pixel transparency via premultiplied desktop composition, read when the surface is created; opaque if
    /// the surface cannot do it.</summary>
    bool UseTransparentComposition { get; set; }

    /// <summary>How this window's frames reach the screen, or Inherit to follow the application's setting. Each window
    /// owns its own swapchain and they can sit on different displays, so the choice belongs here - with an
    /// application-wide default for the usual case where it is set once.</summary>
    Adamantium.Graphics.Core.Presentation.PresentPolicy PresentPolicy { get; set; }

    /// <summary>A steady frame rate in hertz, over the minimum and maximum; 0 lets the rate vary, null follows the
    /// application.</summary>
    uint? RefreshRate { get; set; }

    /// <summary>Frames a second at least, even when nothing changes; 0 none then, null follows the application.</summary>
    uint? MinimumRefreshRate { get; set; }

    /// <summary>Frames a second at most; 0 unlimited, null follows the application.</summary>
    uint? MaximumRefreshRate { get; set; }

    /// <summary>Draws frame after frame as fast as it can, whatever changed and whatever the other rates say, presenting
    /// without waiting for the monitor: the mode to measure in. Null follows the application.</summary>
    bool? UnlimitedRefreshRate { get; set; }

    /// <summary>The refresh rate of the window's monitor in hertz, set by the platform; 0 when unknown.</summary>
    double DisplayRefreshRate { get; set; }

    /// <summary>The ONE draggable caption region (client DIP): the bounds of the title bar's drag-area element. A point
    /// inside it is HTCAPTION (native window drag + Aero Snap); everything else - commands, buttons - stays HTCLIENT and
    /// clickable. A TitleBar publishes this on layout; the platform worker reads it (a plain Rect, thread-safe) from the
    /// OS message thread. Empty = nothing draggable (custom chrome off, or no title bar).</summary>
    Rect CaptionDragRect { get; set; }

    /// <summary>The resize grip's bounds in client DIPs, hit-tested as the bottom-right sizing corner; empty means no
    /// grip.</summary>
    Rect ResizeGripRect { get; set; }

    /// <summary>The focused text's caret in client DIPs, where the input method puts its windows; set through
    /// <see cref="Input.InputMethod.SetCaretBounds"/> and read by the platform worker from the OS message thread.</summary>
    Rect InputMethodCaret { get; set; }

    /// <summary>Turns the input method off while the window takes text that must not pass through it - a password,
    /// which an input method would show in clear and may remember - and back on after. Kept until the native window
    /// exists, so a field focused before it shows is covered too.</summary>
    void SetInputMethodEnabled(bool enabled);

    /// <summary>Per-window DPI scale (device pixels per DIP), separate X/Y (usually equal on desktop). 1,1 = 96 DPI /
    /// 100%. Set by the platform on create and on WM_DPICHANGED; drives the render scale and the DIP&lt;-&gt;physical map.</summary>
    Vector2 DpiScale { get; set; }

    IWindowRenderer DefaultRenderer { get; set; }

    IWindowRenderer Renderer { get; set; }

    /// <summary>Framework tooling overlays (selection frames etc.) drawn ON TOP of the window's content in the same
    /// frame by the renderer's adorner stage. Empty by default; the WPF-equivalent of an AdornerLayer's adorners.</summary>
    IReadOnlyList<IUIComponent> Adorners { get; }

    /// <summary>The laid-out children of the open popups, drawn ON TOP of the content (and adorners) within the window by
    /// the renderer's popup stage. Empty by default. <see cref="LayoutPopups"/> refreshes their positions each frame.</summary>
    IReadOnlyList<IUIComponent> PopupRoots { get; }

    /// <summary>Re-evaluates every open popup's position from its target's current location and lays its child out,
    /// clamped inside the window. Called by the popup render stage each frame so a popup follows a moving target.</summary>
    void LayoutPopups();
    
    bool ShouldDisplayWindow { get; }
    
    IDrawingContext GetDrawingContext();

    /// <summary>A desktop point (physical) in this window's client coordinates (logical DIP).</summary>
    Vector2 ScreenToClient(PixelPoint p);

    /// <summary>A client point (logical DIP) as a desktop point (physical).</summary>
    PixelPoint ClientToScreen(Vector2 p);
    
    event SizeChangedEventHandler ClientSizeChanged;
    event EventHandler<WindowClosingEventArgs> Closing;
    event MSAALevelChangedHandler MSAALevelChanged;
    event StateChangedHandler StateChanged;

    /// <summary>Raised after <see cref="ResizeMode"/> changes, so the platform worker can refresh its thread-safe snapshot
    /// (the WM_NCHITTEST hit-test must not read the lockable ResizeMode property from the OS message thread).</summary>
    event System.EventHandler ResizeModeChanged;

    /// <summary>Raised after <see cref="DpiScale"/> changes (the window moved to a monitor with a different scale).</summary>
    event EventHandler<EventArgs> DpiChanged;

    event EventHandler<EventArgs> Closed;

    event EventHandler<WindowRendererChangedEventArgs> RendererChanged;
    
    event EventHandler<EventArgs> SourceInitialized;
}
