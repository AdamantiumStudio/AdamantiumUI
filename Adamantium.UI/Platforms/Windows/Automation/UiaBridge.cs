using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Platforms.Windows.Automation;

internal sealed class UiaBridge
{
    public const int ElementNotAvailable = unchecked((int)0x80040201);
    public const int TimedOut = unchecked((int)0x80131505);

    private static readonly ConditionalWeakTable<IWindow, UiaBridge> Bridges = new();
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    private static int _listening;

    private readonly ConditionalWeakTable<AutomationPeer, UiaElementProvider> _providers = new();

    private UiaBridge(IWindow window, nint hwnd)
    {
        Window = window;
        Handle = hwnd;
        Root = new UiaWindowProvider(this);
    }

    public static StrategyBasedComWrappers ComWrappers { get; } = new();

    public nint Handle { get; }

    public UiaWindowProvider Root { get; }

    public IWindow Window { get; }

    public static nint Answer(IWindow window, nint hwnd, nint wParam, nint lParam)
    {
        var bridge = Bridges.GetValue(window, w => new UiaBridge(w, hwnd));
        if (Interlocked.Exchange(ref _listening, 1) == 0)
        {
            AutomationEvents.Raised += OnRaised;
        }

        return UiaInterop.UiaReturnRawElementProvider(hwnd, wParam, lParam, bridge.Root);
    }

    private static void OnRaised(object sender, AutomationEventArgs e)
    {
        if (!UiaInterop.UiaClientsAreListening() || BridgeOf(e.Peer) is not { } bridge)
        {
            return;
        }

        var peer = bridge.Reachable(e.Peer);
        if (e.Event == AutomationEvent.PropertyChanged && !ReferenceEquals(peer, e.Peer))
        {
            return;
        }

        var provider = bridge.ProviderFor(peer);
        switch (e.Event)
        {
            case AutomationEvent.PropertyChanged:
                RaisePropertyChanged(provider, e);
                break;
            case AutomationEvent.StructureChanged:
                RaiseStructureChanged(provider, peer);
                break;
            case AutomationEvent.FocusChanged:
                UiaInterop.UiaRaiseAutomationEvent(provider, UiaIds.AutomationFocusChangedEvent);
                break;
            default:
                UiaInterop.UiaRaiseAutomationEvent(provider, EventIdOf(e.Event));
                break;
        }
    }

    private static void RaisePropertyChanged(UiaProvider provider, AutomationEventArgs e)
    {
        var oldValue = ValueOf(e.Property, e.OldValue);
        var newValue = ValueOf(e.Property, e.NewValue);
        try
        {
            UiaInterop.UiaRaiseAutomationPropertyChangedEvent(provider, PropertyIdOf(e.Property), oldValue, newValue);
        }
        finally
        {
            oldValue.Free();
            newValue.Free();
        }
    }

    private static unsafe void RaiseStructureChanged(UiaProvider provider, AutomationPeer peer)
    {
        var runtimeId = stackalloc int[] { UiaInterop.AppendRuntimeId, peer.RuntimeId };
        UiaInterop.UiaRaiseStructureChangedEvent(provider, UiaStructureChangeType.ChildrenInvalidated, runtimeId, 2);
    }

    private static UiaBridge BridgeOf(AutomationPeer peer)
    {
        for (var node = peer; node != null; node = node.GetParent())
        {
            if ((node as UIComponentAutomationPeer)?.Owner is IWindow window && Of(window) is { } bridge)
            {
                return bridge;
            }
        }

        return null;
    }

    private static int EventIdOf(AutomationEvent automationEvent) => automationEvent switch
    {
        AutomationEvent.Invoked => UiaIds.InvokedEvent,
        AutomationEvent.ElementSelected => UiaIds.ElementSelectedEvent,
        AutomationEvent.MenuOpened => UiaIds.MenuOpenedEvent,
        AutomationEvent.MenuClosed => UiaIds.MenuClosedEvent,
        AutomationEvent.WindowOpened => UiaIds.WindowOpenedEvent,
        AutomationEvent.WindowClosed => UiaIds.WindowClosedEvent,
        AutomationEvent.TextChanged => UiaIds.TextChangedEvent,
        AutomationEvent.TextSelectionChanged => UiaIds.TextSelectionChangedEvent,
        _ => throw new ArgumentOutOfRangeException(nameof(automationEvent), automationEvent, null)
    };

