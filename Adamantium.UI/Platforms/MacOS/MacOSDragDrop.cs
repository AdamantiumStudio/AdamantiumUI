using System;
using System.Collections.Generic;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>Drags to and from other applications, through AppKit on the main thread.</summary>
internal sealed class MacOSDragDrop : INativeDragDrop
{
    // Marks a drag of ours: one of nothing other applications take is told to our own windows all the same.
    private static readonly string OwnType = $"com.adamantium.drag.p{Environment.ProcessId}";

    private readonly MacOSPlatform _platform;
    private readonly Dictionary<IWindow, INativeDropSink> _sinks = new();
    private readonly object _lock = new();

    // The drag out, and the view it is from; main thread but for the flag.
    private IDataPackage _data;
    private DragDropEffects _allowed;
    private Action<DragDropEffects> _completed;
    private IntPtr _sourceView;
    private volatile bool _isDragging;

    // What the drag over a window of ours carries; main thread.
    private IDataPackage _over;
    private IWindow _overWindow;

    public MacOSDragDrop(IApplicationPlatform platform) => _platform = (MacOSPlatform)platform;

    public bool IsAvailable => !MacOSPlatform.IsHidden;

    public void RegisterDropTarget(IWindow window, INativeDropSink sink)
    {
        if (!IsAvailable || _platform.WorkerOf(window) is not { } worker)
            return;

        lock (_lock)
            _sinks[window] = sink;
        worker.TakeDrops(this, [OwnType, .. MacOSClipboard.DroppedTypes]);
    }

    public void UnregisterDropTarget(IWindow window)
    {
        INativeDropSink sink;
        lock (_lock)
        {
            if (window == null || !_sinks.Remove(window, out sink))
                return;
        }

        _platform.RunOnMainThread(() =>
        {
            if (ReferenceEquals(_overWindow, window))
                Leave(window, sink);
        });
    }

    /// <summary>A window of ours is closing, with the view of its content: a drag out of it ends.</summary>
    internal void SourceClosed(IntPtr view)
    {
        if (view != IntPtr.Zero && view == _sourceView)
            Complete(DragDropEffects.None);
    }

    public bool BeginDrag(IWindow source, IDataPackage data, DragDropEffects allowed, DragGhostImage ghost,
        Action<DragDropEffects> completed, bool external)
    {
        if (!IsAvailable || _isDragging || _platform.WorkerOf(source) is not { } worker)
            return false;

        _isDragging = true;
        _platform.RunOnMainThread(() =>
        {
            _data = data;
            _allowed = allowed;
            _completed = completed;
            if (!Begin(worker, ghost))
                Complete(DragDropEffects.None);
        });
        return true;
    }

    private bool Begin(MacOSWindowWorker worker, DragGhostImage ghost)
    {
        // Let go of already: what is promised would be kept for nothing.
        if (!worker.IsPressed)
            return false;

        var items = MacOSClipboard.CreateItems(_data, keepsPromises: true);
        if (items.Length == 0)
            items = [NSPasteboard.CreateItem()];
        NSPasteboard.SetData(items[0], [1], OwnType);

        var cursor = NSEvent.MouseLocation;
        var at = _platform.Desktop.ToPixels(cursor.X, cursor.Y);
        var scale = _platform.Desktop.ScaleAt(at.X, at.Y);
        var size = ghost.IsEmpty ? new CGSize(1, 1) : new CGSize(ghost.Width / scale, ghost.Height / scale);
        var image = IntPtr.Zero;
        if (!ghost.IsEmpty)
        {
            var cgImage = CGImage.FromPremultipliedBgra(ghost.PremultipliedBgra, ghost.Width, ghost.Height);
            image = NSImage.FromCGImage(cgImage, size);
            CoreGraphicsInterop.Release(cgImage);
        }

        _sourceView = worker.BeginDrag(this, items, image, size);
        NSObject.Release(image);
        foreach (var item in items)
            NSObject.Release(item);
        return _sourceView != IntPtr.Zero;
    }

    /// <summary>What the drag from a window of ours may do. Another application is not left to decide: what it only copied
    /// would be taken for moved.</summary>
    internal NSDragOperation AllowedOperations(bool withinApplication) => withinApplication
        ? Operations(_allowed)
        : Operations(_allowed) & ~NSDragOperation.Generic;

    /// <summary>The drag from a window of ours ended.</summary>
    internal void Ended(NSDragOperation operation)
    {
        Complete(Effects(operation));
        // What woke the loop meanwhile went to the drag.
        _platform.Signal();
    }

