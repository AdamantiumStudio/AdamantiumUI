using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Adamantium.DBus;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>A status icon's menu as the panel shows it itself (com.canonical.dbusmenu, version 3): read from the
/// application on its loop thread each time the panel is about to show it or asks for its layout, its rows numbered in
/// the order they stand - 0 the menu itself - and a row the panel says was clicked picked on the loop thread. A menu read
/// changed is said with LayoutUpdated, which <paramref name="say"/> puts on the bus.</summary>
internal sealed class StatusNotifierMenu(INotificationAreaMenu menu, Action<Action> post, Action<DBusMessage> say)
{
    internal const string MenuInterface = "com.canonical.dbusmenu";

    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string IntrospectableInterface = "org.freedesktop.DBus.Introspectable";
    private const string ItemSignature = "(ia{sv}av)";

    private readonly object _lock = new();
    // The rows as last read, by number; 0 is the menu itself.
    private List<Row> _rows = [new Row(null, [])];
    // The rows of the layout the panel was given last: its numbers name these, until it asks for the layout again - a
    // menu read changed meanwhile does not turn a click on one row into a click on another.
    private List<Row> _given = [new Row(null, [])];
    private string _shape = string.Empty;
    private uint _revision = 1;
    // Dropped with the icon taken away.
    private volatile INotificationAreaMenu _menu = menu;

    /// <summary>Drops the menu and the rows read: what they would run belongs to an icon taken away.</summary>
    public void Forget()
    {
        _menu = null;
        lock (_lock)
        {
            _rows = [new Row(null, [])];
            _given = _rows;
        }
    }

    public async Task<DBusMessage> Answer(DBusMessage call)
    {
        switch (call.Interface)
        {
            case PropertiesInterface:
                return AnswerProperties(call, MenuInterface, new Dictionary<string, DBusVariant>
                {
                    ["Version"] = DBusVariant.UInt32(3),
                    ["TextDirection"] = DBusVariant.String("ltr"),
                    ["Status"] = DBusVariant.String("normal"),
                    ["IconThemePath"] = new DBusVariant("as", new List<object>()),
                });
            case IntrospectableInterface when call.Member == "Introspect":
                return DBusMessage.MethodReturn(call, "s", writer => writer.WriteString(Introspection));
            case MenuInterface:
                break;
            default:
                return null;
        }

        var reader = call.Reader();
        switch (call.Member)
        {
            case "GetLayout":
            {
                var parent = reader.ReadInt32();
                var depth = reader.ReadInt32();
                await ReadMenuAsync();
                lock (_lock)
                {
                    _given = _rows;
                    var revision = _revision;
                    var layout = parent >= 0 && parent < _rows.Count ? Layout(parent, depth) : Layout(0, 0);
                    return DBusMessage.MethodReturn(call, "u" + ItemSignature, writer =>
                    {
                        writer.WriteUInt32(revision);
                        writer.WriteValue(ItemSignature, layout);
                    });
                }
            }
            case "GetGroupProperties":
            {
                var ids = (List<object>)reader.ReadValue("ai");
                lock (_lock)
                {
                    List<object> found = [];
                    foreach (int id in ids)
                    {
                        if (id >= 0 && id < _given.Count)
                        {
                            found.Add(new object[] { id, PropertiesOf(_given[id].Item) });
                        }
                    }

                    return DBusMessage.MethodReturn(call, "a(ia{sv})", writer => writer.WriteValue("a(ia{sv})", found));
                }
            }
            case "GetProperty":
            {
                var id = reader.ReadInt32();
                var name = reader.ReadString();
                lock (_lock)
                {
                    var properties = id >= 0 && id < _given.Count ? PropertiesOf(_given[id].Item) : null;
                    return properties != null && properties.TryGetValue(name, out var value)
                        ? DBusMessage.MethodReturn(call, "v", writer => writer.WriteVariant(value))
                        : DBusMessage.Error(call, "com.canonical.dbusmenu.Error.UnknownProperty", $"{id}.{name}");
                }
            }
            case "Event":
            {
                var id = reader.ReadInt32();
                var eventId = reader.ReadString();
                Happened(id, eventId);
                return DBusMessage.MethodReturn(call);
            }
            case "EventGroup":
            {
                List<object> unknown = [];
                foreach (object[] happened in (List<object>)reader.ReadValue("a(isvu)"))
                {
                    if (!Happened((int)happened[0], (string)happened[1]))
                    {
                        unknown.Add(happened[0]);
                    }
                }

                return DBusMessage.MethodReturn(call, "ai", writer => writer.WriteValue("ai", unknown));
            }
            case "AboutToShow":
            {
                var changed = await ReadMenuAsync();
                return DBusMessage.MethodReturn(call, "b", writer => writer.WriteBoolean(changed));
            }
            case "AboutToShowGroup":
            {
                var ids = (List<object>)reader.ReadValue("ai");
                var changed = await ReadMenuAsync();
                var updated = changed ? ids : new List<object>();
                return DBusMessage.MethodReturn(call, "aiai", writer =>
                {
                    writer.WriteValue("ai", updated);
                    writer.WriteValue("ai", new List<object>());
                });
            }
            default:
                return null;
        }
    }

