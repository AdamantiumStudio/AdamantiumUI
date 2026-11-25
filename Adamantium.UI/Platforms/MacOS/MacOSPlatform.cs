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
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Platforms.Cups;

namespace Adamantium.UI.Platforms.MacOS;

/// <summary>macOS through AppKit: the application's event loop on the main thread, woken from any thread by an
/// application-defined event.</summary>
public class MacOSPlatform : IApplicationPlatform
{
    /// <summary>The environment variable that, set to 1, keeps the application off the desktop: see <see cref="IsHidden"/>.
    /// </summary>
    public const string HiddenVariable = "ADAM_HIDDEN_WINDOWS";

    private const short WakeSubtype = 0x4144;

    internal readonly object ModalLock = new();

    private readonly int _threadId;
    private readonly List<MacOSWindowWorker> _windows = [];
    private readonly List<MacOSModality> _modalities = [];
    private readonly List<MacOSPlatform> _guests = [];
    private readonly MacOSPlatform _host;

    /// <summary>Whether the application runs where nobody sees it - driven by automation: in no Dock, never active, with
    /// windows that are on no desktop.</summary>
    internal static bool IsHidden { get; } = Environment.GetEnvironmentVariable(HiddenVariable) == "1";

    public MacOSPlatform()
    {
        _threadId = Environment.CurrentManagedThreadId;
        var pool = ObjCRuntime.PushAutoreleasePool();
        NSApplication.Launch(IsHidden ? NSApplication.ProhibitedActivationPolicy : NSApplication.RegularActivationPolicy);
        var applicationDelegate = NSObject.New(MacOSApplicationDelegate.Class);
        ObjCManagedObject.Attach(applicationDelegate, this);
        NSApplication.SetDelegate(applicationDelegate);
        if (!IsHidden)
            ShowApplicationIcon();
        Desktop = new MacOSDisplays();
        Displays.Platform = Desktop;
        ObjCRuntime.PopAutoreleasePool(pool);
        if (!IsHidden)
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

    /// <summary>Takes a splash window's side away from its host: what was asked of it is done first, and a window still
    /// up goes off screen.</summary>
    internal void Disconnect()
    {
        _host.RunOnMainThread(() =>
        {
            Signaled?.Invoke();
            MacOSWindowWorker[] windows;
            lock (ModalLock)
                windows = [.. _windows];
            foreach (var window in windows)
                window.HideWindow();
        });
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
        if (!IsHidden)
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

    internal void Register(MacOSModality modality)
    {
        lock (ModalLock)
            _modalities.Add(modality);
    }

    internal void Unregister(MacOSModality modality)
    {
        lock (ModalLock)
            _modalities.Remove(modality);
    }

    /// <summary>Fills <paramref name="others"/> with the open windows other than that of <paramref name="modality"/>: those
    /// a modal window keeps from input.</summary>
    internal void CollectOthers(MacOSModality modality, List<MacOSModality> others)
    {
        lock (ModalLock)
        {
            foreach (var other in _modalities)
            {
                if (other != modality)
                    others.Add(other);
            }
        }
    }

    /// <summary>The hidden application's open windows (<see cref="IsHidden"/>), the one activated last at the end; the loop
    /// thread's.</summary>
    internal List<MacOSHiddenWindowWorker> HiddenWindows { get; } = [];

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
        foreach (var window in AllWindows())
            window.JoinIfOwnedBy(owner);
    }

    /// <summary>The windows of an <paramref name="owner"/> going off screen stay there: AppKit would take a window's
    /// children with it.</summary>
    internal void LeaveOwned(IntPtr owner)
    {
        foreach (var window in AllWindows())
            window.LeaveIfOwnedBy(owner);
    }

    /// <summary>The application is shown again after being hidden: windows opened meanwhile join their owners.</summary>
    internal void JoinAllOwned()
    {
        foreach (var window in AllWindows())
            window.JoinOwned();
    }

    private List<MacOSWindowWorker> AllWindows()
    {
        var host = _host ?? this;
        var windows = new List<MacOSWindowWorker>();
        lock (host.ModalLock)
            windows.AddRange(host._windows);
        lock (host._guests)
        {
            foreach (var guest in host._guests)
            {
                lock (guest.ModalLock)
                    windows.AddRange(guest._windows);
            }
        }

        return windows;
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
        SystemPrinting.Platform = new CupsPrintPlatform();
    }
}
