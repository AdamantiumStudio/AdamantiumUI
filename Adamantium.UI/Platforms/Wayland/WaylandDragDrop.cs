using System;
using System.Collections.Generic;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>
/// Drag and drop through the compositor's wl_data_device: drops onto every window of ours, and drags out of them.
/// <para>
/// Under Wayland a drag the application carried itself could not leave the window it started in: the pointer stays
/// with that window until the button goes up, and nothing says which window it is over. So every mouse drag goes to the
/// compositor, which tells the window under the pointer - ours or another application's - and moves the drag's icon
/// above everything. A drag meant for this application alone offers a type of its own only, which no other client
/// takes; on a window of ours it arrives with its live payload, as on Windows.
/// </para>
/// <para>
/// The compositor shows the action the target settled on, and lets go of a drop nobody accepts. The drop sink answers a
/// move with the effect it settled on a loop pass before, so the answer is asked for again while the pointer rests, and
/// the compositor has it before the button goes up.
/// </para>
/// </summary>
internal sealed class WaylandDragDrop : INativeDragDrop
{
    // How often the effect is asked for again while the pointer rests over a window of ours.
    private const int AskInterval = 50;

    // A drag of this process: no other client offers it, and no other client knows it.
    private static readonly string OwnType = $"application/x-adamantium-drag;pid={Environment.ProcessId}";

    private readonly WaylandPlatform platform;
    private readonly WaylandDataDevice device;
    private readonly Dictionary<IWindow, INativeDropSink> targets = new();

    // The drag over one of our windows: event thread only.
    private WaylandOffer over;
    private IWindow overWindow;
    private WaylandWindowWorker overWorker;
    private INativeDropSink overSink;
    private IDataPackage overData;
    private DragDropEffects overAllowed;
    private PixelPoint overPoint;
    private uint overSerial;
    private DragDropEffects answered;
    private long nextAsk;

    // The drag this application started. Begun on the loop thread, run on the event thread.
    private volatile bool dragActive;
    private IDataPackage dragData;
    private DragDropEffects dragAllowed;
    private Action<DragDropEffects> dragCompleted;
    private WlDataSource dragSource;
    private WlSurface dragIcon;
    private WpViewport dragIconViewport;
    private WlDataDeviceManager.DndAction dragAction;

    public WaylandDragDrop(IApplicationPlatform platform)
    {
        this.platform = (WaylandPlatform)platform;
        device = this.platform.DataDevice;
        if (device == null)
        {
            return;
        }

        device.DragEntered += OnEnter;
        device.DragMotion += OnMotion;
        device.DragLeft += OnLeave;
        device.DragDropped += OnDrop;
        this.platform.Seat.PointerReleased += OnPointerReleased;
        this.platform.DragDrop = this;
    }

    public bool IsAvailable => device != null;

    public bool CarriesInAppDrags => true;

    public void RegisterDropTarget(IWindow window, INativeDropSink sink)
    {
        lock (targets)
        {
            targets[window] = sink;
        }
    }

    public void UnregisterDropTarget(IWindow window)
    {
        lock (targets)
        {
            targets.Remove(window);
        }
    }

    #region Drop target

    private void OnEnter(uint serial, WlSurface surface, double x, double y, WaylandOffer offer)
    {
        var worker = platform.Find(surface);
        var window = worker?.Window;
        INativeDropSink sink = null;
        if (window != null)
        {
            lock (targets)
            {
                sink = targets.GetValueOrDefault(window);
            }
        }

        over = offer;
        overSerial = serial;
        answered = (DragDropEffects)(-1);
        if (offer == null || sink == null)
        {
            offer?.Proxy.Accept(serial, null);
            overSink = null;
            return;
        }

        var own = dragActive && offer.Has(OwnType);
        overWindow = window;
        overWorker = worker;
        overSink = sink;
        overData = own ? dragData : ForeignPackage(offer);
        overAllowed = own ? dragAllowed : ToEffects(offer.SourceActions == WlDataDeviceManager.DndAction.None
            ? WlDataDeviceManager.DndAction.Copy
            : offer.SourceActions);
        overPoint = PointOf(worker, x, y);
        Answer(sink.DragEnter(window, overData, overPoint, platform.Seat.Modifiers, overAllowed));
    }