    /// <summary>Answers org.freedesktop.DBus.Properties for <paramref name="interfaceName"/> from
    /// <paramref name="properties"/>; null for another interface's.</summary>
    internal static DBusMessage AnswerProperties(DBusMessage call, string interfaceName,
        Dictionary<string, DBusVariant> properties)
    {
        var reader = call.Reader();
        var asked = reader.ReadString();
        switch (call.Member)
        {
            case "GetAll":
                return DBusMessage.MethodReturn(call, "a{sv}", writer =>
                    writer.WriteDictionary(asked == interfaceName ? properties : new Dictionary<string, DBusVariant>()));
            case "Get":
            {
                var name = reader.ReadString();
                return asked == interfaceName && properties.TryGetValue(name, out var value)
                    ? DBusMessage.MethodReturn(call, "v", writer => writer.WriteVariant(value))
                    : DBusMessage.Error(call, "org.freedesktop.DBus.Error.UnknownProperty", $"{asked}.{name} is no property here");
            }
            case "Set":
                return DBusMessage.Error(call, "org.freedesktop.DBus.Error.PropertyReadOnly", $"{asked} is read only");
            default:
                return null;
        }
    }

    // Read on the loop thread; whether the menu changed since it was read last.
    private Task<bool> ReadMenuAsync()
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var menu = _menu;
        if (menu == null)
        {
            done.SetResult(Take(null));
            return done.Task;
        }