    private static int PropertyIdOf(AutomationProperty property) => property switch
    {
        AutomationProperty.Name => UiaIds.NameProperty,
        AutomationProperty.IsEnabled => UiaIds.IsEnabledProperty,
        AutomationProperty.ToggleState => UiaIds.ToggleStateProperty,
        AutomationProperty.Value => UiaIds.ValueValueProperty,
        AutomationProperty.RangeValue => UiaIds.RangeValueValueProperty,
        AutomationProperty.ExpandCollapseState => UiaIds.ExpandCollapseStateProperty,
        AutomationProperty.IsSelected => UiaIds.SelectionItemIsSelectedProperty,
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
    };

    private static UiaVariant ValueOf(AutomationProperty property, object value) => (property, value) switch
    {
        (AutomationProperty.ToggleState, ToggleState state) => UiaVariant.From((int)UiaProvider.ToUia(state)),
        (AutomationProperty.ExpandCollapseState, ExpandCollapseState state) => UiaVariant.From((int)UiaProvider.ToUia(state)),
        (_, bool flag) => UiaVariant.From(flag),
        (_, double number) => UiaVariant.From(number),
        (_, string text) => UiaVariant.From(text),
        _ => UiaVariant.Empty
    };

    public static UiaBridge Of(IWindow window) => window != null && Bridges.TryGetValue(window, out var bridge) ? bridge : null;

    public static void Disconnect(IWindow window)
    {
        if (!Bridges.TryGetValue(window, out var bridge))
        {
            return;
        }

        Bridges.Remove(window);
        UiaInterop.UiaDisconnectProvider(bridge.Root);
        UiaInterop.UiaReturnRawElementProvider(bridge.Handle, 0, 0, null);
    }

    public T Run<T>(Func<T> work)
    {
        var dispatcher = Threading.Dispatcher.CurrentDispatcher;
        if (dispatcher == null || dispatcher.UIThread == Thread.CurrentThread)
        {
            return work();
        }

        T result = default;
        Exception failure = null;
        var done = new ManualResetEventSlim();
        LoopSignal.PostAwaited(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                done.Set();
            }
        });

        if (!done.Wait(CallTimeout))
        {
            throw new COMException("The application did not answer UI Automation in time.", TimedOut);
        }

        if (failure is COMException)
        {
            throw failure;
        }

        if (failure != null)
        {
            throw new COMException(failure.Message, failure.HResult);
        }

        return result;
    }

    public void Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    public AutomationPeer WindowPeer() => (Window as UIComponent)?.GetAutomationPeer();

    public UiaProvider ProviderFor(AutomationPeer peer)
    {
        if (peer == null)
        {
            return null;
        }

        return ReferenceEquals(peer, WindowPeer()) ? Root : _providers.GetValue(peer, p => new UiaElementProvider(this, p));
    }

    public UiaProvider ProviderOf(IUIComponent element)
    {
        for (var node = element as UIComponent; node != null; node = node.VisualParent as UIComponent)
        {
            if (node.GetAutomationPeer() is { } peer)
            {
                return ProviderFor(Reachable(peer));
            }
        }

        return Root;
    }

    public UiaProvider ProviderAt(PixelPoint screen)
    {
        var client = Window.PointToClient(screen);
        return ProviderOf(MouseDevice.HitTestTopmost(Window, client) as IUIComponent);
    }

    public static nint ComPointer(object instance) =>
        ComWrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);

    private AutomationPeer Reachable(AutomationPeer peer)
    {
        var root = WindowPeer();
        while (peer != null && !ReferenceEquals(peer, root))
        {
            var parent = peer.GetParent();
            if (parent == null || ContainsPeer(parent.GetChildren(), peer))
            {
                return peer;
            }

            peer = parent;
        }

        return peer ?? root;
    }

    private static bool ContainsPeer(IReadOnlyList<AutomationPeer> children, AutomationPeer peer)
    {
        foreach (var child in children)
        {
            if (ReferenceEquals(child, peer))
            {
                return true;
            }
        }

        return false;
    }
}
