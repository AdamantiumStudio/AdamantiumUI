using System;
using System.Reflection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Internals;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>A window on Wayland: an xdg_toplevel surface drawn by its Vulkan swapchain, in logical sizes shown through a
/// viewport at the compositor's scale.</summary>
internal sealed class WaylandWindowWorker : IWindowWorkerService
{
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

    private bool chromeCustom;
    private WindowResizeMode chromeResizeMode;
    private WindowState chromeState;
    private WindowState lastWindowState;

    private int width;
    private int height;
    private int normalWidth;
    private int normalHeight;

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

    private static int InitialExtent(double preferred, double clientFallback, int defaultExtent)
    {
        if (!double.IsNaN(preferred) && preferred > 0)
        {
            return (int)preferred;
        }

        if (!double.IsNaN(clientFallback) && clientFallback > 0)
        {
            return (int)clientFallback;
        }

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

    public void SetPosition(double left, double top)
    {
    }

    public void SetSize(double clientWidth, double clientHeight)
    {
        if (toplevel == null || reportingOsSize || double.IsNaN(clientWidth) || double.IsNaN(clientHeight))
        {
            return;
        }

        if (window.State != WindowState.Normal)
        {
            return;
        }

        var w = (int)Math.Round(clientWidth);
        var h = (int)Math.Round(clientHeight);
        if (w <= 0 || h <= 0 || (w == width && h == height))
        {
            return;
        }

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

    public void HideWindow()
    {
    }

    public void Activate()
    {
    }

    public void RaiseWithoutActivation()
    {
    }

    public void SetMouseCapture(bool capture)
    {
    }

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
        if (toplevel == null || seat.Proxy == null)
        {
            return;
        }

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
        if (point.X < EdgeMargin)
        {
            edge |= XdgToplevel.ResizeEdge.Left;
        }
        else if (point.X >= width - EdgeMargin)
        {
            edge |= XdgToplevel.ResizeEdge.Right;
        }

        if (point.Y < EdgeMargin)
        {
            edge |= XdgToplevel.ResizeEdge.Top;
        }
        else if (point.Y >= height - EdgeMargin)
        {
            edge |= XdgToplevel.ResizeEdge.Bottom;
        }

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
            if (closed)
            {
                return;
            }

            if (reportedWidth.HasValue)
            {
                window.Width = reportedWidth.Value;
                window.Height = reportedHeight.Value;
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
            if (!closed)
            {
                window.DpiScale = new Vector2(scale, scale);
            }
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

        UIContext.UIApplication.RemoveWindow(window);
        window.SetHandle(IntPtr.Zero);

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
        if (closed)
        {
            return;
        }

        window.SetIsActive(true);
        UIContext.UIApplication.SetActiveWindow(window);
        if (!FocusManager.TryRestoreFocus(window))
        {
            KeyboardNavigation.MoveInto(window, NavigationMethod.Unspecified);
        }
    }

    private void HandleDeactivation()
    {
        if (closed)
        {
            return;
        }

        window.SetIsActive(false);
        UIContext.UIApplication.InactivateWindow(window);
        FocusManager.LeaveWindow(window);
    }
}
