using System;
using System.Reflection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Internals;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>
/// A window on Wayland: a wl_surface in the xdg_toplevel role, drawn by the window's Vulkan swapchain.
/// <para>
/// The compositor decides, the client follows: a configure says what size and state to take, the client acknowledges
/// it and the next frame it presents is that size. When a configure leaves the size to the client - a window that is
/// neither maximized nor being resized - the window keeps the size it asked for.
/// </para>
/// <para>
/// Sizes are logical. The surface is drawn at the scale the compositor prefers and a viewport shows the drawing at the
/// logical size, so a fractional scale draws as sharp as an integer one.
/// </para>
/// <para>
/// A window drawing its own frame - the default - is resized from its edges by asking the compositor to do it
/// (xdg_toplevel.resize), and moved by its title bar the same way (move). What Wayland does not let a client do is
/// not done: no window is placed on the desktop, raised, kept on top or made click-through.
/// </para>
/// </summary>
internal sealed class WaylandWindowWorker : IWindowWorkerService
{
    // How far in from an edge a press resizes the window, in logical pixels.
    private const double EdgeMargin = 6;

    private readonly WaylandPlatform platform;
    private IWindow window;
    private bool closed;

    private WlSurface surface;
    private XdgSurface xdgSurface;
    private XdgToplevel toplevel;
    private WpViewport viewport;
    private WpFractionalScaleV1 fractionalScale;
    private ZxdgToplevelDecorationV1 decoration;

    // Plain-field snapshots of the window's settings, for the event thread, which must not read an AdamantiumProperty
    // (see the same fields in Win32WindowWorker). Kept current on the loop thread.
    private bool chromeCustom;
    private WindowResizeMode chromeResizeMode;
    private WindowState chromeState;
    private WindowState lastWindowState;

    // Logical sizes: what the window is, and what it goes back to from maximized.
    private int width;
    private int height;
    private int normalWidth;
    private int normalHeight;

    // The configure in progress, applied when xdg_surface.configure closes it.
    private int pendingWidth;
    private int pendingHeight;
    private bool pendingMaximized;
    private bool pendingActivated;
    private bool activated;

    private bool reportingOsSize;

    public WaylandWindowWorker(IUIContext uiContext)
    {
        UIContext = uiContext;
        platform = (WaylandPlatform)uiContext.Resolve<IApplicationPlatform>();
    }

    public IUIContext UIContext { get; }

    /// <summary>The window, or null once it is closed: events still on their way for it go nowhere.</summary>
    public IWindow Window => closed ? null : window;

    internal WlSurface Surface => surface;

    /// <summary>The scale the compositor wants the window drawn at.</summary>
    public double Scale { get; private set; } = 1;

    // The window is created at its requested size; a configure that names one overrides it.
    private static int InitialExtent(double preferred, double clientFallback, int defaultExtent)
    {
        if (!double.IsNaN(preferred) && preferred > 0) return (int)preferred;
        if (!double.IsNaN(clientFallback) && clientFallback > 0) return (int)clientFallback;
        return defaultExtent;
    }

