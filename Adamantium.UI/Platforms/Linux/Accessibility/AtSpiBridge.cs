using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.DBus;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Platforms.Linux.Accessibility;

/// <summary>The application on the desktop's accessibility bus: it registers there and serves the elements automation
/// knows - their roles, names, states, actions, values, text and selection - as AT-SPI objects to Orca, the magnifier
/// and other assistive technologies, and tells them what changes while anyone listens.</summary>
internal sealed partial class AtSpiBridge : IDisposable
{
    private const int Pending = 0;
    private const int Started = 1;
    private const int Abandoned = 2;

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    private readonly DBusConnection _bus;
    private readonly string _registry;
    private readonly Action<Action> _post;
    private readonly object _peersLock = new();
    private readonly Dictionary<int, WeakReference<AutomationPeer>> _peers = new();
    private readonly object _listenersLock = new();
    private readonly Dictionary<(string Bus, string Event), int> _listeners = new();
    private readonly List<IDisposable> _subscriptions = [];
    private volatile DesktopSocket _desktop = new(string.Empty, Paths.Null);
    private IDisposable _served;
    private int _applicationId;
    private int _listenerRefreshes;
    private int _listenersApplied;
    private int _prunedAt;
    private bool _relaying;
    private bool _disposed;

    private AtSpiBridge(DBusConnection bus, string registry, Action<Action> post)
    {
        _bus = bus;
        _registry = registry;
        _post = post;
    }

    /// <summary>Connects to the session's accessibility bus and registers the application there, on the thread pool;
    /// gives null where the desktop has no accessibility bus.</summary>
    public static Task<AtSpiBridge> StartAsync() => Task.Run(Start);

    /// <summary>Registers the application with <paramref name="registry"/> on <paramref name="bus"/> and serves it
    /// there until disposed, which closes the bus too. The elements are read where <paramref name="post"/> runs work -
    /// the application's loop when it is null.</summary>
    internal static AtSpiBridge Attach(DBusConnection bus, string registry, Action<Action> post = null)
    {
        var bridge = new AtSpiBridge(bus, registry, post ?? LoopSignal.PostAwaited);
        try
        {
            bridge.Register();
        }
        catch
        {
            bridge.Dispose();
            throw;
        }

        return bridge;
    }

    private static AtSpiBridge Start()
    {
        DBusConnection bus = null;
        try
        {
            string address;
            using (var session = DBusConnection.ConnectSession())
            {
                address = session.Call(DBusMessage.MethodCall(BusNames.AccessibilityBus, Paths.AccessibilityBus,
                    Interfaces.AccessibilityBus, Methods.GetAddress)).Reader().ReadString();
            }

            bus = DBusConnection.Connect(address);
            return Attach(bus, BusNames.Registry);
        }
        catch (Exception e) when (e is DBusException or IOException or SocketException)
        {
            bus?.Dispose();
            Serilog.Log.Logger.Debug(e, "No accessibility bus: assistive technologies do not see the application");
            return null;
        }
        catch (Exception e)
        {
            bus?.Dispose();
            Serilog.Log.Logger.Warning(e, "The application could not register on the accessibility bus");
            return null;
        }
    }

    private void Register()
    {
        _served = _bus.Serve(call => Task.FromResult(Answer(call)));
        _subscriptions.Add(_bus.Subscribe(_registry, Paths.Registry, Interfaces.Registry,
            Signals.EventListenerRegistered, OnListenersChanged));
        _subscriptions.Add(_bus.Subscribe(_registry, Paths.Registry, Interfaces.Registry,
            Signals.EventListenerDeregistered, OnListenersChanged));

        var desktop = _bus.Call(DBusMessage.MethodCall(_registry, Paths.Root, Interfaces.Socket, Methods.Embed,
            Signatures.Reference, writer => WriteReference(writer, _bus.UniqueName, Paths.Root))).Reader();
        desktop.Align(8);
        _desktop = new DesktopSocket(desktop.ReadString(), desktop.ReadObjectPath().Value);

        var refresh = Interlocked.Increment(ref _listenerRefreshes);
        ApplyListeners(refresh, _bus.Call(ListenersCall()));
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        lock (_listenersLock)
        {
            _disposed = true;
            _listeners.Clear();
            UpdateRelaying();
        }

        lock (_windowsLock)
        {
            foreach (var window in _windows)
            {
                window.Closed -= OnWindowClosed;
            }

            _windows.Clear();
        }

        _served?.Dispose();
        _bus.Dispose();
    }