    private void OnMotion(double x, double y)
    {
        if (overSink == null)
        {
            return;
        }

        overPoint = PointOf(overWorker, x, y);
        Answer(overSink.DragOver(overWindow, overPoint, platform.Seat.Modifiers, overAllowed));
    }

    private void OnLeave()
    {
        var sink = overSink;
        var window = overWindow;
        Forget();
        sink?.DragLeave(window);
    }

    private void OnDrop()
    {
        var sink = overSink;
        var offer = device.TakeDragOffer();
        if (sink == null || offer == null)
        {
            offer?.Destroy();
            Forget();
            return;
        }

        var effect = sink.Drop(overWindow, overData, overPoint, platform.Seat.Modifiers, overAllowed);
        Forget();

        // The drop reaches the loop thread after this; the offer stays until it has, for the bytes it reads from it,
        // and then says what became of them.
        Threading.Dispatcher.CurrentDispatcher?.Post(() => platform.Invoke(() =>
        {
            if (effect != DragDropEffects.None && offer.Proxy.IsAlive)
            {
                offer.Proxy.Finish();
            }

            offer.Destroy();
            platform.Display.Flush();
        }));
    }

    /// <summary>Asks the window under a resting drag again for its effect: what it answered last was settled on
    /// the move before.</summary>
    internal void AskAgain()
    {
        if (overSink != null && Environment.TickCount64 >= nextAsk)
        {
            Answer(overSink.DragOver(overWindow, overPoint, platform.Seat.Modifiers, overAllowed));
        }
    }

    /// <summary>How long the event loop may wait before <see cref="AskAgain"/>; -1 with no drag over us.</summary>
    internal int AskTimeout() => overSink == null ? -1 : (int)Math.Max(0, nextAsk - Environment.TickCount64);

    // Tells the compositor what the target settled on: the action it shows, and whether a drop is taken at all.
    private void Answer(DragDropEffects effect)
    {
        nextAsk = Environment.TickCount64 + AskInterval;
        if (over == null || effect == answered)
        {
            return;
        }

        answered = effect;
        var action = ToAction(effect);
        over.Proxy.Accept(overSerial, action == WlDataDeviceManager.DndAction.None ? null : over.FirstOf([OwnType]) ?? over.First());
        over.Proxy.SetActions(action, action);
        platform.Display.Flush();
    }

    private void Forget()
    {
        over = null;
        overWindow = null;
        overWorker = null;
        overSink = null;
        overData = null;
    }

    // Another client's offer, read when a target asks for a format: the bytes cross only for the drop that wants them.
    private IDataPackage ForeignPackage(WaylandOffer offer)
    {
        var package = new DataPackage();
        foreach (var format in new[] { DataFormats.Files, DataFormats.Text, DataFormats.Html, DataFormats.Rtf, DataFormats.Image })
        {
            var types = WaylandDataDevice.TypesOf(format);
            if (offer.FirstOf(types) != null)
            {
                package.SetDeferred(format, () => device.Read(offer, types) is { } bytes ? WaylandDataDevice.Decode(format, bytes) : null);
            }
        }

        return package;
    }

    private static PixelPoint PointOf(WaylandWindowWorker worker, double x, double y) =>
        new(x * worker.Scale, y * worker.Scale);

    #endregion

    #region Drag source

    public bool BeginDrag(IWindow source, IDataPackage data, DragDropEffects allowed, DragGhostImage ghost,
        Action<DragDropEffects> completed, bool external)
    {
        var worker = source != null ? platform.Find(source.Handle) : null;
        if (device == null || dragActive || worker == null || data == null)
        {
            return false;
        }

        dragActive = true;
        dragData = data;
        dragAllowed = allowed;
        dragCompleted = completed;
        var offers = external ? WaylandDataDevice.Offers(data) : [];
        platform.Invoke(() => StartDrag(worker, offers, allowed, ghost));
        return true;
    }

