using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>macOS through AppKit: the application's event loop on the main thread, woken from any thread by an
/// application-defined event.</summary>
public class MacOSPlatform : IApplicationPlatform
{
    private const short WakeSubtype = 0x4144;

    internal readonly object ModalLock = new();

    private readonly int _threadId;
    private readonly List<MacOSWindowWorker> _windows = [];
    private readonly List<MacOSPlatform> _guests = [];
    private readonly MacOSPlatform _host;

    public MacOSPlatform()
    {
        _threadId = Environment.CurrentManagedThreadId;
        var pool = ObjCRuntime.PushAutoreleasePool();
        NSApplication.Launch();
        NSApplication.SetDelegate(NSObject.New(MacOSApplicationDelegate.Class));
        ShowApplicationIcon();
        Desktop = new MacOSDisplays();
        Displays.Platform = Desktop;
        ObjCRuntime.PopAutoreleasePool(pool);
        Cursor.Platform = new MacOSCursors(this);
    }

    /// <summary>The splash window's side of <paramref name="host"/>: the same thread and event loop, with signals and
    /// windows of its own - answered while the application starts, when the application's own signals still wait.</summary>
    internal MacOSPlatform(MacOSPlatform host)
    {
        _host = host;
        _threadId = host._threadId;
        Desktop = host.Desktop;
        lock (host._guests)
            host._guests.Add(this);
    }

    /// <summary>The dispatcher of a splash window's side; null for the application's own.</summary>
    internal Threading.Dispatcher Dispatcher { get; set; }

    /// <summary>Takes a splash window's side away from its host.</summary>
    internal void Disconnect()
    {
        lock (_host._guests)
            _host._guests.Remove(this);
    }

    /// <summary>The monitors, and the way between AppKit's points and the pixels windows are told.</summary>
    internal MacOSDisplays Desktop { get; }

    public bool IsOnUIThread => Environment.CurrentManagedThreadId == _threadId;

    public event Action Signaled;

    public void Run(CancellationToken token)
    {
        using var wake = token.Register(Signal);
        NSApplication.Activate();
        Signal();   // what was asked for while a wait took the wake
        while (!token.IsCancellationRequested)
            AnswerNext(NSDate.DistantFuture, true);
    }

    public void Wait(Task task, CancellationToken token)
    {
        if (!IsOnUIThread)
        {
            Task.WaitAny([task], token);
            return;
        }

        using var wake = token.Register(Signal);
        task.ContinueWith(_ => Signal(), TaskScheduler.Default);
        while (AnswerNext(NSDate.DistantPast, false) || !task.IsCompleted)
        {
            token.ThrowIfCancellationRequested();
            if (!task.IsCompleted)
                AnswerNext(NSDate.DistantFuture, false);
        }
    }

    // While the application starts its own signals wait, as on every platform: only the splash window's are answered.
    private bool AnswerNext(IntPtr until, bool answersOwn)
    {
        var pool = ObjCRuntime.PushAutoreleasePool();
        try
        {
            var nsEvent = NSApplication.NextEvent(until);
            if (nsEvent == IntPtr.Zero)
                return false;

            var type = NSEvent.TypeOf(nsEvent);
            if (type == NSEventType.ApplicationDefined && NSEvent.SubtypeOf(nsEvent) == WakeSubtype)
            {
                if (answersOwn)
                    Signaled?.Invoke();
                SignalGuests();
            }
            else if (type == NSEventType.KeyUp && NSApplication.KeyWindow is var key && key != IntPtr.Zero &&
                     (NSEvent.ModifierFlagsOf(nsEvent) & NSEventModifierFlags.Command) != 0)
                NSWindow.SendEvent(key, nsEvent);   // NSApplication keeps a key's release to itself while Command is held
            else
                NSApplication.SendEvent(nsEvent);

            return true;
        }
        finally
        {
            ObjCRuntime.PopAutoreleasePool(pool);
        }
    }