    public void SetWindow(IWindow window)
    {
        this.window = window;
        chromeCustom = window.UseCustomChrome;
        chromeResizeMode = window.ResizeMode;
        chromeState = window.State;
        var minWidth = !double.IsNaN(window.MinWidth) && window.MinWidth > 0 ? window.MinWidth : 320;
        var minHeight = !double.IsNaN(window.MinHeight) && window.MinHeight > 0 ? window.MinHeight : 120;

        width = normalWidth = InitialExtent(window.ClientWidth, window.Width, 800);
        height = normalHeight = InitialExtent(window.ClientHeight, window.Height, 600);
        window.Closed += OnWindowClosed;

        surface = platform.Compositor.CreateSurface();
        if (platform.FractionalScale != null)
        {
            fractionalScale = platform.FractionalScale.GetFractionalScale(surface);
            fractionalScale.PreferredScale += scale => SetScale(scale / 120.0);
        }
        else
        {
            surface.PreferredBufferScale += OnPreferredBufferScale;
        }

        if (platform.Viewporter != null)
        {
            viewport = platform.Viewporter.GetViewport(surface);
            viewport.SetDestination(width, height);
        }

        xdgSurface = platform.WmBase.GetXdgSurface(surface);
        xdgSurface.Configure += OnSurfaceConfigure;
        toplevel = xdgSurface.GetToplevel();
        toplevel.Configure += OnToplevelConfigure;
        toplevel.Close += () => WaylandPlatform.DispatchInput(() => Window?.Close());
        toplevel.SetAppId(Assembly.GetEntryAssembly()?.GetName().Name ?? "adamantium");
        toplevel.SetMinSize((int)minWidth, (int)minHeight);

        // Where the compositor would draw a frame (KDE), the window says whether it draws its own. GNOME draws none.
        if (platform.Decorations != null)
        {
            decoration = platform.Decorations.GetToplevelDecoration(toplevel);
            decoration.SetMode(chromeCustom
                ? ZxdgToplevelDecorationV1.Mode.ClientSide
                : ZxdgToplevelDecorationV1.Mode.ServerSide);
        }

        if (chromeState == WindowState.Maximized)
        {
            toplevel.SetMaximized();
        }

        // The first commit, with no buffer, asks for the first configure; the window shows with its first frame.
        surface.Commit();
        platform.Display.Flush();
        platform.Register(this, surface.Handle);

        window.SetHandle(surface.Handle);
        window.SetSurfaceHandle(surface.Handle, platform.Display.Handle);
        window.DpiScale = new Vector2(Scale, Scale);
        window.ClientWidth = width;
        window.ClientHeight = height;

        Input.DragDrop.RegisterNativeDropTarget(window);
        UIContext.ThemeEngine.ApplyCurrentTheme(window);
        UIContext.UIApplication.AddWindow(window);

        window.OnSourceInitialized();
        window.StateChanged += WindowOnStateChanged;
        window.ResizeModeChanged += WindowOnResizeModeChanged;
    }

    private void WindowOnResizeModeChanged(object sender, EventArgs e) => chromeResizeMode = window.ResizeMode;

    public void SetTitle(string title) => toplevel?.SetTitle(title ?? string.Empty);

    // A client does not place its window on Wayland.
    public void SetPosition(double left, double top)
    {
    }

    public void SetSize(double clientWidth, double clientHeight)
    {
        if (toplevel == null || reportingOsSize || double.IsNaN(clientWidth) || double.IsNaN(clientHeight)) return;

        // Maximized, the compositor decides the size.
        if (window.State != WindowState.Normal) return;

        var w = (int)Math.Round(clientWidth);
        var h = (int)Math.Round(clientHeight);
        if (w <= 0 || h <= 0 || (w == width && h == height)) return;

        // The swapchain follows ClientWidth/Height; the viewport shows it at this size from the frame that brings it.
        width = normalWidth = w;
        height = normalHeight = h;
        viewport?.SetDestination(w, h);
    }

    public Rect RestoreBounds => default;

    public void UpdateOverlayTraits()
    {
    }

    public void ShowWindow(WindowState windowState)
    {
        // The window maps with its first frame, already presented by the time Show asks for it.
        switch (windowState)
        {
            case WindowState.Maximized:
                toplevel?.SetMaximized();
                break;
            case WindowState.Minimized:
                toplevel?.SetMinimized();
                break;
        }
    }

    // Unmapping would take the buffer from under the swapchain; a hidden window is minimized instead by those who hide.
    public void HideWindow()
    {
    }

    // xdg-activation wants a token from the input that asked for it; without one the compositor only marks the window.
    public void Activate()
    {
    }

    public void RaiseWithoutActivation()
    {
    }

