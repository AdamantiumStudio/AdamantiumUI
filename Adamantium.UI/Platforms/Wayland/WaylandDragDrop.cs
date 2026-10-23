using System;
using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>Drag and drop through the compositor: every mouse drag goes to it, since only it knows which window the
/// pointer is over. An in-app drag offers a private type only.</summary>
internal sealed class WaylandDragDrop : INativeDragDrop
{
    private const int AskInterval = 50;

    private static readonly string OwnType = $"application/x-adamantium-drag;pid={Environment.ProcessId}";

    private static readonly string[] ForeignFormats =
        [DataFormats.Files, DataFormats.Text, DataFormats.Html, DataFormats.Rtf, DataFormats.Image];

    private readonly WaylandPlatform platform;
    private readonly WaylandDataDevice device;
    private readonly Dictionary<IWindow, INativeDropSink> targets = new();
    private readonly object _targetsLock = new();

    private WaylandOffer over;
    private WaylandWindowWorker windowDragOver;
    private IWindow overWindow;
    private WaylandWindowWorker overWorker;
    private INativeDropSink overSink;
    private IDataPackage overData;
    private DragDropEffects overAllowed;
    private PixelPoint overPoint;
    private uint overSerial;
    private DragDropEffects answered;
    private long nextAsk;

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
        lock (_targetsLock)
        {
            targets[window] = sink;
        }
    }

    public void UnregisterDropTarget(IWindow window)
    {
        lock (_targetsLock)
        {
            targets.Remove(window);
        }
    }

    private void OnEnter(uint serial, WlSurface surface, double x, double y, WaylandOffer offer)
    {
        windowDragOver = null;
        if (offer != null && offer.Has(WaylandWindowDrag.WindowType) && platform.WindowDrag is { IsActive: true } windowDrag)
        {
            over = null;
            overSink = null;
            windowDragOver = platform.Find(surface);
            if (windowDragOver?.InputWindow == null)
            {
                windowDragOver = null;
                offer.Proxy.Accept(serial, null);
                return;
            }

            offer.Proxy.Accept(serial, WaylandWindowDrag.WindowType);
            offer.Proxy.SetActions(WlDataDeviceManager.DndAction.Move, WlDataDeviceManager.DndAction.Move);
            windowDrag.Over(windowDragOver, new Vector2(x, y));
            return;
        }

        var worker = platform.Find(surface);
        var window = worker?.InputWindow;
        INativeDropSink sink = null;
        if (window != null)
        {
            lock (_targetsLock)
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
        if (windowDragOver != null)
        {
            platform.WindowDrag.Over(windowDragOver, new Vector2(x, y));
            return;
        }

        if (overSink == null)
        {
            return;
        }

        overPoint = PointOf(overWorker, x, y);
        Answer(overSink.DragOver(overWindow, overPoint, platform.Seat.Modifiers, overAllowed));
    }

    private void OnLeave()
    {
        if (windowDragOver != null)
        {
            windowDragOver = null;
            platform.WindowDrag.Left();
            return;
        }

        var sink = overSink;
        var window = overWindow;
        Forget();
        sink?.DragLeave(window);
    }

    private void OnDrop()
    {
        if (windowDragOver != null)
        {
            windowDragOver = null;
            var dropped = device.TakeDragOffer();
            dropped?.Proxy.Finish();
            dropped?.Destroy();
            platform.Display.Flush();
            return;
        }

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

    /// <summary>Asks the window under a resting drag for its effect again.</summary>
    internal void AskAgain()
    {
        if (overSink != null && Environment.TickCount64 >= nextAsk)
        {
            Answer(overSink.DragOver(overWindow, overPoint, platform.Seat.Modifiers, overAllowed));
        }
    }

    /// <summary>How long the event loop may wait before <see cref="AskAgain"/>; -1 with no drag over us.</summary>
    internal int AskTimeout() => overSink == null ? -1 : (int)Math.Max(0, nextAsk - Environment.TickCount64);

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

    private IDataPackage ForeignPackage(WaylandOffer offer)
    {
        var package = new DataPackage();
        foreach (var format in ForeignFormats)
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

    private static WlDataDeviceManager.DndAction ToAction(DragDropEffects effect) =>
        (effect & DragDropEffects.Move) != 0 ? WlDataDeviceManager.DndAction.Move
        : (effect & DragDropEffects.Copy) != 0 ? WlDataDeviceManager.DndAction.Copy
        : WlDataDeviceManager.DndAction.None;

    private static DragDropEffects ToEffects(WlDataDeviceManager.DndAction actions) =>
        ((actions & WlDataDeviceManager.DndAction.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None) |
        ((actions & WlDataDeviceManager.DndAction.Move) != 0 ? DragDropEffects.Move : DragDropEffects.None);
}
