using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.MacOS.AppKit;
using Adamantium.MacOS.CoreGraphics;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.MacOS.Accessibility;

/// <summary>A window's elements as the Mac's accessibility sees them - VoiceOver, Zoom, Voice Control: an
/// <c>NSAccessibilityElement</c> for each automation peer asked about, made when AppKit first asks, so nothing is made
/// while no assistive technology looks. What a peer is is read on the application's loop between frames, waited for;
/// what it does is handed to the loop and not waited for.</summary>
internal sealed class MacOSAccessibility : IDisposable
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    private static readonly IntPtr RetainSelector = ObjCRuntime.RegisterSelector("retain");
    private static readonly IntPtr AutoreleaseSelector = ObjCRuntime.RegisterSelector("autorelease");
    private const int PruneEvery = 64;

    private readonly Func<AutomationPeer> _root;
    private readonly IntPtr _container;
    private readonly Func<Rect, CGRect> _toPoints;
    private readonly Func<CGPoint, PixelPoint> _toPixels;
    private readonly Action<Action> _read;
    private readonly Action<Action> _act;
    private readonly Action<IntPtr, string> _notify;
    private readonly object _lock = new();
    private readonly Dictionary<int, IntPtr> _elements = new();
    private int _madeSincePruned;
    private const int LayoutEvery = 250;
    private long _layoutToldAt = long.MinValue / 2;
    private int _layoutPending;
    private bool _relaying;
    private const long QuietFor = 60_000;
    private const long QuietCheckEvery = 30_000;
    private readonly Func<bool> _voiceOverOn;
    private long _askedAt;
    private long _checkedAt;
    private bool _disposed;

    /// <param name="root">The window's own peer, whose children are the window's elements.</param>
    /// <param name="container">The view the window's elements stand in, their parent; zero for none.</param>
    /// <param name="toPoints">A rectangle on screen in pixels, from the top, as AppKit's desktop has it: in points,
    /// from the bottom.</param>
    /// <param name="toPixels">The reverse, for a point.</param>
    /// <param name="post">Where the peers are read and acted on - the application's loop when null.</param>
    /// <param name="notify">Tells the system a notification of an element, which it is handed held and lets go of
    /// - on AppKit's thread, which <paramref name="onMainThread"/> runs work on, when null.</param>
    /// <param name="onMainThread">Runs work on AppKit's thread, for the notifications; it must run all it is given.</param>
    /// <param name="voiceOverOn">Whether VoiceOver is on: asked once nothing was asked for a minute, to stop listening.
    /// </param>
    public MacOSAccessibility(Func<AutomationPeer> root, IntPtr container, Func<Rect, CGRect> toPoints,
        Func<CGPoint, PixelPoint> toPixels, Action<Action> post = null, Action<IntPtr, string> notify = null,
        Action<Action> onMainThread = null, Func<bool> voiceOverOn = null)
    {
        _voiceOverOn = voiceOverOn ?? (() => NSAccessibility.IsVoiceOverOn);
        _root = root;
        _container = container;
        _toPoints = toPoints;
        _toPixels = toPixels;
        _read = post ?? LoopSignal.PostBetweenFrames;
        _act = post ?? LoopSignal.Post;
        _notify = notify ?? ((element, name) => Notify(element, name, onMainThread));
    }

    // The element comes held, and is let go of once told: it may be let go of here before AppKit's thread gets to it.
    private static void Notify(IntPtr element, string notification, Action<Action> onMainThread)
    {
        if (onMainThread == null)
        {
            NSObject.Release(element);
            return;
        }

        onMainThread(() =>
        {
            try
            {
                NSAccessibility.PostNotification(element, notification);
            }
            finally
            {
                NSObject.Release(element);
            }
        });
    }

    /// <summary>The view the window's elements stand in.</summary>
    internal IntPtr Container => _container;

    /// <summary>The window's elements, an autoreleased array; empty when the loop does not answer.</summary>
    public IntPtr Children() => ArrayOf(Read(() => _root()?.GetChildren(), null));

    /// <summary>The children of the element of <paramref name="node"/>, an autoreleased array.</summary>
    public IntPtr ChildrenOf(MacOSAccessibilityNode node) =>
        node.Peer is { } peer ? ArrayOf(Read(peer.GetChildren, null)) : NSArray.Create([]);

    /// <summary>The element under a point of AppKit's desktop, the deepest the hit test finds that VoiceOver stops at,
    /// autoreleased; zero for none.</summary>
    public IntPtr HitTest(CGPoint point)
    {
        var screen = _toPixels(point);
        return Lent(Read(() => Stopping(PeerAt(screen)), null));
    }

    /// <summary>The element that has the keyboard, autoreleased; zero for none.</summary>
    public IntPtr Focused() => Lent(Read(() => Stopping(FocusedPeer(_root())), null));

    /// <summary>The parent element of <paramref name="peer"/>'s, autoreleased: its parent peer's, the window's view for
    /// one of the window's own.</summary>
    public IntPtr ParentOf(AutomationPeer peer)
    {
        var parent = Read(() => ReachableParent(peer), null);
        return parent == null ? _container : Lent(parent);
    }

    /// <summary><paramref name="peer"/>'s place on AppKit's desktop.</summary>
    public CGRect FrameOf(AutomationPeer peer) => _toPoints(Read(() => peer.BoundingRectangle, Rect.Empty));

    /// <summary>A rectangle on screen in pixels, on AppKit's desktop.</summary>
    public CGRect ToPoints(Rect pixels) => _toPoints(pixels);

    /// <summary>A point of AppKit's desktop, on screen in pixels.</summary>
    public PixelPoint ToPixels(CGPoint point) => _toPixels(point);

    /// <summary>The element of <paramref name="peer"/>, made the first time it is asked for; owned by this, valid until
    /// the next question starts.</summary>
    public IntPtr ElementOf(AutomationPeer peer)
    {
        if (peer == null)
        {
            return IntPtr.Zero;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return IntPtr.Zero;
            }

            if (_elements.TryGetValue(peer.RuntimeId, out var known) && MacOSAccessibilityElement.NodeOf(known)?.Peer == peer)
            {
                return known;
            }

            if (known != IntPtr.Zero)
            {
                MacOSAccessibilityElement.Release(known);
            }

            _madeSincePruned++;
            var element = MacOSAccessibilityElement.Create(new MacOSAccessibilityNode(this, peer));
            _elements[peer.RuntimeId] = element;
            Relay();
            return element;
        }
    }

    // A question means an assistive technology looks: from then on - until it has asked nothing for a minute and
    // VoiceOver is off - it is told what changes.
    private void Relay()
    {
        Volatile.Write(ref _askedAt, Environment.TickCount64);
        lock (_lock)
        {
            if (_relaying || _disposed)
            {
                return;
            }

            _relaying = true;
            AutomationEvents.Raised += OnRaised;
        }
    }

    // On the loop, at most every half minute: nothing asked for a minute and VoiceOver off, nobody listens any more.
    private bool StoppedListening()
    {
        var now = Environment.TickCount64;
        if (now - _checkedAt < QuietCheckEvery)
        {
            return false;
        }

        _checkedAt = now;
        if (now - Volatile.Read(ref _askedAt) < QuietFor || _voiceOverOn())
        {
            return false;
        }

        lock (_lock)
        {
            if (_relaying)
            {
                _relaying = false;
                AutomationEvents.Raised -= OnRaised;
            }
        }

        return true;
    }

    /// <summary>Reads <paramref name="read"/> on the loop; <paramref name="none"/> when it does not answer in time or fails.
    /// </summary>
    public T Read<T>(Func<T> read, T none)
    {
        Relay();
        try
        {
            return LoopCall.Run(_read, read, CallTimeout);
        }
        catch (Exception e)
        {
            Serilog.Log.Logger.Debug(e, "An assistive technology's question went unanswered");
            return none;
        }
    }

    /// <summary>Hands <paramref name="act"/> to the loop, not waiting for it.</summary>
    public void Post(Action act) => _act(() =>
    {
        try
        {
            act();
        }
        catch (Exception e)
        {
            Serilog.Log.Logger.Warning(e, "An assistive technology's action failed");
        }
    });

    /// <summary>Lets go of every element; one an assistive technology still holds answers nothing.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_relaying)
            {
                AutomationEvents.Raised -= OnRaised;
            }

            foreach (var element in _elements.Values)
            {
                MacOSAccessibilityElement.Release(element);
            }

            _elements.Clear();
        }
    }

    // On the loop, where automation tells it: what changed in this window, told as AppKit's notification - of an element
    // already made only, but the one that took the keyboard, which is made for it.
    private void OnRaised(object sender, AutomationEventArgs e)
    {
        // This window's: in it, or - a menu shut, already out of it - an element made for it.
        if (StoppedListening() || e.Peer == null || (_root() as UIComponentAutomationPeer)?.Owner is not { } window ||
            !ReferenceEquals((e.Element as UIComponent)?.RootVisual, window) && Existing(e.Peer) == IntPtr.Zero)
        {
            return;
        }

        switch (e.Event)
        {
            // The window itself taking the keyboard is its view's: the window's own peer has no element.
            case AutomationEvent.FocusChanged:
                if (Stopping(e.Peer) is { } focused && !ReferenceEquals(focused, _root()))
                    Tell(focused, NSAccessibility.FocusedElementChanged, make: true);
                else
                    TellView(NSAccessibility.FocusedElementChanged);
                break;
            case AutomationEvent.PropertyChanged:
                Tell(e.Peer, NotificationOf(e), make: false);
                break;
            // ElementSelected says it once; the item's own IsSelected would say it again.
            case AutomationEvent.ElementSelected when ContainerOf(e.Peer) is { } container && !ReferenceEquals(container, _root()):
                Tell(container, NSAccessibility.SelectedChildrenChanged, make: false);
                break;
            case AutomationEvent.ElementSelected:
                TellView(NSAccessibility.SelectedChildrenChanged);
                break;
            case AutomationEvent.MenuOpened:
                Tell(e.Peer, NSAccessibility.MenuOpened, make: true);
                break;
            case AutomationEvent.MenuClosed:
                Tell(e.Peer, NSAccessibility.MenuClosed, make: false);
                break;
            // A text box tells its Value as well: that one is told.
            case AutomationEvent.TextChanged when e.Peer.GetPattern(PatternId.Value) == null:
                Tell(e.Peer, NSAccessibility.ValueChanged, make: false);
                break;
            case AutomationEvent.TextSelectionChanged:
                Tell(e.Peer, NSAccessibility.SelectedTextChanged, make: false);
                break;
            case AutomationEvent.StructureChanged:
                TellLayout();
                break;
        }
    }

    // Held under the lock, so the window closing meanwhile cannot let it go first; the teller lets go of it.
    private void Tell(AutomationPeer peer, string notification, bool make)
    {
        if (peer == null || notification == null)
        {
            return;
        }

        IntPtr element;
        lock (_lock)
        {
            element = make ? ElementOf(peer) : Existing(peer);
            if (element == IntPtr.Zero)
            {
                return;
            }

            NSObject.Retain(element);
        }

        _notify(element, notification);
    }

    private void TellView(string notification)
    {
        lock (_lock)
        {
            if (_disposed || _container == IntPtr.Zero)
            {
                return;
            }

            NSObject.Retain(_container);
        }

        _notify(_container, notification);
    }

    // At most every quarter second - a list filling would tell it for every item - and once more after the last of a
    // burst, which would otherwise go untold.
    private void TellLayout()
    {
        var now = Environment.TickCount64;
        if (now - _layoutToldAt >= LayoutEvery)
        {
            _layoutToldAt = now;
            TellView(NSAccessibility.LayoutChanged);
            return;
        }

        if (Interlocked.Exchange(ref _layoutPending, 1) == 0)
        {
            Task.Delay(TimeSpan.FromMilliseconds(LayoutEvery)).ContinueWith(_ =>
            {
                Volatile.Write(ref _layoutPending, 0);
                _layoutToldAt = Environment.TickCount64;
                TellView(NSAccessibility.LayoutChanged);
            }, TaskScheduler.Default);
        }
    }

    // The element of a peer already asked about; zero for one never asked about, which nothing listens to yet.
    private IntPtr Existing(AutomationPeer peer)
    {
        if (peer == null)
        {
            return IntPtr.Zero;
        }

        lock (_lock)
        {
            return !_disposed && _elements.TryGetValue(peer.RuntimeId, out var element) &&
                   MacOSAccessibilityElement.NodeOf(element)?.Peer == peer
                ? element
                : IntPtr.Zero;
        }
    }

    private static string NotificationOf(AutomationEventArgs e) => e.Property switch
    {
        AutomationProperty.Name => NSAccessibility.TitleChanged,
        AutomationProperty.ToggleState or AutomationProperty.Value or AutomationProperty.RangeValue => NSAccessibility.ValueChanged,
        AutomationProperty.ExpandCollapseState when e.Peer.ControlType == AutomationControlType.TreeItem =>
            Equals(e.NewValue, ExpandCollapseState.Expanded) ? NSAccessibility.RowExpanded : NSAccessibility.RowCollapsed,
        _ => null
    };

    // What an item is selected among: the container its selection says, else its parent.
    private static AutomationPeer ContainerOf(AutomationPeer peer) =>
        (peer.GetPattern(PatternId.SelectionItem) as ISelectionItemProvider)?.SelectionContainer ?? peer.GetParent();

    // An autoreleased array of the elements of the peers, none let go of while it is made.
    internal IntPtr ArrayOf(IReadOnlyList<AutomationPeer> peers)
    {
        PruneGone();
        if (peers == null || peers.Count == 0)
        {
            return NSArray.Create([]);
        }

        var elements = new IntPtr[peers.Count];
        var count = 0;
        lock (_lock)
        {
            foreach (var peer in peers)
            {
                if (ElementOf(peer) is var element && element != IntPtr.Zero)
                {
                    elements[count++] = element;
                }
            }

            return NSArray.Create(elements.AsSpan(0, count));
        }
    }

    // One element handed to AppKit outlives what this lets go of until AppKit's pool drains.
    internal IntPtr Lent(AutomationPeer peer)
    {
        PruneGone();
        lock (_lock)
        {
            var element = ElementOf(peer);
            return element == IntPtr.Zero ? IntPtr.Zero : NSObject.Send(NSObject.Send(element, RetainSelector), AutoreleaseSelector);
        }
    }

    // Elements whose peer the collector took go - at the start of a question, never while one is answered.
    private void PruneGone()
    {
        lock (_lock)
        {
            if (_madeSincePruned < PruneEvery)
            {
                return;
            }

            _madeSincePruned = 0;
            List<int> gone = null;
            foreach (var (id, element) in _elements)
            {
                if (MacOSAccessibilityElement.NodeOf(element)?.Peer == null)
                {
                    (gone ??= []).Add(id);
                }
            }

            foreach (var id in gone ?? [])
            {
                MacOSAccessibilityElement.Release(_elements[id]);
                _elements.Remove(id);
            }
        }
    }

    // The nearest from the peer up that VoiceOver stops at: an unnamed container it walks through.
    private AutomationPeer Stopping(AutomationPeer peer)
    {
        for (var at = peer; at != null; at = ReachableParent(at))
        {
            if (MacOSAccessibilityRoles.IsElement(at))
            {
                return at;
            }
        }

        return null;
    }

    // The deepest peer under the point; null outside the window's elements.
    private AutomationPeer PeerAt(PixelPoint screen)
    {
        if ((_root() as UIComponentAutomationPeer)?.Owner is not IRootVisualComponent window ||
            window is not IInputComponent root)
        {
            return null;
        }

        for (var node = PointerDevice.HitTestTopmost(root, window.PointToClient(screen), hover: true) as UIComponent;
             node != null;
             node = node.VisualParent as UIComponent)
        {
            if (node.GetAutomationPeer() is { } peer && !ReferenceEquals(peer, _root()))
            {
                return peer;
            }
        }

        return null;
    }

    // What has the keyboard in the window, or the nearest above it that automation knows.
    private static AutomationPeer FocusedPeer(AutomationPeer window)
    {
        if ((window as UIComponentAutomationPeer)?.Owner is not { } owner)
        {
            return null;
        }

        for (var node = FocusManager.GetFocusedElement(owner) as UIComponent; node != null && !ReferenceEquals(node, owner);
             node = node.VisualParent as UIComponent)
        {
            if (node.GetAutomationPeer() is { } peer)
            {
                return peer;
            }
        }

        return null;
    }

    // The window's own peer stands for the view, not for an element of its own.
    private AutomationPeer ReachableParent(AutomationPeer peer)
    {
        var parent = peer.GetParent();
        return parent == null || ReferenceEquals(parent, _root()) ? null : parent;
    }
}
