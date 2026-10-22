using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The application on a Wayland compositor: the connection, its globals, and the loop over its socket. Desktop
/// coordinates are a window's own: Wayland gives a client no position on the desktop.</summary>
public sealed unsafe class WaylandPlatform : IApplicationPlatform, Linux.IPortalParent
{
    private readonly Thread uiThread;
    private readonly int signalFd;
    private readonly Dictionary<nint, WaylandWindowWorker> windows = new();
    private readonly object _windowsLock = new();

    public WaylandPlatform()
    {
        uiThread = Thread.CurrentThread;
        Display = WaylandDisplay.Connect();

        var registry = Display.GetRegistry();
        WlSeat seat = null;
        WlDataDeviceManager dataDevices = null;
        ZwpPrimarySelectionDeviceManagerV1 primarySelections = null;
        ZwpTabletManagerV2 tablets = null;
        XdgSessionManagerV1 sessions = null;
        ZwpTextInputManagerV3 textInputs = null;
        registry.Global += (name, @interface, version) =>
        {
            switch (@interface)
            {
                case WlCompositor.InterfaceName:
                    Compositor = registry.Bind<WlCompositor>(name, Math.Min(version, 6));
                    break;
                case XdgWmBase.InterfaceName:
                    WmBase = registry.Bind<XdgWmBase>(name, Math.Min(version, 6));
                    break;
                case WlSeat.InterfaceName when seat == null:
                    seat = registry.Bind<WlSeat>(name, Math.Min(version, 9));
                    break;
                case WpViewporter.InterfaceName:
                    Viewporter = registry.Bind<WpViewporter>(name, 1);
                    break;
                case WpFractionalScaleManagerV1.InterfaceName:
                    FractionalScale = registry.Bind<WpFractionalScaleManagerV1>(name, 1);
                    break;
                case WpCursorShapeManagerV1.InterfaceName:
                    CursorShapes = registry.Bind<WpCursorShapeManagerV1>(name, 1);
                    break;
                case ZxdgDecorationManagerV1.InterfaceName:
                    Decorations = registry.Bind<ZxdgDecorationManagerV1>(name, 1);
                    break;
                case WlShm.InterfaceName:
                    Shm = registry.Bind<WlShm>(name, 1);
                    break;
                case WlSubcompositor.InterfaceName:
                    Subcompositor = registry.Bind<WlSubcompositor>(name, 1);
                    break;
                case XdgToplevelDragManagerV1.InterfaceName:
                    ToplevelDrags = registry.Bind<XdgToplevelDragManagerV1>(name, 1);
                    break;
                case ZwpPrimarySelectionDeviceManagerV1.InterfaceName:
                    primarySelections = registry.Bind<ZwpPrimarySelectionDeviceManagerV1>(name, 1);
                    break;
                case ZwpRelativePointerManagerV1.InterfaceName:
                    RelativePointers = registry.Bind<ZwpRelativePointerManagerV1>(name, 1);
                    break;
                case ZwpTextInputManagerV3.InterfaceName:
                    textInputs = registry.Bind<ZwpTextInputManagerV3>(name, 1);
                    break;
                case XdgSessionManagerV1.InterfaceName:
                    sessions = registry.Bind<XdgSessionManagerV1>(name, 1);
                    break;
                case ZwpTabletManagerV2.InterfaceName:
                    tablets = registry.Bind<ZwpTabletManagerV2>(name, 1);
                    break;
                case ZxdgExporterV2.InterfaceName:
                    Exporter = registry.Bind<ZxdgExporterV2>(name, 1);
                    break;
                case ZwpPointerGesturesV1.InterfaceName:
                    PointerGestures = registry.Bind<ZwpPointerGesturesV1>(name, 1);
                    break;
                case ZwpPointerConstraintsV1.InterfaceName:
                    PointerConstraints = registry.Bind<ZwpPointerConstraintsV1>(name, 1);
                    break;
                case XdgActivationV1.InterfaceName:
                    Activation = registry.Bind<XdgActivationV1>(name, 1);
                    break;
                case WlDataDeviceManager.InterfaceName:
                    dataDevices = registry.Bind<WlDataDeviceManager>(name, Math.Min(version, 3));
                    break;
                case WlOutput.InterfaceName:
                    Outputs.Add(name, registry.Bind<WlOutput>(name, Math.Min(version, 4)));
                    break;
                case ZxdgOutputManagerV1.InterfaceName:
                    Outputs.SetManager(registry.Bind<ZxdgOutputManagerV1>(name, Math.Min(version, 3)));
                    break;
            }
        };
        registry.GlobalRemove += Outputs.Remove;
        Display.Roundtrip();

        if (Compositor == null || WmBase == null)
        {
            throw new PlatformNotSupportedException(
                "The Wayland compositor offers no wl_compositor or xdg_wm_base, so it can show no window.");
        }

        WmBase.Ping += serial => WmBase.Pong(serial);

        signalFd = PosixInterop.EventFd(0, PosixInterop.EventFdNonBlock | PosixInterop.EventFdCloseOnExec);
        Seat = new WaylandSeat(this, seat);

        Display.Roundtrip();

        if (dataDevices != null && seat != null)
        {
            DataDevice = new WaylandDataDevice(this, dataDevices, seat);
            WindowDrag = new WaylandWindowDrag(this, DataDevice);
            Clipboard.Current = new WaylandClipboard(this, DataDevice);
        }

        if (sessions != null)
        {
            Session = new WaylandSession(sessions, AppId);
        }
        if (textInputs != null && seat != null)
        {
            TextInput = new WaylandTextInput(this, textInputs, seat);
        }

        if (tablets != null && seat != null)
        {
            Tablet = new WaylandTablet(this, Seat, tablets.GetTabletSeat(seat));
        }

        if (primarySelections != null && seat != null)
        {
            Clipboard.PrimarySelection = new WaylandPrimarySelection(this, primarySelections, seat);
        }

        Cursor.Platform = Seat;
        Mouse.Platform = Seat;
        Keyboard.Platform = Seat;
        PlatformSettings.Platform = Seat;
    }

