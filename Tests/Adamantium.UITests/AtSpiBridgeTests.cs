using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.DBus;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Platforms.Linux.Accessibility;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>What an assistive technology on a Linux desktop meets: the application embedded in the desktop through the
/// registry, its elements served over D-Bus as AT-SPI objects - role, name, state, actions, text, selection - read on
/// the application's loop, and what changes told only while someone listens for it.</summary>
[TestFixture]
public class AtSpiBridgeTests
{
    private const string RootPath = "/org/a11y/atspi/accessible/root";
    private const string RegistryPath = "/org/a11y/atspi/registry";
    private const string RegistryInterface = "org.a11y.atspi.Registry";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string AccessibleInterface = "org.a11y.atspi.Accessible";
    private const string ActionInterface = "org.a11y.atspi.Action";
    private const string TextInterface = "org.a11y.atspi.Text";
    private const string EditableTextInterface = "org.a11y.atspi.EditableText";
    private const string SelectionInterface = "org.a11y.atspi.Selection";
    private const string ObjectEvent = "org.a11y.atspi.Event.Object";
    private const string StateChanged = "StateChanged";
    private const string TextChanged = "TextChanged";
    private const uint SentenceGranularity = 2;
    private const int CheckedState = 4;
    private const int CheckableState = 41;

    private FakeRegistry _registry;
    private DBusConnection _bus;
    private Loop _loop;
    private AtSpiBridge _bridge;

    [SetUp]
    public void Embed()
    {
        Assume.That(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
            Is.Not.Null.And.Not.Empty, "no session bus here");
        _registry = new FakeRegistry();
        _bus = DBusConnection.ConnectSession();
        _loop = new Loop();
        _bridge = AtSpiBridge.Attach(_bus, _registry.Bus.UniqueName, _loop.Post);
    }

    [TearDown]
    public void LetGo()
    {
        _bridge?.Dispose();
        _registry?.Dispose();
        _loop?.Dispose();
    }

    [Test]
    public void TheApplication_IsEmbeddedInTheDesktop_AndNamesItAsItsParent()
    {
        Assert.That(_registry.Embedded, Is.EqualTo((_bus.UniqueName, RootPath)));

        var parent = (object[])Property(RootPath, AccessibleInterface, "Parent");
        Assert.That(parent[0], Is.EqualTo(_registry.Bus.UniqueName));
        Assert.That(((DBusObjectPath)parent[1]).Value, Is.EqualTo(RootPath));
        Assert.That(Call(RootPath, AccessibleInterface, "GetRole").ReadUInt32(), Is.EqualTo((uint)AtSpiRole.Application));
    }

    [Test]
    public void AnElement_IsServedWithItsRoleNameAndState_ReadOnTheLoop()
    {
        var check = new CheckBox { Content = "Bold", IsChecked = true };
        var path = _bridge.PathOf(check.GetAutomationPeer());
        var before = _loop.Ran;

        Assert.That(Call(path, AccessibleInterface, "GetRole").ReadUInt32(), Is.EqualTo((uint)AtSpiRole.CheckBox));
        Assert.That(Call(path, AccessibleInterface, "GetRoleName").ReadString(), Is.EqualTo("check box"));
        Assert.That(Property(path, AccessibleInterface, "Name"), Is.EqualTo("Bold"));
        Assert.That(States(path), Does.Contain(CheckedState).And.Contain(CheckableState));
        Assert.That(_loop.Ran - before, Is.EqualTo(4), "every call is answered on the loop");
    }

