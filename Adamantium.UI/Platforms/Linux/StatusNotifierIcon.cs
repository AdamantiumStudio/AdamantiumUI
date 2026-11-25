using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Adamantium.DBus;
using Adamantium.UI.Core;
using Adamantium.UI.Platforms.Wayland;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>One icon of the panel's status area: a StatusNotifierItem on a bus name of its own, which the panel's
/// StatusNotifierWatcher is told of. The panel calls it back - Activate for a click, ContextMenu where it leaves the menu
/// to the application - and reads its menu from <see cref="StatusNotifierMenu"/>. Until a watcher runs the icon shows
/// nowhere; it is registered the moment one starts, and again if the panel restarts.</summary>
internal sealed class StatusNotifierIcon : INotificationAreaIcon
{
    internal const string ItemPath = "/StatusNotifierItem";
    internal const string MenuPath = "/MenuBar";
    internal const string ItemInterface = "org.kde.StatusNotifierItem";
    internal const string WatcherName = "org.kde.StatusNotifierWatcher";
    internal const string WatcherPath = "/StatusNotifierWatcher";
    internal const string WatcherInterface = "org.kde.StatusNotifierWatcher";

    private const string NoMenuPath = "/NO_DBUSMENU";
    private const string BusName = "org.freedesktop.DBus";
    private const string BusPath = "/org/freedesktop/DBus";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string IntrospectableInterface = "org.freedesktop.DBus.Introspectable";
    private const uint DoNotQueue = 4;
    private const uint PrimaryOwner = 1;
    // The pictures a panel is given: it scales the nearest; a larger one only makes every reading of the icon longer.
    private const int LargestPicture = 256;

    private static readonly Lazy<List<object>> Pixmaps = new(ReadPixmaps);

    private readonly object _lock = new();
    private readonly object _checking = new();
    private readonly string _watcher;
    private readonly Action<Action> _post;
    private readonly StatusNotifierMenu _menu;
    private readonly bool _hasMenu;
    private readonly List<IDisposable> _served = [];
    private DBusConnection _bus;
    private string _toolTip;
    private string _registeredAs;
    private bool _displayed;
    private bool _disposed;

    internal StatusNotifierIcon(string toolTip, INotificationAreaMenu menu, int id, Action<Action> post,
        string watcher = WatcherName)
    {
        _toolTip = toolTip;
        _post = post;
        _watcher = watcher;
        _hasMenu = menu != null;
        _menu = new StatusNotifierMenu(menu, post, Say);
        Name = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-{id}";
    }

    /// <summary>The bus name the icon asks for, which the watcher is told - or the connection's own name, where another
    /// process holds it: a sandboxed one, whose process ids are its own.</summary>
    public string Name { get; }

    public string ToolTip
    {
        get
        {
            lock (_lock)
            {
                return _toolTip;
            }
        }
        set
        {
            lock (_lock)
            {
                _toolTip = value;
            }

            Say(DBusMessage.Signal(ItemPath, ItemInterface, "NewToolTip"));
            Say(DBusMessage.Signal(ItemPath, ItemInterface, "NewTitle"));
        }
    }

    /// <summary>The watcher took the icon and a panel showing the icons is registered with it.</summary>
    public bool IsDisplayed
    {
        get
        {
            lock (_lock)
            {
                return _displayed;
            }
        }
    }

    public event EventHandler Clicked;

    public event EventHandler<NotificationAreaMenuEventArgs> MenuAsked;

    public event EventHandler IsDisplayedChanged;

    /// <summary>An icon put on the session bus in the background: it is given at once, and shows when the bus answers.
    /// </summary>
    public static StatusNotifierIcon Start(string toolTip, INotificationAreaMenu menu, int id)
    {
        var icon = new StatusNotifierIcon(toolTip, menu, id, LoopSignal.PostAwaited);
        Task.Run(() =>
        {
            DBusConnection bus = null;
            try
            {
                bus = DBusConnection.ConnectSession();
                icon.Connect(bus);
            }
            catch (Exception e) when (e is DBusException or IOException or SocketException)
            {
                icon.Abandon(bus);
                Serilog.Log.Logger.Debug(e, "The application's status icon is on no session bus; it shows nowhere");
            }
            catch (Exception e)
            {
                icon.Abandon(bus);
                Serilog.Log.Logger.Warning(e, "The application's status icon could not be put on the session bus");
            }
        });
        return icon;
    }