    // A press grabs the pointer for the surface until it is released; there is nothing to take.
    public void SetMouseCapture(bool capture)
    {
    }

    // The window's client corner is the origin of the only desktop a Wayland client knows.
    public PixelPoint ScreenToClient(PixelPoint screen) => screen;

    public PixelPoint ClientToScreen(PixelPoint client) => client;

    public void SetRelativeMouseMode(bool enabled, PixelPoint restoreScreen)
    {
    }

    public void SetInputMethodEnabled(bool enabled)
    {
    }

    public void BeginMoveDrag()
    {
        var seat = platform.Seat;
        if (toplevel == null || seat.Proxy == null) return;

        toplevel.Move(seat.Proxy, seat.LastInputSerial);
    }

    /// <summary>The edge a press at this point resizes the window from: none unless the window draws its own frame,
    /// can be resized, and is not maximized.</summary>
    internal XdgToplevel.ResizeEdge ResizeEdgeAt(Vector2 point)
    {
        if (!chromeCustom || chromeState != WindowState.Normal || closed)
        {
            return XdgToplevel.ResizeEdge.None;
        }

        if (chromeResizeMode == WindowResizeMode.CanResizeWithGrip)
        {
            return window.ResizeGripRect.Contains(point) ? XdgToplevel.ResizeEdge.BottomRight : XdgToplevel.ResizeEdge.None;
        }

        if (chromeResizeMode != WindowResizeMode.CanResize)
        {
            return XdgToplevel.ResizeEdge.None;
        }

        var edge = XdgToplevel.ResizeEdge.None;
        if (point.X < EdgeMargin) edge |= XdgToplevel.ResizeEdge.Left;
        else if (point.X >= width - EdgeMargin) edge |= XdgToplevel.ResizeEdge.Right;
        if (point.Y < EdgeMargin) edge |= XdgToplevel.ResizeEdge.Top;
        else if (point.Y >= height - EdgeMargin) edge |= XdgToplevel.ResizeEdge.Bottom;
        return edge;
    }

    /// <summary>Hands the resize from this edge to the compositor; false when there is no edge to resize from.</summary>
    internal bool BeginResize(XdgToplevel.ResizeEdge edge, uint serial)
    {
        var seat = platform.Seat;
        if (edge == XdgToplevel.ResizeEdge.None || toplevel == null || seat.Proxy == null)
        {
            return false;
        }

        toplevel.Resize(seat.Proxy, serial, edge);
        return true;
    }

    private void OnToplevelConfigure(int configuredWidth, int configuredHeight, ReadOnlySpan<byte> states)
    {
        pendingWidth = configuredWidth;
        pendingHeight = configuredHeight;
        pendingMaximized = false;
        pendingActivated = false;

        // An array of uint32 xdg_toplevel.state values.
        for (var i = 0; i + 4 <= states.Length; i += 4)
        {
            switch ((XdgToplevel.State)BitConverter.ToUInt32(states.Slice(i, 4)))
            {
                case XdgToplevel.State.Maximized:
                case XdgToplevel.State.Fullscreen:
                    pendingMaximized = true;
                    break;
                case XdgToplevel.State.Activated:
                    pendingActivated = true;
                    break;
            }
        }
    }

    private void OnSurfaceConfigure(uint serial)
    {
        xdgSurface.AckConfigure(serial);
        if (closed)
        {
            return;
        }

        var w = pendingWidth;
        var h = pendingHeight;
        if (w <= 0 || h <= 0)
        {
            // The size is the client's: what it had before it was maximized, or what it asked for.
            w = normalWidth;
            h = normalHeight;
        }
        else if (!pendingMaximized)
        {
            normalWidth = w;
            normalHeight = h;
        }

        var resized = w != width || h != height;
        if (resized)
        {
            width = w;
            height = h;
            viewport?.SetDestination(w, h);
        }

        // Minimized is not something Wayland reports: a window stays minimized until it is active again.
        WindowState? osState = pendingMaximized ? WindowState.Maximized : WindowState.Normal;
        if (chromeState == WindowState.Minimized && !pendingActivated)
        {
            osState = null;
        }

        ReportOsState(resized ? w : null, resized ? h : null, osState);

        if (pendingActivated != activated)
        {
            activated = pendingActivated;
            WaylandPlatform.DispatchInput(activated ? HandleActivation : HandleDeactivation);
        }
    }