    [Test]
    public void AnAction_IsDoneOverTheBus()
    {
        var check = new CheckBox { Content = "Bold" };
        var path = _bridge.PathOf(check.GetAutomationPeer());

        var actions = Call(path, ActionInterface, "GetActions");
        var end = actions.ReadArrayEnd(8);
        List<string> names = [];
        while (actions.Position < end)
        {
            actions.Align(8);
            names.Add(actions.ReadString());
            actions.ReadString();
            actions.ReadString();
        }

        Assert.That(names, Is.EqualTo(new[] { "toggle" }));
        Assert.That(Call(path, ActionInterface, "DoAction", "i", writer => writer.WriteInt32(0)).ReadBoolean(), Is.True);
        Call(RootPath, AccessibleInterface, "GetRole");
        Assert.That(check.IsChecked, Is.True, "done on the loop after the answer, as GTK does: a long action holds no one");
        Assert.That(Call(path, ActionInterface, "DoAction", "i", writer => writer.WriteInt32(5)).ReadBoolean(), Is.False);
    }

    [Test]
    public void ACallTheLoopNeverReached_IsRefused_AndItsWorkIsNotDoneLater()
    {
        var check = new CheckBox { Content = "Bold" };
        var path = _bridge.PathOf(check.GetAutomationPeer());
        _loop.Hold();

        Assert.Throws<DBusException>(() => Call(path, ActionInterface, "DoAction", "i", writer => writer.WriteInt32(0)));
        _loop.Release();
        Call(path, AccessibleInterface, "GetRole");

        Assert.That(check.IsChecked, Is.Not.True, "the client was told it failed: it must not happen after all");
    }

    [Test]
    public void AList_ServesItsItems_AndIsSelectedIn()
    {
        var list = new ListBox();
        list.Items.Add("Red");
        list.Items.Add("Green");
        list.Items.Add("Blue");
        var path = _bridge.PathOf(list.GetAutomationPeer());

        var children = References(Call(path, AccessibleInterface, "GetChildren"));
        Assert.That(children, Has.Count.EqualTo(3));
        Assert.That(Call(children[1], AccessibleInterface, "GetRole").ReadUInt32(), Is.EqualTo((uint)AtSpiRole.ListItem));

        Assert.That(Call(path, SelectionInterface, "SelectChild", "i", writer => writer.WriteInt32(1)).ReadBoolean(), Is.True);
        Assert.That(list.SelectedIndex, Is.EqualTo(1));
        Assert.That(Call(path, SelectionInterface, "IsChildSelected", "i", writer => writer.WriteInt32(1)).ReadBoolean(), Is.True);
        Assert.That(Property(path, SelectionInterface, "NSelectedChildren"), Is.EqualTo(1));
    }

    [Test]
    public void Text_IsReadByTheSentence_AndEditedOverTheBus()
    {
        var box = new TextBox { Text = "Hello world. Again here" };
        var path = _bridge.PathOf(box.GetAutomationPeer());

        Assert.That(GetText(path, 0, -1), Is.EqualTo("Hello world. Again here"));

        var sentence = Call(path, TextInterface, "GetStringAtOffset", "iu", writer =>
        {
            writer.WriteInt32(12);
            writer.WriteUInt32(SentenceGranularity);
        });
        Assert.That((sentence.ReadString(), sentence.ReadInt32(), sentence.ReadInt32()), Is.EqualTo(("Hello world. ", 0, 13)));

        Assert.That(TwoInts(path, EditableTextInterface, "DeleteText", 0, 6).ReadBoolean(), Is.True);
        Assert.That(box.Text, Is.EqualTo("world. Again here"));

        var inserted = Call(path, EditableTextInterface, "InsertText", "isi", writer =>
        {
            writer.WriteInt32(0);
            writer.WriteString("Big ");
            writer.WriteInt32(-1);
        });
        Assert.That(inserted.ReadBoolean(), Is.True);
        Assert.That(box.Text, Is.EqualTo("Big world. Again here"), "a length of -1 inserts the whole string");

        Call(path, EditableTextInterface, "InsertText", "isi", writer =>
        {
            writer.WriteInt32(0);
            writer.WriteString("é!");
            writer.WriteInt32(2);
        });
        Assert.That(box.Text, Does.StartWith("éBig"), "the length counts UTF-8 bytes: é is two");
    }

