using System;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>A window carried by the compositor in a drag (xdg_toplevel_drag): it rides under the pointer, while our
/// other windows are told where the pointer is over them - what a docking host aims and drops by.</summary>
internal sealed class WaylandWindowDrag
{
    /// <summary>The type a window drag offers: no other client takes it.</summary>
    public static readonly string WindowType = $"application/x-adamantium-window;pid={Environment.ProcessId}";

    private readonly WaylandPlatform platform;
    private readonly WaylandDataDevice device;
    private WlDataSource source;
    private XdgToplevelDragV1 drag;
    private WaylandWindowWorker dragged;

    public WaylandWindowDrag(WaylandPlatform platform, WaylandDataDevice device)
    {
        this.platform = platform;
        this.device = device;
    }

    public bool IsActive => source != null;

    /// <summary>Starts carrying <paramref name="worker"/>'s window, held at <paramref name="grab"/>; false where the
    /// compositor cannot, or no press is there to start it from. The window is attached once the drag runs: Mutter
    /// moves a window already on screen only when it is attached during a drag.</summary>
    public bool Start(WaylandWindowWorker worker, Vector2<double> grab)
    {
        var origin = platform.Seat.PointerWindow;
        if (platform.ToplevelDrags == null || source != null || origin is not { IsLive: true } || !platform.Seat.IsPressed ||
            worker.Toplevel == null)
        {
            return false;
        }

        dragged = worker;
        source = device.Manager.CreateDataSource();
        source.Offer(WindowType);
        source.SetActions(WlDataDeviceManager.DndAction.Move);
        source.Send += (_, fd) => WaylandDataDevice.Send([], fd);
        source.Cancelled += End;
        source.DndFinished += End;
        drag = platform.ToplevelDrags.GetXdgToplevelDrag(source);
        device.Device.StartDrag(source, origin.Surface, null, platform.Seat.PressSerial);
        drag.Attach(worker.Toplevel, (int)Math.Round(grab.X), (int)Math.Round(grab.Y));
        platform.Display.Flush();
        WaylandPlatform.DispatchInput(WindowDragPointer.Begin);
        return true;
    }

    /// <summary>The pointer is over one of our windows at a logical point of it.</summary>
    public void Over(WaylandWindowWorker worker, Vector2<double> position)
    {
        var window = worker.Window;
        WaylandPlatform.DispatchInput(() =>
        {
            WindowDragPointer.Update(window, position);
            (dragged?.Window as WindowBase)?.RaiseWindowMoving();
        });
    }

    /// <summary>The pointer left our windows.</summary>
    public void Left()
    {
        WaylandPlatform.DispatchInput(() =>
        {
            WindowDragPointer.Update(null, default);
            (dragged?.Window as WindowBase)?.RaiseWindowMoving();
        });
    }

    private void End()
    {
        if (source == null)
        {
            return;
        }

        drag.Destroy();
        source.Destroy();
        drag = null;
        source = null;
        platform.Display.Flush();

        var window = dragged?.Window as WindowBase;
        dragged = null;
        WaylandPlatform.DispatchInput(() =>
        {
            window?.RaiseWindowMoveCompleted();
            WindowDragPointer.End();
        });
    }
}
