using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>
/// The application on a Wayland compositor: the connection, the globals every window is made from, and the loop that
/// waits on the compositor's socket. The thread that runs the loop is the one every event arrives on - pointer, keys,
/// a window's configure - and from there it goes to the loop thread like a Win32 message would.
/// <para>
/// No position on the desktop exists under Wayland: a client never learns where its window is, nor places one. The
/// desktop coordinates the framework asks for are the window's own, its client corner at the origin.
/// </para>
/// </summary>
public sealed unsafe class WaylandPlatform : IApplicationPlatform
{
    private readonly Thread uiThread;
    private readonly int signalFd;
    private readonly Dictionary<nint, WaylandWindowWorker> windows = new();

    public WaylandPlatform()
    {
        uiThread = Thread.CurrentThread;
        Display = WaylandDisplay.Connect();

        var registry = Display.GetRegistry();
        WlSeat seat = null;
        WlDataDeviceManager dataDevices = null;
        registry.Global += (name, @interface, version) =>
        {
            switch (@interface)
            {
                // wl_surface 6 says which scale it wants drawn at.
                case WlCompositor.InterfaceName:
                    Compositor = registry.Bind<WlCompositor>(name, Math.Min(version, 6));
                    break;
                case XdgWmBase.InterfaceName:
                    WmBase = registry.Bind<XdgWmBase>(name, Math.Min(version, 6));
                    break;
                // wl_pointer 8 counts the wheel in 120ths of a notch, as Windows does.
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
                case WlDataDeviceManager.InterfaceName:
                    dataDevices = registry.Bind<WlDataDeviceManager>(name, Math.Min(version, 3));
                    break;
            }
        };
        Display.Roundtrip();

        if (Compositor == null || WmBase == null)
        {
            throw new PlatformNotSupportedException(
                "The Wayland compositor offers no wl_compositor or xdg_wm_base, so it can show no window.");
        }

        // Unanswered, the compositor takes the application for hung and offers to end it.
        WmBase.Ping += serial => WmBase.Pong(serial);

        signalFd = LinuxInterop.eventfd(0, LinuxInterop.EventFdNonBlock | LinuxInterop.EventFdCloseOnExec);
        Seat = new WaylandSeat(this, seat);

        // The seat's devices and the keyboard's keymap, before the first window wants them.
        Display.Roundtrip();

        // The OS clipboard in place of the in-process one, and drag and drop, where the compositor has them to give.
        if (dataDevices != null && seat != null)
        {
            DataDevice = new WaylandDataDevice(this, dataDevices, seat);
            Clipboard.Current = new WaylandClipboard(this, DataDevice);
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

    public WlShm Shm { get; private set; }

    public WlSubcompositor Subcompositor { get; private set; }

    internal WaylandSeat Seat { get; }

    internal WaylandDataDevice DataDevice { get; }

    /// <summary>Drag and drop, once the first window has registered with it: the loop asks it again for a drop's
    /// effect while the pointer rests.</summary>
    internal WaylandDragDrop DragDrop { get; set; }

    public bool IsOnUIThread => uiThread == Thread.CurrentThread;

    public event Action Signaled;

    /// <summary>
    /// Waits on the compositor's socket and on <see cref="Signal"/> at once, with nothing spinning in between: a still
    /// application takes no time. A held key's repeats are timed by the same wait.
    /// </summary>
    public void Run(CancellationToken token)
    {
        using var wake = token.Register(Signal);
        var fds = stackalloc LinuxInterop.PollFd[2];

        while (!token.IsCancellationRequested)
        {
            // Another thread may have queued events since the last read; libwayland reads only with none queued.
            while (!Display.PrepareRead())
            {
                Display.DispatchPending();
            }

            Display.Flush();

            fds[0] = new LinuxInterop.PollFd { Fd = Display.FileDescriptor, Events = LinuxInterop.PollIn };
            fds[1] = new LinuxInterop.PollFd { Fd = signalFd, Events = LinuxInterop.PollIn };
            var ready = LinuxInterop.poll(fds, 2, Sooner(Seat.RepeatTimeout(), DragDrop?.AskTimeout() ?? -1));

            if (ready > 0 && (fds[0].Revents & LinuxInterop.PollIn) != 0)
            {
                Display.ReadEvents();
            }
            else
            {
                Display.CancelRead();
            }

            Display.DispatchPending();

            if (ready > 0 && (fds[1].Revents & LinuxInterop.PollIn) != 0)
            {
                ulong count;
                LinuxInterop.read(signalFd, &count, sizeof(ulong));
                Signaled?.Invoke();
            }

            Seat.Repeat();
            DragDrop?.AskAgain();
        }
    }

    // The sooner of two poll timeouts, -1 being none.
    private static int Sooner(int a, int b) => a < 0 ? b : b < 0 ? a : Math.Min(a, b);

    public void Signal()
    {
        ulong one = 1;
        LinuxInterop.write(signalFd, &one, sizeof(ulong));
    }

    /// <summary>Zero: a client cannot ask what is on the desktop, so a drag falls back to the client bounds.</summary>
    public IntPtr WindowFromScreenPoint(PixelPoint point) => IntPtr.Zero;

    internal void Register(WaylandWindowWorker worker, nint surface)
    {
        lock (windows)
        {
            windows[surface] = worker;
        }
    }

    internal void Unregister(nint surface)
    {
        lock (windows)
        {
            windows.Remove(surface);
        }
    }

    /// <summary>The window whose surface an event names; null for a surface that is not a window of ours.</summary>
    internal WaylandWindowWorker Find(WlSurface surface) => surface == null ? null : Find(surface.Handle);

    internal WaylandWindowWorker Find(nint surface)
    {
        lock (windows)
        {
            return windows.GetValueOrDefault(surface);
        }
    }

    /// <summary>Runs on the thread that dispatches the compositor's events: at once from it, queued from any other.
    /// An object that has events is destroyed there, never under one of its own.</summary>
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
        if (dispatcher != null) dispatcher.Post(raise);
        else raise();
    }

    public static void Initialize(IContainerRegistry resolver)
    {
        resolver.RegisterSingleton<IApplicationPlatform, WaylandPlatform>();
        resolver.RegisterSingleton<IDragGhost, WaylandDragGhost>();
        resolver.RegisterSingleton<INativeDragDrop, WaylandDragDrop>();
    }
}