    [Test]
    public void ABridgeLetGo_ListensToNothingAnyMore()
    {
        Listen("object:");
        Assert.That(AutomationEvents.IsListening, Is.True);

        _bridge.Dispose();
        _bridge = null;

        Assert.That(AutomationEvents.IsListening, Is.False);
    }

    [Test]
    public void Offsets_CountCharacters_NotUtf16Units()
    {
        var box = new TextBox { Text = "a😀b" };
        var path = _bridge.PathOf(box.GetAutomationPeer());

        Assert.That(Property(path, TextInterface, "CharacterCount"), Is.EqualTo(3));
        Assert.That(Call(path, TextInterface, "GetCharacterAtOffset", "i", writer => writer.WriteInt32(1)).ReadInt32(),
            Is.EqualTo(0x1F600));
        Assert.That(GetText(path, 1, 2), Is.EqualTo("😀"));
        Assert.That(GetText(path, 2, 3), Is.EqualTo("b"));

        TwoInts(path, EditableTextInterface, "DeleteText", 1, 2);
        Assert.That(box.Text, Is.EqualTo("ab"));
    }

    [Test]
    public void APassword_ShowsNoText()
    {
        var box = new PasswordBox { Password = "secret" };
        var path = _bridge.PathOf(box.GetAutomationPeer());

        var interfaces = Strings(Call(path, AccessibleInterface, "GetInterfaces"));
        Assert.That(interfaces, Does.Not.Contain(TextInterface).And.Not.Contain(EditableTextInterface));
        Assert.Throws<DBusException>(() => GetText(path, 0, -1));
        Assert.Throws<DBusException>(() => TwoInts(path, EditableTextInterface, "CopyText", 0, -1));
    }

    [Test]
    public void AnObjectNeverServed_IsUnknown()
    {
        var error = Assert.Throws<DBusException>(() => Call("/org/a11y/atspi/accessible/987654321", AccessibleInterface, "GetRole"));
        Assert.That(error.Name, Is.EqualTo("org.freedesktop.DBus.Error.UnknownObject"));
    }

    [Test]
    public void AChange_IsToldOnlyWhileSomeoneListensForIt()
    {
        var check = new CheckBox { Content = "Bold" };
        var path = _bridge.PathOf(check.GetAutomationPeer());
        var told = Hear(StateChanged);

        check.IsChecked = true;
        Call(RootPath, AccessibleInterface, "GetRole");
        Assert.That(told.TryTake(out _, TimeSpan.FromMilliseconds(300)), Is.False, "told with no one listening");

        Listen("object:state-changed:checked");
        check.IsChecked = false;

        Assert.That(told.TryTake(out var change, TimeSpan.FromSeconds(5)), Is.True, "not told while someone listens");
        Assert.That(change, Is.EqualTo((path, "checked", 0, (object)0)));
    }

    [Test]
    public void AListenerRegisteredTwice_IsStillToldAfterOneOfThemGoes()
    {
        var check = new CheckBox { Content = "Bold" };
        _bridge.PathOf(check.GetAutomationPeer());
        var told = Hear(StateChanged);
        Listen("Object:StateChanged:checked");
        Listen("Object:StateChanged:checked");

        Forget("Object:StateChanged:checked");
        check.IsChecked = true;

        Assert.That(told.TryTake(out _, TimeSpan.FromSeconds(5)), Is.True);
    }

    [Test]
    public void Focus_MovingOn_IsToldToTheElementItLeft()
    {
        var first = new CheckBox { Content = "First" };
        var second = new CheckBox { Content = "Second" };
        var firstPath = _bridge.PathOf(first.GetAutomationPeer());
        var secondPath = _bridge.PathOf(second.GetAutomationPeer());
        var told = Hear(StateChanged);
        Listen("object:state-changed:focused");

        AutomationEvents.Raise(first, AutomationEvent.FocusChanged);
        AutomationEvents.Raise(second, AutomationEvent.FocusChanged);

        Assert.That(Next(told), Is.EqualTo((firstPath, "focused", 1, (object)0)));
        Assert.That(Next(told), Is.EqualTo((firstPath, "focused", 0, (object)0)));
        Assert.That(Next(told), Is.EqualTo((secondPath, "focused", 1, (object)0)));
    }

