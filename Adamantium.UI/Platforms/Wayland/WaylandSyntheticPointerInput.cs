using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Adamantium.DBus;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Input.Raw;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>Fingers through GNOME's remote desktop. A Wayland client does not know where its window is, so the pointer
/// is moved over the monitors until the window says it is under it, and every landing is checked the same way.</summary>
internal sealed class WaylandSyntheticPointerInput : ISyntheticPointerInput, IDisposable
{
    private const int MaxProbes = 1500;
    private const double Nearby = 2;
    private const double Sideways = 40;

    private static readonly TimeSpan ProbeQuiet = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan SettleLimit = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FrameGap = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan TouchscreenWait = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan TouchscreenLimit = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(15);

    private readonly WaylandPlatform platform;
    private readonly object _desktopLock = new();
    private readonly AutoResetEvent pointerChanged = new(false);
    private readonly Timer idleTimer;
    private GnomeRemoteDesktop desktop;
    private WaylandSyntheticPlacement placement;

    public WaylandSyntheticPointerInput(IApplicationPlatform platform)
    {
        this.platform = (WaylandPlatform)platform;
        this.platform.Seat.PointerChanged += () => pointerChanged.Set();
        idleTimer = new Timer(_ => CloseDesktop());
    }

    /// <summary>Probe points over a monitor, from its middle outwards, never at its edges or corners, where GNOME opens
    /// the overview.</summary>
    internal static IEnumerable<(double X, double Y)> Spiral(int width, int height, int step = 120, int margin = 48)
    {
        var cx = width / 2.0 + step / 4.0;
        var cy = height / 2.0 + step / 4.0;
        var rings = Math.Max(width, height) / step / 2 + 1;
        for (var ring = 0; ring <= rings; ring++)
        {
            for (var i = -ring; i <= ring; i++)
            {
                for (var j = -ring; j <= ring; j++)
                {
                    var x = cx + i * step;
                    var y = cy + j * step;
                    if (Math.Max(Math.Abs(i), Math.Abs(j)) == ring &&
                        x >= margin && y >= margin && x <= width - margin && y <= height - margin)
                    {
                        yield return (x, y);
                    }
                }
            }
        }
    }