    public WaylandDisplay Display { get; }

    public WlCompositor Compositor { get; private set; }

    public XdgWmBase WmBase { get; private set; }

    public WpViewporter Viewporter { get; private set; }

    public WpFractionalScaleManagerV1 FractionalScale { get; private set; }

    public WpCursorShapeManagerV1 CursorShapes { get; private set; }

    public ZxdgDecorationManagerV1 Decorations { get; private set; }

    public XdgToplevelDragManagerV1 ToplevelDrags { get; private set; }

    public XdgActivationV1 Activation { get; private set; }

    public ZwpRelativePointerManagerV1 RelativePointers { get; private set; }

    public ZwpPointerConstraintsV1 PointerConstraints { get; private set; }

    public ZwpPointerGesturesV1 PointerGestures { get; private set; }

    public ZxdgExporterV2 Exporter { get; private set; }

    /// <summary>What the compositor knows the application by.</summary>
    internal static string AppId => System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "adamantium";

    /// <summary>The session that brings windows back where they were; null where the compositor keeps none.</summary>
    internal WaylandSession Session { get; }

    /// <summary>The input method; null where the compositor has none.</summary>
    internal WaylandTextInput TextInput { get; }

    /// <summary>The seat's drawing tablets; null where the compositor has none.</summary>
    internal WaylandTablet Tablet { get; }

    /// <summary>The monitors.</summary>
    internal WaylandOutputs Outputs { get; } = new();

    public WlShm Shm { get; private set; }

    public WlSubcompositor Subcompositor { get; private set; }

    internal WaylandSeat Seat { get; }

    internal WaylandDataDevice DataDevice { get; }

    /// <summary>A window carried in a drag; null where the compositor has no drag and drop.</summary>
    internal WaylandWindowDrag WindowDrag { get; }

    /// <summary>Drag and drop, once the first window has registered with it.</summary>
    internal WaylandDragDrop DragDrop { get; set; }

    public bool IsOnUIThread => uiThread == Thread.CurrentThread;