    private void StartDrag(WaylandWindowWorker worker, List<(string Type, Func<byte[]> Produce)> offers, DragDropEffects allowed,
        DragGhostImage ghost)
    {
        // The compositor starts a drag only inside the press that makes it.
        if (worker.Window == null || !platform.Seat.IsPressed)
        {
            EndDrag(DragDropEffects.None);
            return;
        }

        var byType = new Dictionary<string, Func<byte[]>> { [OwnType] = () => [] };

        dragAction = WlDataDeviceManager.DndAction.None;
        dragSource = device.Manager.CreateDataSource();
        dragSource.Send += (type, fd) => WaylandDataDevice.Send(byType.GetValueOrDefault(type), fd);
        dragSource.Action += action => dragAction = action;
        dragSource.Cancelled += () => EndDrag(DragDropEffects.None);
        dragSource.DndFinished += () => EndDrag(ToEffects(dragAction));
        dragSource.Offer(OwnType);
        foreach (var (type, produce) in offers)
        {
            if (byType.TryAdd(type, produce))
            {
                dragSource.Offer(type);
            }
        }

        dragSource.SetActions(ToAction(allowed & DragDropEffects.Copy) | ToAction(allowed & DragDropEffects.Move));

        dragIcon = ghost.IsEmpty ? null : platform.Compositor.CreateSurface();
        device.Device.StartDrag(dragSource, worker.Surface, dragIcon, platform.Seat.PressSerial);
        if (dragIcon != null)
        {
            DrawIcon(ghost, worker.Scale);
        }

        platform.Display.Flush();
    }

    // The ghost as the drag's icon: its top-left at the pointer, moved by the offset the cursor has inside it.
    private void DrawIcon(DragGhostImage ghost, double scale)
    {
        var buffer = WaylandShm.CreateBuffer(platform, ghost.PremultipliedBgra, ghost.Width, ghost.Height);
        if (buffer == null)
        {
            return;
        }

        if (platform.Viewporter == null)
        {
            scale = 1;
        }
        else
        {
            dragIconViewport = platform.Viewporter.GetViewport(dragIcon);
            dragIconViewport.SetDestination(Math.Max(1, (int)Math.Round(ghost.Width / scale)),
                Math.Max(1, (int)Math.Round(ghost.Height / scale)));
        }

        dragIcon.Attach(buffer, 0, 0);
        if ((ghost.OffsetX != 0 || ghost.OffsetY != 0) && dragIcon.Version >= 5)
        {
            dragIcon.Offset(-(int)Math.Round(ghost.OffsetX / scale), -(int)Math.Round(ghost.OffsetY / scale));
        }

        dragIcon.DamageBuffer(0, 0, ghost.Width, ghost.Height);
        dragIcon.Commit();
        buffer.Destroy();
    }

    // A release the pointer still hears means the compositor never took the drag: it would have had the pointer.
    private void OnPointerReleased()
    {
        if (dragSource != null)
        {
            EndDrag(DragDropEffects.None);
        }
    }

    private void EndDrag(DragDropEffects effect)
    {
        if (!dragActive)
        {
            return;
        }

        dragSource?.Destroy();
        dragIconViewport?.Destroy();
        dragIcon?.Destroy();
        dragSource = null;
        dragIconViewport = null;
        dragIcon = null;
        platform.Display.Flush();

        var completed = dragCompleted;
        dragCompleted = null;
        dragData = null;
        dragActive = false;
        Threading.Dispatcher.CurrentDispatcher?.Post(() => completed?.Invoke(effect));
    }

    #endregion

    // Wayland has no link: what a drag allows of it is not offered.
    private static WlDataDeviceManager.DndAction ToAction(DragDropEffects effect) =>
        (effect & DragDropEffects.Move) != 0 ? WlDataDeviceManager.DndAction.Move
        : (effect & DragDropEffects.Copy) != 0 ? WlDataDeviceManager.DndAction.Copy
        : WlDataDeviceManager.DndAction.None;

    private static DragDropEffects ToEffects(WlDataDeviceManager.DndAction actions) =>
        ((actions & WlDataDeviceManager.DndAction.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None) |
        ((actions & WlDataDeviceManager.DndAction.Move) != 0 ? DragDropEffects.Move : DragDropEffects.None);
}