    private void SignalGuests()
    {
        MacOSPlatform[] guests;
        lock (_guests)
        {
            if (_guests.Count == 0)
                return;

            guests = [.. _guests];
        }

        foreach (var guest in guests)
            guest.Signaled?.Invoke();
    }

    public void Signal()
    {
        var pool = ObjCRuntime.PushAutoreleasePool();
        NSApplication.PostEvent(NSEvent.CreateApplicationDefined(WakeSubtype));
        ObjCRuntime.PopAutoreleasePool(pool);
    }

    // TODO(macOS): NSWindow.windowNumberAtPoint:belowWindowWithWindowNumber: -> the window number, mapped back to our
    // handle. Zero until then, which makes the drag fall back to client-bounds containment.
    public IntPtr WindowFromScreenPoint(PixelPoint point) => IntPtr.Zero;

    internal void Register(MacOSWindowWorker worker)
    {
        lock (ModalLock)
            _windows.Add(worker);
    }

    internal void Unregister(MacOSWindowWorker worker)
    {
        lock (ModalLock)
            _windows.Remove(worker);
    }

    /// <summary>Fills <paramref name="others"/> with the open windows other than <paramref name="worker"/>: those a modal
    /// window keeps from input.</summary>
    internal void CollectOthers(MacOSWindowWorker worker, List<MacOSWindowWorker> others)
    {
        lock (ModalLock)
        {
            foreach (var other in _windows)
            {
                if (other != worker)
                    others.Add(other);
            }
        }
    }

    /// <summary>Raises on the loop thread what AppKit told this one, in the order it came: the visual tree is not to
    /// be touched from the main thread.</summary>
    internal static void DispatchInput(Action raise)
    {
        var dispatcher = Threading.Dispatcher.CurrentDispatcher;
        if (dispatcher != null)
            dispatcher.Post(raise);
        else
            raise();
    }

    /// <summary>Runs <paramref name="action"/> on the main thread, the only one AppKit may be called from: at once
    /// when called there, else queued.</summary>
    internal void RunOnMainThread(Action action)
    {
        if (!IsOnUIThread)
        {
            (Dispatcher ?? Threading.Dispatcher.CurrentDispatcher).Invoke(action);
            return;
        }

        var pool = ObjCRuntime.PushAutoreleasePool();
        try
        {
            action();
        }
        finally
        {
            ObjCRuntime.PopAutoreleasePool(pool);
        }
    }

    /// <summary>The windows a shown <paramref name="owner"/> has, the splash window's side included, join it.</summary>
    internal void JoinOwned(IntPtr owner)
    {
        var host = _host ?? this;
        host.JoinOwnedHere(owner);
        MacOSPlatform[] guests;
        lock (host._guests)
            guests = [.. host._guests];

        foreach (var guest in guests)
            guest.JoinOwnedHere(owner);
    }

    private void JoinOwnedHere(IntPtr owner)
    {
        MacOSWindowWorker[] windows;
        lock (ModalLock)
            windows = [.. _windows];

        foreach (var window in windows)
            window.JoinIfOwnedBy(owner);
    }

    private static void ShowApplicationIcon()
    {
        if (ApplicationIcon.Load() is not { Pictures.Count: > 0 } icon)
            return;

        var picture = icon.Pictures[0];
        var cgImage = CGImage.FromPremultipliedBgra(picture.PremultipliedBgra, picture.Size, picture.Size);
        var image = NSImage.FromCGImage(cgImage, new CGSize(picture.Size, picture.Size));
        NSApplication.SetIconImage(image);
        NSObject.Release(image);
        CoreGraphicsInterop.Release(cgImage);
    }

    public static void Initialize(IContainerRegistry resolver)
    {
        resolver.RegisterSingleton<IApplicationPlatform, MacOSPlatform>();
        resolver.RegisterSingleton<IDragGhost, MacOSDragGhost>();
        FileManager.Platform = new FinderFileManager();
        Trash.Platform = new FinderTrash();
    }
}