    public void Inject(IWindow window, PointerType type, IReadOnlyList<IReadOnlyList<SyntheticPointerFrame>> frames)
    {
        if (type != PointerType.Touch)
        {
            throw new NotSupportedException("GNOME's remote desktop takes fingers only, no pen.");
        }

        var worker = platform.Find(window.Handle)
                     ?? throw new InvalidOperationException("The window has no Wayland surface to press.");
        lock (_desktopLock)
        {
            idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
            try
            {
                try
                {
                    InjectLocked(worker, frames);
                }
                catch (DBusException)
                {
                    CloseDesktop();
                    InjectLocked(worker, frames);
                }
            }
            finally
            {
                idleTimer.Change(IdleClose, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        idleTimer.Dispose();
        CloseDesktop();
    }

    private void InjectLocked(WaylandWindowWorker worker, IReadOnlyList<IReadOnlyList<SyntheticPointerFrame>> frames)
    {
        if (platform.Seat.IsPressed)
        {
            throw new InvalidOperationException("A mouse button is held: the pointer stays with the window it was pressed in, so no landing can be checked.");
        }

        if (desktop?.IsClosed == true)
        {
            CloseDesktop();
        }

        desktop ??= GnomeRemoteDesktop.Open() ?? throw new InvalidOperationException(
            "Synthetic input on Wayland needs GNOME's remote desktop (org.gnome.Mutter.RemoteDesktop), which this desktop does not offer.");

        var landings = frames[0].Select(contact => Local(worker, contact.Screen)).ToList();
        Place(worker, landings);
        EnsureTouchscreen(worker, landings[0]);
        foreach (var landing in landings)
        {
            if (!Lands(placement, landing))
            {
                throw Covered(landing);
            }
        }

        Play(frames, worker);
    }

    private void Place(WaylandWindowWorker worker, IEnumerable<Vector2> landings)
    {
        foreach (var landing in landings)
        {
            if (placement?.Worker == worker && Lands(placement, landing))
            {
                continue;
            }

            if (placement?.Worker == worker && worker.Window is { } window &&
                Lands(placement, new Vector2(window.ClientWidth / 2, window.ClientHeight / 2)))
            {
                throw Covered(landing);
            }

            placement = Locate(worker) ?? throw new InvalidOperationException("The window was not found on any monitor.");
            if (!Lands(placement, landing))
            {
                throw Covered(landing);
            }
        }
    }

    private void Play(IReadOnlyList<IReadOnlyList<SyntheticPointerFrame>> frames, WaylandWindowWorker worker)
    {
        HashSet<uint> down = [];
        try
        {
            foreach (var frame in frames)
            {
                for (var slot = 0u; slot < frame.Count; slot++)
                {
                    var contact = frame[(int)slot];
                    var at = placement.ToStream(Local(worker, contact.Screen));
                    switch (contact.Kind)
                    {
                        case RawPointerEventType.Down:
                            desktop.TouchDown(placement.Monitor.Stream, slot, at.X, at.Y);
                            down.Add(slot);
                            break;
                        case RawPointerEventType.Move:
                            desktop.TouchMotion(placement.Monitor.Stream, slot, at.X, at.Y);
                            break;
                        case RawPointerEventType.Up:
                            desktop.TouchUp(slot);
                            down.Remove(slot);
                            break;
                    }
                }

                Thread.Sleep(FrameGap);
            }
        }
        finally
        {
            foreach (var slot in down)
            {
                try
                {
                    desktop.TouchUp(slot);
                }
                catch (DBusException)
                {
                }
            }
        }
    }

    private void EnsureTouchscreen(WaylandWindowWorker worker, Vector2 landing)
    {
        if (platform.Seat.HasTouch)
        {
            return;
        }

        platform.Seat.TouchSuppressed = true;
        try
        {
            var at = placement.ToStream(landing);
            desktop.TouchDown(placement.Monitor.Stream, 0, at.X, at.Y);
            desktop.TouchUp(0);
            var deadline = Environment.TickCount64 + (long)TouchscreenLimit.TotalMilliseconds;
            while (!platform.Seat.HasTouch)
            {
                if (Environment.TickCount64 > deadline)
                {
                    throw new InvalidOperationException("The desktop gave the seat no touchscreen.");
                }

                Thread.Sleep(FrameGap);
            }

            Thread.Sleep(TouchscreenWait);
        }
        finally
        {
            platform.Seat.TouchSuppressed = false;
        }
    }

    private static Vector2 Local(WaylandWindowWorker worker, PixelPoint screen) =>
        new(screen.X / worker.Scale, screen.Y / worker.Scale);

    private static InvalidOperationException Covered(Vector2 local) =>
        new($"({local.X:0}, {local.Y:0}) of the window is under another one: the system would press that.");

    private bool Lands(WaylandSyntheticPlacement at, Vector2 local)
    {
        var point = at.ToStream(local);
        return Probe(at.Monitor, point.X, point.Y, at.Worker) is { } seen &&
               Math.Abs(seen.X - local.X) <= Nearby && Math.Abs(seen.Y - local.Y) <= Nearby;
    }

    private WaylandSyntheticPlacement Locate(WaylandWindowWorker worker)
    {
        var probes = 0;
        foreach (var monitor in desktop.Monitors)
        {
            foreach (var (x, y) in Spiral(monitor.Width, monitor.Height))
            {
                if (++probes > MaxProbes)
                {
                    return null;
                }

                if (Probe(monitor, x, y, worker) is not { } first)
                {
                    continue;
                }

                foreach (var dx in (double[])[Sideways, -Sideways])
                {
                    if (Probe(monitor, x + dx, y, worker) is { } second && second.X != first.X)
                    {
                        var scale = (second.X - first.X) / dx;
                        return new WaylandSyntheticPlacement(worker, monitor,
                            new Vector2(x - first.X / scale, y - first.Y / scale), scale);
                    }
                }
            }
        }

        return null;
    }

    private Vector2? Probe(GnomeMonitorStream monitor, double x, double y, WaylandWindowWorker worker)
    {
        var before = platform.Seat.Pointer;
        pointerChanged.Reset();
        desktop.MovePointer(monitor.Stream, x + (x + 1 < monitor.Width ? 1 : -1), y);
        desktop.MovePointer(monitor.Stream, x, y);
        var deadline = Environment.TickCount64 + (long)SettleLimit.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (pointerChanged.WaitOne(ProbeQuiet))
            {
                continue;
            }

            if (platform.Seat.Pointer.Sequence > before.Sequence || before.Window == null)
            {
                break;
            }
        }

        var now = platform.Seat.Pointer;
        return now.Sequence > before.Sequence && now.Window == worker ? now.Position : null;
    }

    private void CloseDesktop()
    {
        lock (_desktopLock)
        {
            desktop?.Dispose();
            desktop = null;
            placement = null;
        }
    }
}