    public event Action Signaled;

    /// <summary>Waits on the compositor's socket and on <see cref="Signal"/> at once, spinning nowhere.</summary>
    public void Run(CancellationToken token)
    {
        using var wake = token.Register(Signal);
        var fds = stackalloc PosixPollFd[2];

        while (!token.IsCancellationRequested)
        {
            while (!Display.PrepareRead())
            {
                Display.DispatchPending();
            }

            Display.Flush();

            fds[0] = new PosixPollFd { Fd = Display.FileDescriptor, Events = PosixInterop.PollIn };
            fds[1] = new PosixPollFd { Fd = signalFd, Events = PosixInterop.PollIn };
            var ready = PosixInterop.Poll(fds, 2, Sooner(Seat.RepeatTimeout(), DragDrop?.AskTimeout() ?? -1));

            if (ready > 0 && (fds[0].Revents & PosixInterop.PollIn) != 0)
            {
                Display.ReadEvents();
            }
            else
            {
                Display.CancelRead();
            }

            Display.DispatchPending();
            Seat.Flush();

            if (ready > 0 && (fds[1].Revents & PosixInterop.PollIn) != 0)
            {
                ulong count;
                PosixInterop.Read(signalFd, &count, sizeof(ulong));
                Signaled?.Invoke();
            }

            Seat.Repeat();
            DragDrop?.AskAgain();
        }
    }

    private static int Sooner(int a, int b) => a < 0 ? b : b < 0 ? a : Math.Min(a, b);

    public void Signal()
    {
        ulong one = 1;
        PosixInterop.Write(signalFd, &one, sizeof(ulong));
    }

    /// <summary>Zero: a client cannot ask what is on the desktop, so a drag falls back to the client bounds.</summary>
    public IntPtr WindowFromScreenPoint(PixelPoint point) => IntPtr.Zero;

    internal void Register(WaylandWindowWorker worker, nint surface)
    {
        lock (_windowsLock)
        {
            windows[surface] = worker;
        }
    }

    internal void Unregister(nint surface)
    {
        lock (_windowsLock)
        {
            windows.Remove(surface);
        }
    }

    /// <summary>The window whose surface an event names; null for a surface that is not a window of ours.</summary>
    internal WaylandWindowWorker Find(WlSurface surface) => surface == null ? null : Find(surface.Handle);

    internal WaylandWindowWorker Find(nint surface)
    {
        lock (_windowsLock)
        {
            return windows.GetValueOrDefault(surface);
        }
    }

    string Linux.IPortalParent.PortalParentOf(IntPtr owner)
    {
        if (owner == IntPtr.Zero)
        {
            owner = UIAppContext.Current?.UIContext?.UIApplication?.ActiveWindow?.Handle ?? IntPtr.Zero;
        }

        var handle = Find(owner)?.ExportHandle();
        return handle == null ? string.Empty : "wayland:" + handle;
    }

    /// <summary>Runs on the thread that dispatches the compositor's events: at once from it, queued from any other.</summary>
    internal void Invoke(Action action)
    {
        if (IsOnUIThread)
        {
            action();
        }
        else
        {
            _ = Threading.Dispatcher.CurrentDispatcher?.InvokeAsync(action);
        }
    }

    /// <summary>Raises on the loop thread what came in on this one, in the order it came - as the Win32 worker does
    /// with its messages: the visual tree is not to be touched from here.</summary>
    internal static void DispatchInput(Action raise)
    {
        var dispatcher = Threading.Dispatcher.CurrentDispatcher;
        if (dispatcher != null)
        {
            dispatcher.Post(raise);
        }
        else
        {
            raise();
        }
    }

    public static void Initialize(IContainerRegistry resolver)
    {
        resolver.RegisterSingleton<IApplicationPlatform, WaylandPlatform>();
        resolver.RegisterSingleton<IDragGhost, WaylandDragGhost>();
        resolver.RegisterSingleton<INativeDragDrop, WaylandDragDrop>();
        resolver.RegisterSingleton<ISyntheticPointerInput, WaylandSyntheticPointerInput>();
    }
}