    [Test]
    public void Typing_InTheFocusedBox_IsToldInCharacters_AndAnotherBoxChangingDoesNotStopIt()
    {
        var box = new TextBox { Text = "a😀" };
        var status = new TextBox { Text = "idle" };
        var path = _bridge.PathOf(box.GetAutomationPeer());
        _bridge.PathOf(status.GetAutomationPeer());
        var told = Hear(TextChanged);
        Listen("object:text-changed");

        AutomationEvents.Raise(box, AutomationEvent.FocusChanged);
        status.Text = "busy";
        box.Text = "a😀b";
        box.Text = "a😁b";

        Assert.That(Next(told), Is.EqualTo((path, "insert", 2, (object)"b")));
        Assert.That(Next(told), Is.EqualTo((path, "delete", 1, (object)"😀")), "a pair is never split");
        Assert.That(Next(told), Is.EqualTo((path, "insert", 1, (object)"😁")));
    }

    private BlockingCollection<(string Path, string Detail, int Detail1, object Data)> Hear(string member)
    {
        var told = new BlockingCollection<(string, string, int, object)>();
        _registry.Subscriptions.Add(_registry.Bus.Subscribe(_bus.UniqueName, null, ObjectEvent, member, signal =>
        {
            var reader = signal.Reader();
            var detail = reader.ReadString();
            var detail1 = reader.ReadInt32();
            reader.ReadInt32();
            told.Add((signal.Path, detail, detail1, reader.ReadVariant().Value));
        }));
        return told;
    }

    private static (string, string, int, object) Next(BlockingCollection<(string, string, int, object)> told)
    {
        Assert.That(told.TryTake(out var next, TimeSpan.FromSeconds(5)), Is.True, "nothing told");
        return next;
    }

    private void Listen(string eventName) => Tell(() => _registry.Listen(eventName));

    private void Forget(string eventName) => Tell(() => _registry.Forget(eventName));