    /// <summary>Serves the icon on <paramref name="bus"/>, which it closes when disposed, and tells the watcher of it.
    /// </summary>
    internal void Connect(DBusConnection bus)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                bus.Dispose();
                return;
            }

            _bus = bus;
        }

        var owned = bus.Call(DBusMessage.MethodCall(BusName, BusPath, BusName, "RequestName", "su", writer =>
        {
            writer.WriteString(Name);
            writer.WriteUInt32(DoNotQueue);
        })).Reader().ReadUInt32();
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _registeredAs = owned == PrimaryOwner ? Name : bus.UniqueName;
        }

        // Not under the lock: a subscription waits for the bus's answer, read on the thread the signals are heard on -
        // which takes the lock.
        List<IDisposable> served =
        [
            bus.Serve(Answer),
            // A watcher starting - the panel did, or restarted: the icon is told to it again.
            bus.Subscribe(BusName, BusPath, BusName, "NameOwnerChanged", OnNameOwnerChanged),
            // A panel coming or going on the watcher: whichever watcher runs, so from any sender.
            bus.Subscribe(null, WatcherPath, WatcherInterface, "StatusNotifierHostRegistered",
                _ => Task.Run(() => CheckDisplayed(bus))),
            bus.Subscribe(null, WatcherPath, WatcherInterface, "StatusNotifierHostUnregistered",
                _ => Task.Run(() => CheckDisplayed(bus))),
        ];
        lock (_lock)
        {
            if (_disposed)
            {
                foreach (var each in served)
                {
                    each.Dispose();
                }

                return;
            }

            _served.AddRange(served);
        }

        Register(bus);
    }

    // The connection did not take the icon: nothing is said on it any more.
    private void Abandon(DBusConnection bus)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_bus, bus))
            {
                _bus = null;
            }

            foreach (var served in _served)
            {
                served.Dispose();
            }

            _served.Clear();
        }

        bus?.Dispose();
    }

    // A signal of the icon's, where it is on a bus; one the bus can no longer take is left unsaid.
    private void Say(DBusMessage signal)
    {
        DBusConnection bus;
        lock (_lock)
        {
            bus = _disposed ? null : _bus;
        }

        try
        {
            bus?.Send(signal);
        }
        catch (Exception e) when (e is DBusException or IOException or SocketException or ObjectDisposedException)
        {
            Serilog.Log.Logger.Debug(e, "The session bus took no {Member} of the status icon", signal.Member);
        }
    }

    public void Dispose()
    {
        DBusConnection bus;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            bus = _bus;
            _bus = null;
            foreach (var served in _served)
            {
                served.Dispose();
            }

            _served.Clear();
        }

        // The bus name goes with the connection: the watcher takes the icon away when it sees that.
        _menu.Forget();
        bus?.Dispose();
    }

    private void Register(DBusConnection bus)
    {
        string name;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            name = _registeredAs;
        }

        try
        {
            bus.Call(DBusMessage.MethodCall(_watcher, WatcherPath, WatcherInterface, "RegisterStatusNotifierItem", "s",
                writer => writer.WriteString(name)));
        }
        catch (Exception e) when (e is DBusException or IOException or SocketException or ObjectDisposedException)
        {
            Serilog.Log.Logger.Debug(e, "No panel shows status icons now; the application's icon shows when one starts");
        }

        CheckDisplayed(bus);
    }

    // Asked of the watcher: whether it runs and a panel is registered with it now. One asking at a time, each applying
    // what it was told: an answer read before another can not land after it. Never on the thread the bus is read on,
    // which the answer comes through.
    private void CheckDisplayed(DBusConnection bus)
    {
        lock (_checking)
        {
            bool displayed;
            try
            {
                displayed = StatusNotifierArea.IsShownOn(bus, _watcher);
            }
            catch (Exception e) when (e is DBusException or IOException or SocketException or ObjectDisposedException)
            {
                Serilog.Log.Logger.Debug(e, "The watcher did not say whether a panel shows the status icons");
                displayed = false;
            }

            SetDisplayed(displayed);
        }
    }

    private void SetDisplayed(bool displayed)
    {
        lock (_lock)
        {
            if (_disposed || _displayed == displayed)
            {
                return;
            }

            _displayed = displayed;
        }

        _post(() => IsDisplayedChanged?.Invoke(this, EventArgs.Empty));
    }

    private void OnNameOwnerChanged(DBusMessage signal)
    {
        var reader = signal.Reader();
        var name = reader.ReadString();
        reader.ReadString();
        var owner = reader.ReadString();
        if (name != _watcher)
        {
            return;
        }

        DBusConnection bus;
        lock (_lock)
        {
            bus = _disposed ? null : _bus;
        }

        // Not on the thread the bus is read on, which the calls' answers have to come through. The watcher gone - the
        // panel ended - nothing shows the icon until one starts again; one started is told of the icon.
        if (bus != null)
        {
            Task.Run(owner.Length == 0 ? () => CheckDisplayed(bus) : () => Register(bus));
        }
    }

    private Task<DBusMessage> Answer(DBusMessage call)
    {
        if (call.Path == MenuPath)
        {
            return _menu.Answer(call);
        }

        if (call.Path != ItemPath)
        {
            return Task.FromResult<DBusMessage>(null);
        }

        return Task.FromResult(call.Interface switch
        {
            ItemInterface => Act(call),
            PropertiesInterface => StatusNotifierMenu.AnswerProperties(call, ItemInterface, Properties()),
            IntrospectableInterface when call.Member == "Introspect" =>
                DBusMessage.MethodReturn(call, "s", writer => writer.WriteString(Introspection)),
            _ => null,
        });
    }

    private DBusMessage Act(DBusMessage call)
    {
        switch (call.Member)
        {
            case "Activate":
                _post(() => Clicked?.Invoke(this, EventArgs.Empty));
                break;
            case "ContextMenu":
            {
                // A panel that leaves the menu to the application: where it was asked for, as the panel says.
                var reader = call.Reader();
                var point = new PixelPoint(reader.ReadInt32(), reader.ReadInt32());
                if (!_hasMenu)
                {
                    _post(() => MenuAsked?.Invoke(this, new NotificationAreaMenuEventArgs(point)));
                }

                break;
            }
            case "SecondaryActivate" or "Scroll":
                break;
            default:
                return null;
        }

        return DBusMessage.MethodReturn(call);
    }

    private Dictionary<string, DBusVariant> Properties()
    {
        var toolTip = ToolTip ?? string.Empty;
        var appId = WaylandPlatform.AppId;
        var pixmaps = Pixmaps.Value;
        return new Dictionary<string, DBusVariant>
        {
            ["Category"] = DBusVariant.String("ApplicationStatus"),
            ["Id"] = DBusVariant.String(appId),
            ["Title"] = DBusVariant.String(toolTip.Length > 0 ? toolTip : appId),
            ["Status"] = DBusVariant.String("Active"),
            ["WindowId"] = DBusVariant.Int32(0),
            // The pictures the application carries; without them, its icon by the name the desktop entry put in the theme.
            ["IconName"] = DBusVariant.String(pixmaps.Count > 0 ? string.Empty : appId),
            ["IconPixmap"] = new DBusVariant("a(iiay)", pixmaps),
            ["IconThemePath"] = DBusVariant.String(string.Empty),
            ["OverlayIconName"] = DBusVariant.String(string.Empty),
            ["OverlayIconPixmap"] = new DBusVariant("a(iiay)", new List<object>()),
            ["AttentionIconName"] = DBusVariant.String(string.Empty),
            ["AttentionIconPixmap"] = new DBusVariant("a(iiay)", new List<object>()),
            ["AttentionMovieName"] = DBusVariant.String(string.Empty),
            ["ToolTip"] = new DBusVariant("(sa(iiay)ss)",
                new object[] { string.Empty, new List<object>(), toolTip, string.Empty }),
            ["ItemIsMenu"] = DBusVariant.Boolean(false),
            ["Menu"] = DBusVariant.ObjectPath(_hasMenu ? MenuPath : NoMenuPath),
        };
    }

    // ARGB32 in network order - alpha, red, green, blue - as the protocol has it.
    private static List<object> ReadPixmaps()
    {
        List<object> pixmaps = [];
        if (ApplicationIcon.Load() is not { } icon)
        {
            return pixmaps;
        }

        foreach (var picture in icon.Pictures)
        {
            if (picture.Size > LargestPicture)
            {
                continue;
            }

            var rgba = picture.Rgba;
            var argb = new byte[rgba.Length];
            for (var at = 0; at < rgba.Length; at += 4)
            {
                argb[at] = rgba[at + 3];
                argb[at + 1] = rgba[at];
                argb[at + 2] = rgba[at + 1];
                argb[at + 3] = rgba[at + 2];
            }

            pixmaps.Add(new object[] { picture.Size, picture.Size, argb });
        }

        return pixmaps;
    }

    private const string Introspection = """
        <node>
          <interface name="org.kde.StatusNotifierItem">
            <property name="Category" type="s" access="read"/>
            <property name="Id" type="s" access="read"/>
            <property name="Title" type="s" access="read"/>
            <property name="Status" type="s" access="read"/>
            <property name="WindowId" type="i" access="read"/>
            <property name="IconName" type="s" access="read"/>
            <property name="IconPixmap" type="a(iiay)" access="read"/>
            <property name="IconThemePath" type="s" access="read"/>
            <property name="OverlayIconName" type="s" access="read"/>
            <property name="OverlayIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionIconName" type="s" access="read"/>
            <property name="AttentionIconPixmap" type="a(iiay)" access="read"/>
            <property name="AttentionMovieName" type="s" access="read"/>
            <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
            <property name="ItemIsMenu" type="b" access="read"/>
            <property name="Menu" type="o" access="read"/>
            <method name="Activate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="SecondaryActivate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="ContextMenu"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
            <method name="Scroll"><arg name="delta" type="i" direction="in"/><arg name="orientation" type="s" direction="in"/></method>
            <signal name="NewTitle"/>
            <signal name="NewIcon"/>
            <signal name="NewToolTip"/>
            <signal name="NewStatus"><arg name="status" type="s"/></signal>
          </interface>
        </node>
        """;
}