    private void ReportOsState(int? reportedWidth, int? reportedHeight, WindowState? osState)
    {
        if (osState == chromeState)
        {
            osState = null;
        }

        if (reportedWidth == null && osState == null)
        {
            return;
        }

        if (osState.HasValue)
        {
            chromeState = osState.Value;
        }

        WaylandPlatform.DispatchInput(() =>
        {
            if (closed) return;

            if (reportedWidth.HasValue)
            {
                window.Width = reportedWidth.Value;
                window.Height = reportedHeight.Value;
                // A report of what the compositor did, not a request: SetSize must not send it back (see Win32WindowWorker).
                reportingOsSize = true;
                window.ClientWidth = reportedWidth.Value;
                window.ClientHeight = reportedHeight.Value;
                reportingOsSize = false;
            }

            if (osState.HasValue && window.State != osState.Value)
            {
                window.StateChanged -= WindowOnStateChanged;
                window.State = osState.Value;
                window.StateChanged += WindowOnStateChanged;
            }
        });
    }

    private void OnPreferredBufferScale(int factor)
    {
        // Without a viewport the buffer itself says its scale; with one, the viewport maps it.
        if (viewport == null)
        {
            surface.SetBufferScale(factor);
        }

        SetScale(factor);
    }

    private void SetScale(double scale)
    {
        if (scale <= 0 || scale == Scale)
        {
            return;
        }

        Scale = scale;
        WaylandPlatform.DispatchInput(() =>
        {
            if (!closed) window.DpiScale = new Vector2(scale, scale);
        });
    }

    private void WindowOnStateChanged(object sender, StateChangedEventArgs e)
    {
        if (e.State == WindowState.Minimized && chromeState != WindowState.Minimized)
            lastWindowState = chromeState;

        var previous = chromeState;
        chromeState = e.State;
        switch (e.State)
        {
            case WindowState.Maximized:
                toplevel?.SetMaximized();
                break;
            case WindowState.Minimized:
                toplevel?.SetMinimized();
                break;
            case WindowState.Normal when previous == WindowState.Maximized || lastWindowState == WindowState.Maximized:
                toplevel?.UnsetMaximized();
                break;
        }
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        if (closed)
        {
            return;
        }

        closed = true;
        platform.Unregister(surface.Handle);

        Input.DragDrop.UnregisterNativeDropTarget(window);

        // The renderer lets go of the swapchain and its surface here, before the wl_surface they draw to goes.
        UIContext.UIApplication.RemoveWindow(window);
        window.SetHandle(IntPtr.Zero);

        // Destroyed on the thread that dispatches their events, so none arrives for an object half gone.
        _ = UIContext.UIApplication.ExecuteOnUIThreadAsync(() =>
        {
            decoration?.Destroy();
            viewport?.Destroy();
            fractionalScale?.Destroy();
            toplevel.Destroy();
            xdgSurface.Destroy();
            surface.Destroy();
            platform.Display.Flush();
        });
    }

    private void HandleActivation()
    {
        if (closed) return;

        window.SetIsActive(true);
        UIContext.UIApplication.SetActiveWindow(window);
        if (!FocusManager.TryRestoreFocus(window))
        {
            KeyboardNavigation.MoveInto(window, NavigationMethod.Unspecified);
        }
    }

    private void HandleDeactivation()
    {
        if (closed) return;

        window.SetIsActive(false);
        UIContext.UIApplication.InactivateWindow(window);
        FocusManager.LeaveWindow(window);
    }
}