    // The registry tells of a change; the bridge reads the whole list again, and the test waits until it has.
    private void Tell(Action change)
    {
        var read = _bridge.ListenerListsRead;
        change();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_bridge.ListenerListsRead == read)
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "the bridge never read the listeners again");
            Thread.Sleep(10);
        }
    }

    private DBusReader Call(string path, string interfaceName, string member, string signature = "", Action<DBusWriter> body = null) =>
        _registry.Bus.Call(DBusMessage.MethodCall(_bus.UniqueName, path, interfaceName, member, signature, body)).Reader();

    private DBusReader TwoInts(string path, string interfaceName, string member, int first, int second) =>
        Call(path, interfaceName, member, "ii", writer =>
        {
            writer.WriteInt32(first);
            writer.WriteInt32(second);
        });

    private string GetText(string path, int start, int end) => TwoInts(path, TextInterface, "GetText", start, end).ReadString();

    private object Property(string path, string interfaceName, string name) =>
        Call(path, PropertiesInterface, "Get", "ss", writer =>
        {
            writer.WriteString(interfaceName);
            writer.WriteString(name);
        }).ReadVariant().Value;

    private List<int> States(string path)
    {
        var reader = Call(path, AccessibleInterface, "GetState");
        var end = reader.ReadArrayEnd(4);
        List<int> states = [];
        for (var word = 0; reader.Position < end; word++)
        {
            var bits = reader.ReadUInt32();
            for (var bit = 0; bit < 32; bit++)
            {
                if ((bits & (1u << bit)) != 0)
                {
                    states.Add(word * 32 + bit);
                }
            }
        }

        return states;
    }

    private static List<string> References(DBusReader reader)
    {
        var end = reader.ReadArrayEnd(8);
        List<string> paths = [];
        while (reader.Position < end)
        {
            reader.Align(8);
            reader.ReadString();
            paths.Add(reader.ReadObjectPath().Value);
        }

        return paths;
    }

    private static List<string> Strings(DBusReader reader)
    {
        var end = reader.ReadArrayEnd(4);
        List<string> strings = [];
        while (reader.Position < end)
        {
            strings.Add(reader.ReadString());
        }

        return strings;
    }

    // The application's loop: one thread that runs what is posted to it, in order - or holds it, as a busy loop does.
    private sealed class Loop : IDisposable
    {
        private readonly BlockingCollection<Action> _work = new();
        private readonly ManualResetEventSlim _free = new(true);
        private readonly Thread _thread;
        private int _ran;

        public Loop()
        {
            _thread = new Thread(() =>
            {
                foreach (var work in _work.GetConsumingEnumerable())
                {
                    _free.Wait();
                    work();
                    Interlocked.Increment(ref _ran);
                }
            }) { IsBackground = true, Name = "AT-SPI test loop" };
            _thread.Start();
        }

        public int Ran => Volatile.Read(ref _ran);

        public void Post(Action work) => _work.Add(work);

        public void Hold() => _free.Reset();

        public void Release() => _free.Set();

        public void Dispose()
        {
            _free.Set();
            _work.CompleteAdding();
            _thread.Join();
        }
    }

    private sealed class FakeRegistry : IDisposable
    {
        private readonly IDisposable _served;

        public FakeRegistry()
        {
            _served = Bus.Serve(call => Task.FromResult(Answer(call)));
        }

        public DBusConnection Bus { get; } = DBusConnection.ConnectSession();

        public List<IDisposable> Subscriptions { get; } = [];

        public (string Name, string Path) Embedded { get; private set; }

        private List<string> Listeners { get; } = [];

        public void Listen(string eventName)
        {
            lock (Listeners)
            {
                Listeners.Add(eventName);
            }

            Signal("EventListenerRegistered", eventName);
        }

        public void Forget(string eventName)
        {
            lock (Listeners)
            {
                Listeners.Remove(eventName);
            }

            Signal("EventListenerDeregistered", eventName);
        }

        private void Signal(string member, string eventName) => Bus.Send(DBusMessage.Signal(RegistryPath, RegistryInterface,
            member, "ssas", writer =>
            {
                writer.WriteString(Bus.UniqueName);
                writer.WriteString(eventName);
                writer.WriteArray(4, _ => { });
            }));

        private DBusMessage Answer(DBusMessage call)
        {
            switch (call.Member)
            {
                case "Embed":
                {
                    var reader = call.Reader();
                    Embedded = (reader.ReadString(), reader.ReadObjectPath().Value);
                    return DBusMessage.MethodReturn(call, "(so)", writer => writer.WriteStruct(fields =>
                    {
                        fields.WriteString(Bus.UniqueName);
                        fields.WriteObjectPath(RootPath);
                    }));
                }
                case "GetRegisteredEvents":
                {
                    List<string> listeners;
                    lock (Listeners)
                    {
                        listeners = [.. Listeners];
                    }

                    return DBusMessage.MethodReturn(call, "a(ss)", writer => writer.WriteArray(8, entries =>
                    {
                        foreach (var listener in listeners)
                        {
                            entries.WriteStruct(fields =>
                            {
                                fields.WriteString(Bus.UniqueName);
                                fields.WriteString(listener);
                            });
                        }
                    }));
                }
                default:
                    return null;
            }
        }

        public void Dispose()
        {
            foreach (var subscription in Subscriptions)
            {
                subscription.Dispose();
            }

            _served.Dispose();
            Bus.Dispose();
        }
    }
}
