using System;
using System.Collections.Generic;
using System.Linq;
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
    private const int IconScale = 1;

    private readonly WaylandPlatform platform;
    private IWindow window;
    private bool closed;

    private WlSurface surface;
    private XdgSurface xdgSurface;
    private XdgToplevel toplevel;
    private WpViewport viewport;
    private WpFractionalScaleV1 fractionalScale;

    private readonly List<WlOutput> shownOn = [];
    private readonly object shownOnLock = new();
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

    private string title = string.Empty;
    private int minWidth;
    private int minHeight;
    private readonly object mapLock = new();
    private bool hidden;
    private bool remapping;
    private bool configuredUnmapped;
    private bool unmapping;
    private long presentedWhenShown;
    private ZxdgExportedV2 exported;
    private XdgToplevelSessionV1 toplevelSession;
    private string exportedHandle;
    private XdgDialogV1 dialog;
    private IWindow owner;
    private readonly List<WaylandWindowWorker> blockedWindows = [];
    private readonly HashSet<WaylandWindowWorker> blockers = [];
    private readonly HashSet<WaylandWindowWorker> exempt = [];
    private bool blocking;
    private volatile bool inputBlocked;

    public WaylandWindowWorker(IUIContext uiContext)
    {
        UIContext = uiContext;
        platform = (WaylandPlatform)uiContext.Resolve<IApplicationPlatform>();
    }

    public IUIContext UIContext { get; }

    /// <summary>The window, or null once it is closed: events still on their way for it go nowhere.</summary>
    public IWindow Window => closed ? null : window;

    /// <summary>The window input goes to: null while it is closed, or while a modal window keeps it from input.</summary>
    internal IWindow InputWindow => inputBlocked ? null : Window;

    internal WlSurface Surface => surface;

    internal XdgToplevel Toplevel => toplevel;

    /// <summary>Whether the window is open and its surface still there to name in a request.</summary>
    internal bool IsLive => !closed && surface is { IsAlive: true };

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
        minWidth = !double.IsNaN(window.MinWidth) && window.MinWidth > 0 ? (int)window.MinWidth : 320;
        minHeight = !double.IsNaN(window.MinHeight) && window.MinHeight > 0 ? (int)window.MinHeight : 120;

        width = normalWidth = InitialExtent(window.ClientWidth, window.Width, 800);
        height = normalHeight = InitialExtent(window.ClientHeight, window.Height, 600);
        window.Closed += OnWindowClosed;

        surface = platform.Compositor.CreateSurface();
        surface.Enter += OnEnterOutput;
        surface.Leave += OnLeaveOutput;
        platform.Outputs.Changed += UpdateDisplayRefreshRate;
        UpdateDisplayRefreshRate();
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
        toplevel.Close += () => WaylandPlatform.DispatchInput(() => InputWindow?.Close());
        if (platform.Decorations != null)
        {
            decoration = platform.Decorations.GetToplevelDecoration(toplevel);
        }

        SendToplevelState();
        if (window.Owner != null)
        {
            ParentTo(window.Owner);
        }
        if (platform.Session != null &&
            window is Adamantium.UI.Controls.WindowBase { RemembersPlacement: true, ActivateOnShow: true } remembered)
        {
            toplevelSession = platform.Session.Restore(toplevel, remembered.PlacementName());
        }

        ApplyInputRegion(window.TransparentToInput);
        remapping = true;
        ((UIApplication)UIContext.UIApplication).SetWindowHidden(window, true);
        surface.Commit();
        platform.Display.Flush();
        platform.Register(this, surface.Handle);

        window.SetHandle(surface.Handle);
        window.SetSurfaceHandle(surface.Handle, platform.Display.Handle);
        window.DpiScale = new Vector2(Scale, Scale);
        window.UpdateClientSizeFromPlatform(width, height);

        if (platform.IsApplication)
        {
            Input.DragDrop.RegisterNativeDropTarget(window);
        }

        UIContext.ThemeEngine.ApplyCurrentTheme(window);
        UIContext.UIApplication.AddWindow(window);

        window.OnSourceInitialized();
        window.StateChanged += WindowOnStateChanged;
        window.ResizeModeChanged += WindowOnResizeModeChanged;
    }

    private void WindowOnResizeModeChanged(object sender, EventArgs e) => chromeResizeMode = window.ResizeMode;

    public void SetTitle(string title)
    {
        this.title = title ?? string.Empty;
        toplevel?.SetTitle(this.title);
    }

    private void SendToplevelState()
    {
        toplevel.SetAppId(WaylandPlatform.AppId);
        SendIcon();
        toplevel.SetTitle(title);
        toplevel.SetMinSize(minWidth, minHeight);
        decoration?.SetMode(chromeCustom ? ZxdgToplevelDecorationV1.Mode.ClientSide : ZxdgToplevelDecorationV1.Mode.ServerSide);
        if (chromeState == WindowState.Maximized)
        {
            toplevel.SetMaximized();
        }
    }

    private void SendIcon()
    {
        if (platform.ToplevelIcons is not { } icons || platform.Icon is not { } appIcon)
        {
            return;
        }

        var icon = icons.CreateIcon();
        icon.SetName(WaylandPlatform.AppId);
        List<WlBuffer> buffers = [];
        foreach (var picture in appIcon.Pictures)
        {
            if (WaylandShm.CreateBuffer(platform, picture.PremultipliedBgra, picture.Size, picture.Size) is { } buffer)
            {
                icon.AddBuffer(buffer, IconScale);
                buffers.Add(buffer);
            }
        }

        icons.SetIcon(toplevel, icon);
        icon.Destroy();
        foreach (var buffer in buffers)
        {
            buffer.Destroy();
        }
    }

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

    public void SetOwner(IWindow owner)
    {
        platform.Invoke(() =>
        {
            if (closed)
            {
                return;
            }

            ParentTo(owner);
            platform.Display.Flush();
        });
    }

    private void ParentTo(IWindow owner)
    {
        this.owner = owner;
        SendParent();
    }

    private void SendParent()
    {
        toplevel.SetParent(owner == null ? null : platform.Find(owner.Handle)?.Toplevel);
    }

    public void UpdateModal()
    {
        List<WaylandWindowWorker> changed;
        lock (platform.ModalLock)
        {
            var modal = window.IsModal && !closed;
            if (modal == blocking)
            {
                return;
            }

            blocking = modal;
            if (modal)
            {
                exempt.UnionWith(blockers);
                platform.CollectOthers(this, blockedWindows);
                foreach (var other in blockedWindows)
                {
                    other.blockers.Add(this);
                }

                changed = [.. blockedWindows];
            }
            else
            {
                foreach (var other in blockedWindows)
                {
                    other.blockers.Remove(this);
                }

                changed = [.. blockedWindows];
                blockedWindows.Clear();
                exempt.Clear();
            }

            Recount();
            foreach (var other in changed)
            {
                other.Recount();
            }
        }

        platform.Invoke(SendModal);
    }

    private void SendModal()
    {
        bool modal;
        lock (platform.ModalLock)
        {
            modal = blocking;
        }

        if (closed || platform.Dialogs == null || (dialog == null && !modal))
        {
            return;
        }

        dialog ??= platform.Dialogs.GetXdgDialog(toplevel);
        if (modal)
        {
            dialog.SetModal();
        }
        else
        {
            dialog.UnsetModal();
        }

        platform.Display.Flush();
    }

    private void Recount()
    {
        var blocked = false;
        foreach (var blocker in blockers)
        {
            if (!blocking || !exempt.Contains(blocker))
            {
                blocked = true;
                break;
            }
        }

        if (blocked == inputBlocked)
        {
            return;
        }

        inputBlocked = blocked;
        if (blocked)
        {
            WaylandPlatform.DispatchInput(LetGo);
        }
    }

    private void LetGo()
    {
        if (Window is not { } shown)
        {
            return;
        }

        if (Mouse.Pointer.Captured is IUIComponent captured && RootOf(captured) == shown)
        {
            Mouse.Pointer.Capture(null);
        }

        FocusManager.LeaveWindow(shown);
    }

    private static IUIComponent RootOf(IUIComponent node)
    {
        while (node?.VisualParent is { } parent)
        {
            node = parent;
        }

        return node;
    }

    /// <summary>Asks for the modal window keeping this one from input to be activated: the keyboard came here.</summary>
    internal void ActivateBlocker()
    {
        WaylandWindowWorker modal = null;
        lock (platform.ModalLock)
        {
            foreach (var blocker in blockers)
            {
                if (blocker.blocking)
                {
                    modal = blocker;
                }
            }
        }

        modal?.Activate();
    }

    public void UpdateOverlayTraits()
    {
        var transparent = window.TransparentToInput;
        platform.Invoke(() =>
        {
            if (closed)
            {
                return;
            }

            ApplyInputRegion(transparent);
            if (!hidden)
            {
                surface.Commit();
            }

            platform.Display.Flush();
        });
    }

    private void ApplyInputRegion(bool transparent)
    {
        if (!transparent)
        {
            surface.SetInputRegion(null);
            return;
        }

        var region = platform.Compositor.CreateRegion();
        surface.SetInputRegion(region);
        region.Destroy();
    }

    public void ShowWindow(WindowState windowState)
    {
        lock (mapLock)
        {
            if (hidden && !closed)
            {
                hidden = false;
                if (configuredUnmapped)
                {
                    configuredUnmapped = false;
                    presentedWhenShown = Presented();
                    ((UIApplication)UIContext.UIApplication).SetWindowHidden(window, false);
                    var shown = window;
                    WaylandPlatform.DispatchInput(() => shown.InvalidateRender(true));
                }
                else if (!remapping)
                {
                    remapping = true;
                    platform.Queue(Remap);
                }
            }
        }

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
        lock (mapLock)
        {
            if (hidden || closed || surface == null)
            {
                return;
            }

            hidden = true;
            if (remapping)
            {
                return;
            }

            var shownAt = presentedWhenShown;
            ((UIApplication)UIContext.UIApplication).SetWindowHidden(window, true, () =>
            {
                if (Presented() == shownAt)
                {
                    configuredUnmapped = true;
                }
                else
                {
                    platform.Queue(Unmap);
                }
            });
        }
    }

    private long Presented() => window.Renderer?.PresentedFrames ?? 0;

    private void Remap()
    {
        if (closed)
        {
            return;
        }

        SendToplevelState();
        if (owner != null)
        {
            SendParent();
        }

        SendModal();
        surface.Commit();
        platform.Display.Flush();
    }

    private void Unmap()
    {
        if (closed)
        {
            return;
        }

        surface.Attach(null, 0, 0);
        surface.Commit();
        lock (mapLock)
        {
            unmapping = true;
        }

        var fence = platform.Display.Proxy.Sync();
        fence.Done += _ =>
        {
            lock (mapLock)
            {
                unmapping = false;
            }
        };
        platform.Display.Flush();
    }

    private void OnMapped()
    {
        lock (mapLock)
        {
            if (closed || !remapping)
            {
                return;
            }

            remapping = false;
            if (hidden)
            {
                configuredUnmapped = true;
                return;
            }

            presentedWhenShown = Presented();
            ((UIApplication)UIContext.UIApplication).SetWindowHidden(window, false);
        }

        window.InvalidateRender(true);
    }

    /// <summary>The handle another client names this window by (xdg_foreign), made on first ask; null where the
    /// compositor cannot export it or does not answer in time.</summary>
    internal string ExportHandle()
    {
        if (exportedHandle != null || platform.Exporter == null)
        {
            return exportedHandle;
        }

        var answered = new System.Threading.Tasks.TaskCompletionSource();
        platform.Invoke(() =>
        {
            if (closed || exported != null)
            {
                answered.TrySetResult();
                return;
            }

            exported = platform.Exporter.ExportToplevel(surface);
            exported.HandleEvent += handle =>
            {
                exportedHandle = handle;
                answered.TrySetResult();
            };
            platform.Display.Flush();
        });
        answered.Task.Wait(TimeSpan.FromSeconds(1));
        return exportedHandle;
    }

    public void Activate() => platform.Invoke(RequestActivation);

    private void RequestActivation()
    {
        if (closed || platform.Activation == null || !platform.IsApplication)
        {
            return;
        }

        var launchToken = Environment.GetEnvironmentVariable("XDG_ACTIVATION_TOKEN");
        if (!string.IsNullOrEmpty(launchToken))
        {
            Environment.SetEnvironmentVariable("XDG_ACTIVATION_TOKEN", null);
            platform.Activation.Activate(launchToken, surface);
            platform.Display.Flush();
            return;
        }

        var seat = platform.Seat;
        var token = platform.Activation.GetActivationToken();
        if (seat != null && seat.LastInputSerial != 0)
        {
            token.SetSerial(seat.LastInputSerial, seat.Proxy);
        }

        var focused = seat?.LastInputWindow is { IsLive: true } inputWindow ? inputWindow
            : seat?.KeyboardWindow is { IsLive: true } keyboardWindow ? keyboardWindow
            : seat?.PointerWindow is { IsLive: true } pointerWindow ? pointerWindow
            : null;
        if (focused != null)
        {
            token.SetSurface(focused.Surface);
        }

        token.Done += value =>
        {
            token.Destroy();
            if (!closed)
            {
                platform.Activation.Activate(value, surface);
            }

            platform.Display.Flush();
        };
        token.Commit();
        platform.Display.Flush();
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
        var at = new Vector2(restoreScreen.X / Scale, restoreScreen.Y / Scale);
        platform.Invoke(() =>
        {
            if (closed)
            {
                return;
            }

            if (enabled)
            {
                platform.Seat.LockPointer(this);
            }
            else
            {
                platform.Seat.UnlockPointer(at);
            }
        });
    }

    /// <summary>Whether the window takes text from the input method; on unless it says otherwise.</summary>
    internal bool WantsInputMethod { get; private set; } = true;

    public void SetInputMethodEnabled(bool enabled)
    {
        WantsInputMethod = enabled;
        platform.Invoke(() => platform.TextInput?.Update());
    }

    public void BeginMoveDrag()
    {
        var pointer = platform.Seat.Pointer;
        BeginMoveDrag(pointer.Window == this ? pointer.Position : Vector2.Zero);
    }

    /// <summary>A window someone follows the moves of is carried in a drag the compositor reports over our other
    /// windows; any other is moved by the compositor alone.</summary>
    public void BeginMoveDrag(Vector2 grab)
    {
        if (window is Adamantium.UI.Controls.WindowBase { IsMoveTracked: true } && platform.WindowDrag?.Start(this, grab) == true)
        {
            return;
        }

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
        bool awaited;
        lock (mapLock)
        {
            if (unmapping)
            {
                return;
            }

            awaited = remapping;
        }

        xdgSurface.AckConfigure(serial);
        if (closed)
        {
            return;
        }

        if (awaited)
        {
            WaylandPlatform.DispatchInput(OnMapped);
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

            if (osState.HasValue && window.State != osState.Value)
            {
                window.StateChanged -= WindowOnStateChanged;
                window.State = osState.Value;
                window.StateChanged += WindowOnStateChanged;
            }

            if (reportedWidth.HasValue)
            {
                window.Width = reportedWidth.Value;
                window.Height = reportedHeight.Value;
                reportingOsSize = true;
                window.UpdateClientSizeFromPlatform(reportedWidth.Value, reportedHeight.Value);
                reportingOsSize = false;
            }
        });
    }

    private void OnEnterOutput(WlOutput output)
    {
        if (output == null)
        {
            return;
        }

        lock (shownOnLock)
        {
            shownOn.Add(output);
        }

        UpdateDisplayRefreshRate();
    }

    private void OnLeaveOutput(WlOutput output)
    {
        lock (shownOnLock)
        {
            shownOn.RemoveAll(o => !o.IsAlive || (output != null && o.Handle == output.Handle));
        }

        UpdateDisplayRefreshRate();
    }

    private void UpdateDisplayRefreshRate()
    {
        lock (shownOnLock)
        {
            shownOn.RemoveAll(o => !o.IsAlive || !platform.Outputs.Knows(o));
            window.DisplayRefreshRate = shownOn.Count > 0
                ? platform.Outputs.RefreshRateOf(shownOn)
                : platform.Outputs.Screens.Select(s => s.RefreshRate).DefaultIfEmpty(0).Max();
        }
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
        lock (platform.ModalLock)
        {
            foreach (var blocker in blockers)
            {
                blocker.blockedWindows.Remove(this);
            }

            blockers.Clear();
        }

        if (platform.IsApplication)
        {
            Input.DragDrop.UnregisterNativeDropTarget(window);
        }

        UIContext.UIApplication.RemoveWindow(window);
        window.SetHandle(IntPtr.Zero);

        platform.Queue(() =>
        {
            platform.Seat.Forget(this);
            platform.Outputs.Changed -= UpdateDisplayRefreshRate;
            exported?.Destroy();
            toplevelSession?.Destroy();
            dialog?.Destroy();
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
        if (inputBlocked)
        {
            return;
        }

        UIContext.UIApplication.SetActiveWindow(window);
        FocusManager.EnterWindow(window);
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