    private void Complete(DragDropEffects effects)
    {
        var completed = _completed;
        _data = null;
        _completed = null;
        _sourceView = IntPtr.Zero;
        _isDragging = false;
        // After the drop, which goes the same way.
        MacOSPlatform.DispatchInput(() => completed?.Invoke(effects));
    }

    /// <summary>A drag came over a window, or goes over it; the answer is what it would do there.</summary>
    internal NSDragOperation Over(IWindow window, IntPtr info, bool entered)
    {
        if (SinkOf(window) is not { } sink)
            return NSDragOperation.None;

        var offered = NSDragging.OperationsOf(info);
        var allowed = Effects(offered);
        // Come over one that a modal window kept from it, too.
        entered |= !ReferenceEquals(_overWindow, window);
        if (entered)
        {
            _over = Read(info);
            _overWindow = window;
        }

        var effects = entered
            ? sink.DragEnter(window, _over, Point, InputModifiers.None, allowed)
            : sink.DragOver(window, Point, InputModifiers.None, allowed);
        return Operation(effects, offered);
    }

    /// <summary>A drag left a window.</summary>
    internal void Leave(IWindow window)
    {
        if (window != null && ReferenceEquals(_overWindow, window) && SinkOf(window) is { } sink)
            Leave(window, sink);
    }

    private void Leave(IWindow window, INativeDropSink sink)
    {
        _over = null;
        _overWindow = null;
        sink.DragLeave(window);
    }

    /// <summary>What a drag carries was dropped on a window; false when it is not taken.</summary>
    internal bool Drop(IWindow window, IntPtr info)
    {
        if (SinkOf(window) is not { } sink)
            return false;

        // A source may have filled it only now.
        var data = Read(info);
        _over = null;
        _overWindow = null;
        return sink.Drop(window, data, Point, InputModifiers.None, Effects(NSDragging.OperationsOf(info))) != DragDropEffects.None;
    }

    // Our own drag yields the live package.
    private IDataPackage Read(IntPtr info) => _data != null && _sourceView != IntPtr.Zero && NSDragging.SourceOf(info) == _sourceView
        ? _data
        : new MacOSClipboard(NSDragging.PasteboardOf(info)).Read();

    private PixelPoint Point
    {
        get
        {
            var cursor = NSEvent.MouseLocation;
            var at = _platform.Desktop.ToPixels(cursor.X, cursor.Y);
            return new PixelPoint((int)Math.Round(at.X), (int)Math.Round(at.Y));
        }
    }

    private INativeDropSink SinkOf(IWindow window)
    {
        lock (_lock)
            return window == null ? null : _sinks.GetValueOrDefault(window);
    }

    /// <summary>What may be done, of what the system offers: the keys held - Option to copy, Command to move, Control to
    /// link - have narrowed it already.</summary>
    internal static DragDropEffects Effects(NSDragOperation operations)
    {
        var effects = DragDropEffects.None;
        if ((operations & NSDragOperation.Copy) != 0)
            effects |= DragDropEffects.Copy;
        if ((operations & NSDragOperation.Link) != 0)
            effects |= DragDropEffects.Link;
        if ((operations & (NSDragOperation.Move | NSDragOperation.Generic)) != 0)
            effects |= DragDropEffects.Move;
        return effects;
    }

    /// <summary>What may be done, as the system has it.</summary>
    internal static NSDragOperation Operations(DragDropEffects effects)
    {
        var operations = NSDragOperation.None;
        if ((effects & DragDropEffects.Copy) != 0)
            operations |= NSDragOperation.Copy;
        if ((effects & DragDropEffects.Link) != 0)
            operations |= NSDragOperation.Link;
        if ((effects & DragDropEffects.Move) != 0)
            operations |= NSDragOperation.Move | NSDragOperation.Generic;
        return operations;
    }

    /// <summary>The one operation an effect is, of those <paramref name="offered"/>.</summary>
    internal static NSDragOperation Operation(DragDropEffects effects, NSDragOperation offered)
    {
        var operations = Operations(effects) & offered;
        foreach (var operation in (NSDragOperation[])[NSDragOperation.Copy, NSDragOperation.Move, NSDragOperation.Generic, NSDragOperation.Link])
        {
            if ((operations & operation) != 0)
                return operation;
        }

        return NSDragOperation.None;
    }
}