    private DBusMessage Answer(DBusMessage call)
    {
        if (call.Path == Paths.Cache)
        {
            return call.Interface == Interfaces.Cache && call.Member == Methods.GetItems
                ? DBusMessage.MethodReturn(call, Signatures.CacheItems, writer => writer.WriteArray(8, _ => { }))
                : null;
        }

        if (!TryTarget(call.Path, out var peer))
        {
            return DBusMessage.Error(call, Errors.UnknownObject, $"{call.Path} is no object here");
        }

        return Run(() => call.Interface switch
        {
            Interfaces.Properties => Properties(call, peer),
            Interfaces.Accessible => Accessible(call, peer),
            Interfaces.Application when peer == null => Application(call),
            Interfaces.Component when peer != null => Component(call, peer),
            Interfaces.Action when peer != null => Action(call, peer),
            Interfaces.Text when peer != null => Text(call, peer),
            Interfaces.EditableText when peer != null => EditableText(call, peer),
            Interfaces.Selection when peer != null => Selection(call, peer),
            _ => null,
        });
    }

    private T Run<T>(Func<T> work)
    {
        if (LoopSignal.IsLoopThread)
        {
            return work();
        }

        T result = default;
        Exception failure = null;
        var state = Pending;
        var done = new ManualResetEventSlim();
        _post(() =>
        {
            if (Interlocked.CompareExchange(ref state, Started, Pending) != Pending)
            {
                return;
            }

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
            if (Interlocked.CompareExchange(ref state, Abandoned, Pending) == Pending)
            {
                done.Dispose();
                throw new TimeoutException("The application did not answer the accessibility bus in time.");
            }

            if (!done.Wait(CallTimeout))
            {
                throw new TimeoutException("The application is still answering the accessibility bus.");
            }
        }

        done.Dispose();
        if (failure != null)
        {
            throw failure;
        }

        return result;
    }

    private bool TryTarget(string path, out AutomationPeer peer)
    {
        peer = null;
        if (path == Paths.Root)
        {
            return true;
        }

        if (!path.StartsWith(Paths.ObjectPrefix, StringComparison.Ordinal) ||
            !int.TryParse(path.AsSpan(Paths.ObjectPrefix.Length), out var id))
        {
            return false;
        }

        lock (_peersLock)
        {
            return _peers.TryGetValue(id, out var reference) && reference.TryGetTarget(out peer);
        }
    }

    /// <summary>The object path <paramref name="peer"/> is served at; the application's root for null.</summary>
    internal string PathOf(AutomationPeer peer)
    {
        if (peer == null)
        {
            return Paths.Root;
        }

        lock (_peersLock)
        {
            _peers[peer.RuntimeId] = new WeakReference<AutomationPeer>(peer);
            if (_peers.Count > Math.Max(256, _prunedAt * 2))
            {
                Prune();
            }
        }

        return Paths.ObjectPrefix + peer.RuntimeId;
    }

    private void Prune()
    {
        List<int> dead = [];
        foreach (var (id, reference) in _peers)
        {
            if (!reference.TryGetTarget(out _))
            {
                dead.Add(id);
            }
        }

        foreach (var id in dead)
        {
            _peers.Remove(id);
        }

        _prunedAt = _peers.Count;
    }

    private void WriteReference(DBusWriter writer, AutomationPeer peer) => WriteReference(writer, _bus.UniqueName, PathOf(peer));

    private static void WriteReference(DBusWriter writer, string name, string path) => writer.WriteStruct(reference =>
    {
        reference.WriteString(name);
        reference.WriteObjectPath(path);
    });

    private static void WriteNullReference(DBusWriter writer) => WriteReference(writer, string.Empty, Paths.Null);

    private DBusVariant Reference(AutomationPeer peer)
    {
        object[] fields = [_bus.UniqueName, new DBusObjectPath(PathOf(peer))];
        return new DBusVariant(Signatures.Reference, fields);
    }

    private IReadOnlyList<AutomationPeer> Windows()
    {
        List<AutomationPeer> windows = [];
        foreach (var window in UIAppContext.Current?.UIContext?.UIApplication?.Windows ?? [])
        {
            if (!window.IsClosed && window is not SplashWindow && (window as UIComponent)?.GetAutomationPeer() is { } peer)
            {
                Track(window);
                windows.Add(peer);
            }
        }

        return windows;
    }

    private IReadOnlyList<AutomationPeer> ChildrenOf(AutomationPeer peer) => peer == null ? Windows() : peer.GetChildren();

    private static AutomationPeer ParentOf(AutomationPeer peer) =>
        peer.GetParent() is { } parent && (peer as UIComponentAutomationPeer)?.Owner is not IWindow ? parent : null;

    private static AutomationPeer Reachable(AutomationPeer peer)
    {
        while (peer != null && ParentOf(peer) is { } parent && !Contains(parent.GetChildren(), peer))
        {
            peer = parent;
        }

        return peer;
    }

    private static bool Contains(IReadOnlyList<AutomationPeer> peers, AutomationPeer peer)
    {
        foreach (var candidate in peers)
        {
            if (ReferenceEquals(candidate, peer))
            {
                return true;
            }
        }

        return false;
    }

    private static string ApplicationName => Assembly.GetEntryAssembly()?.GetName().Name ?? ToolkitName;

    private sealed record DesktopSocket(string Name, string Path);
}