        post(() =>
        {
            try
            {
                done.SetResult(Take(menu.ReadRows()));
            }
            catch (Exception e)
            {
                Serilog.Log.Logger.Warning(e, "The status icon's menu could not be read");
                done.SetResult(Take(null));
            }
        });
        return done.Task;
    }

    private bool Take(IReadOnlyList<NotificationAreaMenuItem> items)
    {
        List<Row> rows = [];
        var root = new Row(null, []);
        rows.Add(root);
        var shape = new StringBuilder();
        Number(items ?? [], root, rows, shape);
        uint revision;
        lock (_lock)
        {
            _rows = rows;
            var text = shape.ToString();
            if (text == _shape)
            {
                return false;
            }

            _shape = text;
            revision = ++_revision;
        }

        // Panels of libdbusmenu and the AppIndicator extension ask for the layout again only when told.
        say(DBusMessage.Signal(StatusNotifierIcon.MenuPath, MenuInterface, "LayoutUpdated", "ui", writer =>
        {
            writer.WriteUInt32(revision);
            writer.WriteInt32(0);
        }));
        return true;
    }

    // In the order the rows stand, depth first: the number a row gets is where it is.
    private static void Number(IReadOnlyList<NotificationAreaMenuItem> items, Row parent, List<Row> rows,
        StringBuilder shape)
    {
        shape.Append('[');
        foreach (var item in items)
        {
            var row = new Row(item, []);
            parent.Children.Add(rows.Count);
            rows.Add(row);
            shape.Append(item.IsSeparator ? "-" : item.Text).Append('|').Append(item.IsEnabled).Append('|')
                .Append(item.IsCheckable).Append('|').Append(item.IsChecked);
            Number(item.Items ?? [], row, rows, shape);
        }

        shape.Append(']');
    }

    private object[] Layout(int id, int depth)
    {
        var row = _rows[id];
        List<object> children = [];
        if (depth != 0)
        {
            foreach (var child in row.Children)
            {
                children.Add(new DBusVariant(ItemSignature, Layout(child, depth - 1)));
            }
        }

        return [id, PropertiesOf(row.Item, row.Children.Count > 0), children];
    }

    private static Dictionary<string, DBusVariant> PropertiesOf(NotificationAreaMenuItem item) =>
        PropertiesOf(item, item?.Items is { Count: > 0 });

    private static Dictionary<string, DBusVariant> PropertiesOf(NotificationAreaMenuItem item, bool hasRows)
    {
        Dictionary<string, DBusVariant> properties = [];
        if (hasRows)
        {
            properties["children-display"] = DBusVariant.String("submenu");
        }

        if (item == null)
        {
            return properties;
        }

        if (item.IsSeparator)
        {
            properties["type"] = DBusVariant.String("separator");
            return properties;
        }

        // An underscore marks the access key there, and none of the application's text means one: it is doubled.
        properties["label"] = DBusVariant.String((item.Text ?? string.Empty).Replace("_", "__"));
        if (!item.IsEnabled)
        {
            properties["enabled"] = DBusVariant.Boolean(false);
        }

        if (item.IsCheckable)
        {
            properties["toggle-type"] = DBusVariant.String("checkmark");
            properties["toggle-state"] = DBusVariant.Int32(item.IsChecked ? 1 : 0);
        }

        return properties;
    }

    private bool Happened(int id, string eventId)
    {
        Row row;
        lock (_lock)
        {
            if (id < 0 || id >= _given.Count)
            {
                return false;
            }

            row = _given[id];
        }

        if (eventId == "clicked" && row.Item is { IsSeparator: false, IsEnabled: true, Picked: { } picked })
        {
            post(picked);
        }

        return true;
    }

    private sealed record Row(NotificationAreaMenuItem Item, List<int> Children);

    private const string Introspection = """
        <node>
          <interface name="com.canonical.dbusmenu">
            <property name="Version" type="u" access="read"/>
            <property name="TextDirection" type="s" access="read"/>
            <property name="Status" type="s" access="read"/>
            <property name="IconThemePath" type="as" access="read"/>
            <method name="GetLayout">
              <arg type="i" name="parentId" direction="in"/>
              <arg type="i" name="recursionDepth" direction="in"/>
              <arg type="as" name="propertyNames" direction="in"/>
              <arg type="u" name="revision" direction="out"/>
              <arg type="(ia{sv}av)" name="layout" direction="out"/>
            </method>
            <method name="GetGroupProperties">
              <arg type="ai" name="ids" direction="in"/>
              <arg type="as" name="propertyNames" direction="in"/>
              <arg type="a(ia{sv})" name="properties" direction="out"/>
            </method>
            <method name="GetProperty">
              <arg type="i" name="id" direction="in"/>
              <arg type="s" name="name" direction="in"/>
              <arg type="v" name="value" direction="out"/>
            </method>
            <method name="Event">
              <arg type="i" name="id" direction="in"/>
              <arg type="s" name="eventId" direction="in"/>
              <arg type="v" name="data" direction="in"/>
              <arg type="u" name="timestamp" direction="in"/>
            </method>
            <method name="EventGroup">
              <arg type="a(isvu)" name="events" direction="in"/>
              <arg type="ai" name="idErrors" direction="out"/>
            </method>
            <method name="AboutToShow">
              <arg type="i" name="id" direction="in"/>
              <arg type="b" name="needUpdate" direction="out"/>
            </method>
            <method name="AboutToShowGroup">
              <arg type="ai" name="ids" direction="in"/>
              <arg type="ai" name="updatesNeeded" direction="out"/>
              <arg type="ai" name="idErrors" direction="out"/>
            </method>
            <signal name="ItemsPropertiesUpdated">
              <arg type="a(ia{sv})" name="updatedProps"/>
              <arg type="a(ias)" name="removedProps"/>
            </signal>
            <signal name="LayoutUpdated">
              <arg type="u" name="revision"/>
              <arg type="i" name="parent"/>
            </signal>
            <signal name="ItemActivationRequested">
              <arg type="i" name="id"/>
              <arg type="u" name="timestamp"/>
            </signal>
          </interface>
        </node>
        """;
}
