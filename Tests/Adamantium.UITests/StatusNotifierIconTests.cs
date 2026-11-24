using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Adamantium.DBus;
using Adamantium.UI.Core;
using Adamantium.UI.Platforms.Linux;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What a Linux panel meets of the application's status icon: an item on a bus name of its own, told to the
/// panel's watcher - again when a watcher starts - with its properties, a click and the menu it shows itself, read from
/// the application as it stands, a row clicked there picked in the application.</summary>
[TestFixture]
[NonParallelizable]
public class StatusNotifierIconTests
{
    private const string ItemPath = "/StatusNotifierItem";
    private const string MenuPath = "/MenuBar";
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private const string MenuInterface = "com.canonical.dbusmenu";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private string _watcherName;
    private FakeWatcher _watcher;
    private DBusConnection _panel;
    private StatusNotifierIcon _icon;
    private readonly ConcurrentQueue<Action> _posted = new();

    [SetUp]
    public void SetUp()
    {
        Assume.That(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ??
                    Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), Is.Not.Null.And.Not.Empty, "no session bus here");
        _watcherName = $"org.adamantium.TestWatcher.N{Guid.NewGuid():N}";
        _watcher = new FakeWatcher(_watcherName);
        _panel = DBusConnection.ConnectSession();
        _icon = new StatusNotifierIcon("Keyboard", 1, Post, _watcherName);
    }

    [TearDown]
    public void TearDown()
    {
        _icon?.Dispose();
        _panel?.Dispose();
        _watcher?.Dispose();
    }

    // Told by way of the loop: the menu the application gives right after asking for the icon is offered from the first.
    [Test]
    public void TheIcon_IsToldToTheWatcher_OnABusNameOfItsOwn_WithWhatTheLoopGaveItFirst()
    {
        _icon.Connect(DBusConnection.ConnectSession());
        var beforeTheLoop = _watcher.Registered.Count;
        _icon.Menu = () => [new NotificationAreaMenuItem { Text = "Show" }];
        RunPosted();

        Assert.That(SpinWait.SpinUntil(() => _watcher.Registered.Count > 0, Patience), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(beforeTheLoop, Is.Zero, "told after the loop's turn");
            Assert.That(_watcher.Registered, Is.EqualTo(new[] { _icon.Name }));
            Assert.That(_icon.Name, Does.StartWith($"org.kde.StatusNotifierItem-{Environment.ProcessId}-"));
            Assert.That(((DBusObjectPath)All(ItemPath, ItemInterface)["Menu"].Value).Value, Is.EqualTo(MenuPath));
        });
    }

    // Its name held by another - a sandboxed process with the same process id: the watcher is told the connection's own.
    [Test]
    public void ItsNameHeldByAnother_TheWatcherIsToldTheConnectionsOwnName()
    {
        using var other = DBusConnection.ConnectSession();
        other.Call(DBusMessage.MethodCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus",
            "RequestName", "su", writer =>
            {
                writer.WriteString(_icon.Name);
                writer.WriteUInt32(4);
            }));
        var bus = DBusConnection.ConnectSession();

        _icon.Connect(bus);
        RunPosted();

        Assert.That(SpinWait.SpinUntil(() => _watcher.Registered.Count > 0, Patience), Is.True);
        Assert.That(_watcher.Registered, Is.EqualTo(new[] { bus.UniqueName }));
    }

    [Test]
    public void ItsBusGone_AToolTipChangedIsLeftUnsaid()
    {
        var bus = DBusConnection.ConnectSession();
        _icon.Connect(bus);
        bus.Dispose();

        Assert.DoesNotThrow(() => _icon.ToolTip = "Keys");
    }

    [Test]
    public void AWatcherStartingLater_IsToldOfTheIcon()
    {
        _watcher.Dispose();
        _icon.Connect(DBusConnection.ConnectSession());
        RunPosted();

        _watcher = new FakeWatcher(_watcherName);

        Assert.That(SpinWait.SpinUntil(() => _watcher.Registered.Count > 0, Patience), Is.True, "told when it started");
        Assert.That(_watcher.Registered, Is.EqualTo(new[] { _icon.Name }));
    }

    [Test]
    public void ItsProperties_SayItsToolTip_AndThatItIsNoMenuItself()
    {
        _icon.Connect(DBusConnection.ConnectSession());

        var properties = All(ItemPath, ItemInterface);
        var toolTip = (object[])properties["ToolTip"].Value;

        Assert.Multiple(() =>
        {
            Assert.That(properties["Title"].Value, Is.EqualTo("Keyboard"));
            Assert.That(toolTip[2], Is.EqualTo("Keyboard"));
            Assert.That(properties["Status"].Value, Is.EqualTo("Active"));
            Assert.That(properties["ItemIsMenu"].Value, Is.False);
            Assert.That(((DBusObjectPath)properties["Menu"].Value).Value, Is.EqualTo("/NO_DBUSMENU"), "no menu given");
        });
    }

    [Test]
    public void ToolTipChanged_IsSaid_AndReadAnew()
    {
        _icon.Connect(DBusConnection.ConnectSession());
        var said = new ManualResetEventSlim();
        using var heard = _panel.Subscribe(_icon.Name, ItemPath, ItemInterface, "NewToolTip", _ => said.Set());

        _icon.ToolTip = "Keys";

        Assert.That(said.Wait(Patience), Is.True, "NewToolTip");
        Assert.That(((object[])All(ItemPath, ItemInterface)["ToolTip"].Value)[2], Is.EqualTo("Keys"));
    }

    [Test]
    public void Activated_ItIsClicked_OnTheLoop()
    {
        _icon.Connect(DBusConnection.ConnectSession());
        var clickedOn = new List<int>();
        _icon.Clicked += (_, _) => clickedOn.Add(Environment.CurrentManagedThreadId);

        Call(ItemPath, ItemInterface, "Activate", "ii", writer =>
        {
            writer.WriteInt32(10);
            writer.WriteInt32(20);
        });
        RunPosted();

        // The posted work runs on this thread here - the loop's stand-in - not on the thread the bus is read on.
        Assert.That(clickedOn, Is.EqualTo(new[] { Environment.CurrentManagedThreadId }));
    }

    [Test]
    public void ItsMenuAsked_WithoutAMenu_IsTold_AtThePoint()
    {
        _icon.Connect(DBusConnection.ConnectSession());
        PixelPoint? asked = null;
        _icon.MenuAsked += (_, e) => asked = e.Point;

        Call(ItemPath, ItemInterface, "ContextMenu", "ii", writer =>
        {
            writer.WriteInt32(300);
            writer.WriteInt32(40);
        });
        RunPosted();

        Assert.That(asked, Is.EqualTo(new PixelPoint(300, 40)));
    }

    [Test]
    public void WithAMenu_ThePanelShowsIt_RowsAsTheyStand()
    {
        var picked = new List<string>();
        var docked = false;
        _icon.Menu = () =>
        [
            new NotificationAreaMenuItem { Text = "Show", Picked = () => picked.Add("Show") },
            new NotificationAreaMenuItem { Text = "Along the bottom", IsCheckable = true, IsChecked = docked },
            new NotificationAreaMenuItem { Text = "Off", IsEnabled = false, Picked = () => picked.Add("Off") },
            NotificationAreaMenuItem.Separator,
            new NotificationAreaMenuItem { Text = "More", Items = [new NotificationAreaMenuItem { Text = "Inner" }] },
        ];
        _icon.Connect(DBusConnection.ConnectSession());
        PixelPoint? asked = null;
        _icon.MenuAsked += (_, e) => asked = e.Point;

        var menuPath = ((DBusObjectPath)All(ItemPath, ItemInterface)["Menu"].Value).Value;
        var layout = Layout();
        var rows = ((List<object>)layout[2]).Select(row => (object[])((DBusVariant)row).Value).ToList();
        Call(MenuPath, MenuInterface, "Event", "isvu", writer =>
        {
            writer.WriteInt32((int)rows[0][0]);
            writer.WriteString("clicked");
            writer.WriteVariant(DBusVariant.Int32(0));
            writer.WriteUInt32(0);
        });
        Call(MenuPath, MenuInterface, "Event", "isvu", writer =>
        {
            writer.WriteInt32((int)rows[2][0]);
            writer.WriteString("clicked");
            writer.WriteVariant(DBusVariant.Int32(0));
            writer.WriteUInt32(0);
        });
        Call(ItemPath, ItemInterface, "ContextMenu", "ii", writer =>
        {
            writer.WriteInt32(1);
            writer.WriteInt32(1);
        });
        RunPosted();
        var unchanged = AboutToShow();
        docked = true;
        var changed = AboutToShow();

        Assert.Multiple(() =>
        {
            Assert.That(menuPath, Is.EqualTo(MenuPath));
            Assert.That(rows.Select(row => Label(row) ?? Type(row)),
                Is.EqualTo(new[] { "Show", "Along the bottom", "Off", "separator", "More" }));
            Assert.That(Property(rows[1], "toggle-type"), Is.EqualTo("checkmark"));
            Assert.That(Property(rows[1], "toggle-state"), Is.EqualTo(0));
            Assert.That(Property(rows[2], "enabled"), Is.False);
            Assert.That(Property(rows[4], "children-display"), Is.EqualTo("submenu"));
            Assert.That(((List<object>)rows[4][2]).Select(row => Label((object[])((DBusVariant)row).Value)),
                Is.EqualTo(new[] { "Inner" }));
            Assert.That(picked, Is.EqualTo(new[] { "Show" }), "a row turned off is not picked");
            Assert.That(asked, Is.Null, "the panel shows the menu: none is opened by the application");
            Assert.That(unchanged, Is.False);
            Assert.That(changed, Is.True, "the check turned");
        });
    }

    // The menu read changed is said, for panels that ask for the layout again only when told; a click names the row of the
    // layout the panel was given, though rows came in above it since.
    [Test]
    public void AMenuChanged_IsSaid_AndAClickNamesTheRowThePanelWasGiven()
    {
        var picked = new List<string>();
        var withNew = false;
        _icon.Menu = () => withNew
            ? [new NotificationAreaMenuItem { Text = "New", Picked = () => picked.Add("New") },
               new NotificationAreaMenuItem { Text = "my_project", Picked = () => picked.Add("my_project") }]
            : [new NotificationAreaMenuItem { Text = "my_project", Picked = () => picked.Add("my_project") }];
        _icon.Connect(DBusConnection.ConnectSession());
        var said = new ManualResetEventSlim();
        using var heard = _panel.Subscribe(_icon.Name, MenuPath, MenuInterface, "LayoutUpdated", _ => said.Set());

        var row = (object[])((DBusVariant)((List<object>)Layout()[2])[0]).Value;
        said.Reset();
        withNew = true;
        var changed = AboutToShow();
        Call(MenuPath, MenuInterface, "Event", "isvu", writer =>
        {
            writer.WriteInt32((int)row[0]);
            writer.WriteString("clicked");
            writer.WriteVariant(DBusVariant.Int32(0));
            writer.WriteUInt32(0);
        });
        RunPosted();

        Assert.Multiple(() =>
        {
            Assert.That(Label(row), Is.EqualTo("my__project"), "an underscore is no access key here");
            Assert.That(changed, Is.True);
            Assert.That(said.Wait(Patience), Is.True, "LayoutUpdated");
            Assert.That(picked, Is.EqualTo(new[] { "my_project" }), "the row the panel showed, not the new one");
        });
    }

    // Icons are shown where a watcher runs with a panel registered - not with no watcher (GNOME without the AppIndicator
    // extension), nor with a watcher no panel shows.
    [Test]
    public void TheIconsAreShown_WhereAWatcherRunsWithAPanel()
    {
        var withPanel = StatusNotifierArea.IsShownOn(_panel, _watcherName);
        _watcher.HasPanel = false;
        var withoutPanel = StatusNotifierArea.IsShownOn(_panel, _watcherName);
        var withoutWatcher = StatusNotifierArea.IsShownOn(_panel, $"org.adamantium.NoWatcher.N{Guid.NewGuid():N}");

        Assert.Multiple(() =>
        {
            Assert.That(withPanel, Is.True);
            Assert.That(withoutPanel, Is.False);
            Assert.That(withoutWatcher, Is.False);
        });
    }

    [Test]
    public void TakenAway_ItsBusNameGoes()
    {
        _icon.Connect(DBusConnection.ConnectSession());
        var name = _icon.Name;

        _icon.Dispose();

        Assert.That(SpinWait.SpinUntil(() => !HasOwner(name), Patience), Is.True);
    }

    private void Post(Action work) => _posted.Enqueue(work);

    // What the loop would run, run here; the work a call waits on is run while it waits.
    private void RunPosted()
    {
        while (_posted.TryDequeue(out var work))
        {
            work();
        }
    }

    private DBusReader Call(string path, string interfaceName, string member, string signature = "",
        Action<DBusWriter> body = null)
    {
        var reply = _panel.CallAsync(DBusMessage.MethodCall(_icon.Name, path, interfaceName, member, signature, body));
        while (!reply.IsCompleted)
        {
            RunPosted();
            Thread.Sleep(1);
        }

        return reply.GetAwaiter().GetResult().Reader();
    }

    private Dictionary<string, DBusVariant> All(string path, string interfaceName)
    {
        var entries = (Dictionary<object, object>)Call(path, PropertiesInterface, "GetAll", "s",
            writer => writer.WriteString(interfaceName)).ReadValue("a{sv}");
        return entries.ToDictionary(entry => (string)entry.Key, entry => (DBusVariant)entry.Value);
    }

    private object[] Layout()
    {
        var reader = Call(MenuPath, MenuInterface, "GetLayout", "iias", writer =>
        {
            writer.WriteInt32(0);
            writer.WriteInt32(-1);
            writer.WriteArray(4, _ => { });
        });
        reader.ReadUInt32();
        return (object[])reader.ReadValue("(ia{sv}av)");
    }

    private bool AboutToShow() =>
        Call(MenuPath, MenuInterface, "AboutToShow", "i", writer => writer.WriteInt32(0)).ReadBoolean();

    private static Dictionary<object, object> Properties(object[] row) => (Dictionary<object, object>)row[1];

    private static object Property(object[] row, string name) => ((DBusVariant)Properties(row)[name]).Value;

    private static string Label(object[] row) =>
        Properties(row).TryGetValue("label", out var label) ? (string)((DBusVariant)label).Value : null;

    private static string Type(object[] row) =>
        Properties(row).TryGetValue("type", out var type) ? (string)((DBusVariant)type).Value : null;

    private bool HasOwner(string name) => _panel.Call(DBusMessage.MethodCall("org.freedesktop.DBus",
        "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s", writer => writer.WriteString(name)))
        .Reader().ReadBoolean();

    // The panel's StatusNotifierWatcher: the names of the items it was told of.
    private sealed class FakeWatcher : IDisposable
    {
        private readonly DBusConnection _bus = DBusConnection.ConnectSession();
        private readonly ConcurrentQueue<string> _registered = new();
        private readonly IDisposable _served;

        public FakeWatcher(string name)
        {
            _served = _bus.Serve(call => System.Threading.Tasks.Task.FromResult(call.Member switch
            {
                "RegisterStatusNotifierItem" => Registering(call),
                "Get" => DBusMessage.MethodReturn(call, "v", writer => writer.WriteVariant(DBusVariant.Boolean(HasPanel))),
                _ => null,
            }));
            _bus.Call(DBusMessage.MethodCall("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus",
                "RequestName", "su", writer =>
                {
                    writer.WriteString(name);
                    writer.WriteUInt32(4);
                }));
        }

        public IReadOnlyList<string> Registered => _registered.ToList();

        /// <summary>A panel showing the icons is registered with it (IsStatusNotifierHostRegistered).</summary>
        public bool HasPanel { get; set; } = true;

        public void Dispose()
        {
            _served.Dispose();
            _bus.Dispose();
        }

        private DBusMessage Registering(DBusMessage call)
        {
            _registered.Enqueue(call.Reader().ReadString());
            return DBusMessage.MethodReturn(call);
        }
    }
}
