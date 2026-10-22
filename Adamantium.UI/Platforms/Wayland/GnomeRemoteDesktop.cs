using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Adamantium.DBus;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>A GNOME remote desktop session (org.gnome.Mutter.RemoteDesktop): the pointer moved and fingers pressed at
/// points of a monitor, as the system's own input.</summary>
internal sealed class GnomeRemoteDesktop : IDisposable
{
    private const string RemoteDesktop = "org.gnome.Mutter.RemoteDesktop";
    private const string ScreenCast = "org.gnome.Mutter.ScreenCast";
    private const string DisplayConfig = "org.gnome.Mutter.DisplayConfig";
    private const string Properties = "org.freedesktop.DBus.Properties";
    private const string ServiceUnknown = "org.freedesktop.DBus.Error.ServiceUnknown";

    private readonly DBusConnection bus;
    private readonly string session;
    private readonly IDisposable closedSubscription;
    private int closed;

    private GnomeRemoteDesktop(DBusConnection bus, string session)
    {
        this.bus = bus;
        this.session = session;
        closedSubscription = bus.Subscribe(RemoteDesktop, session, RemoteDesktop + ".Session", "Closed",
            _ => Interlocked.Exchange(ref closed, 1));
    }

    public IReadOnlyList<GnomeMonitorStream> Monitors { get; private set; } = [];

    /// <summary>Whether GNOME ended the session - the user stopped the sharing, or the shell restarted.</summary>
    public bool IsClosed => Volatile.Read(ref closed) != 0;

    /// <summary>A started session with every monitor recorded; null where the desktop is not GNOME's.</summary>
    /// <exception cref="InvalidOperationException">GNOME refused the session.</exception>
    public static GnomeRemoteDesktop Open()
    {
        DBusConnection bus;
        try
        {
            bus = DBusConnection.ConnectSession();
        }
        catch (Exception e) when (e is DBusException or SocketException or IOException)
        {
            return null;
        }

        try
        {
            var session = bus.Call(DBusMessage.MethodCall(RemoteDesktop, "/org/gnome/Mutter/RemoteDesktop", RemoteDesktop,
                "CreateSession")).Reader().ReadObjectPath().Value;
            var desktop = new GnomeRemoteDesktop(bus, session);
            desktop.Monitors = desktop.RecordMonitors();
            return desktop;
        }
        catch (DBusException e) when (e.Name == ServiceUnknown)
        {
            bus.Dispose();
            return null;
        }
        catch (Exception e) when (e is DBusException or InvalidCastException or IOException)
        {
            bus.Dispose();
            throw new InvalidOperationException($"GNOME refused a remote desktop session: {e.Message}", e);
        }
    }

    public void MovePointer(string stream, double x, double y) =>
        Notify("NotifyPointerMotionAbsolute", "sdd", writer =>
        {
            writer.WriteString(stream);
            writer.WriteDouble(x);
            writer.WriteDouble(y);
        });

    public void TouchDown(string stream, uint slot, double x, double y) => Touch("NotifyTouchDown", stream, slot, x, y);

    public void TouchMotion(string stream, uint slot, double x, double y) => Touch("NotifyTouchMotion", stream, slot, x, y);

    public void TouchUp(uint slot) => Notify("NotifyTouchUp", "u", writer => writer.WriteUInt32(slot));

    public void Dispose()
    {
        closedSubscription.Dispose();
        if (!IsClosed)
        {
            try
            {
                bus.Call(DBusMessage.MethodCall(RemoteDesktop, session, RemoteDesktop + ".Session", "Stop"));
            }
            catch (DBusException)
            {
            }
        }

        bus.Dispose();
    }

    private List<GnomeMonitorStream> RecordMonitors()
    {
        var id = bus.Call(DBusMessage.MethodCall(RemoteDesktop, session, Properties, "Get", "ss", writer =>
        {
            writer.WriteString(RemoteDesktop + ".Session");
            writer.WriteString("SessionId");
        })).Reader().ReadVariant().Value as string;
        var cast = bus.Call(DBusMessage.MethodCall(ScreenCast, "/org/gnome/Mutter/ScreenCast", ScreenCast, "CreateSession",
            "a{sv}", writer => writer.WriteDictionary(new Dictionary<string, DBusVariant>
            {
                ["remote-desktop-session-id"] = DBusVariant.String(id),
            }))).Reader().ReadObjectPath().Value;

        List<(string Connector, string Stream, int Width, int Height)> recorded = [];
        foreach (var (connector, width, height) in CurrentModes())
        {
            var stream = bus.Call(DBusMessage.MethodCall(ScreenCast, cast, ScreenCast + ".Session", "RecordMonitor", "sa{sv}",
                writer =>
                {
                    writer.WriteString(connector);
                    writer.WriteDictionary(new Dictionary<string, DBusVariant>());
                })).Reader().ReadObjectPath().Value;
            recorded.Add((connector, stream, width, height));
        }

        bus.Call(DBusMessage.MethodCall(RemoteDesktop, session, RemoteDesktop + ".Session", "Start"));

        List<GnomeMonitorStream> monitors = [];
        foreach (var (connector, stream, width, height) in recorded)
        {
            var (streamWidth, streamHeight) = StreamSize(stream) ?? (width, height);
            monitors.Add(new GnomeMonitorStream(connector, stream, streamWidth, streamHeight));
        }

        return monitors;
    }

    private (int Width, int Height)? StreamSize(string stream)
    {
        try
        {
            var parameters = (Dictionary<object, object>)bus.Call(DBusMessage.MethodCall(ScreenCast, stream, Properties, "Get",
                "ss", writer =>
                {
                    writer.WriteString(ScreenCast + ".Stream");
                    writer.WriteString("Parameters");
                })).Reader().ReadVariant().Value;
            return parameters.TryGetValue("size", out var size) && ((DBusVariant)size).Value is object[] { Length: 2 } pair
                ? ((int)pair[0], (int)pair[1])
                : null;
        }
        catch (DBusException)
        {
            return null;
        }
    }

    private IEnumerable<(string Connector, int Width, int Height)> CurrentModes()
    {
        var reader = bus.Call(DBusMessage.MethodCall(DisplayConfig, "/org/gnome/Mutter/DisplayConfig", DisplayConfig,
            "GetCurrentState")).Reader();
        reader.ReadUInt32();
        var monitors = (List<object>)reader.ReadValue("a((ssss)a(siiddada{sv})a{sv})");
        foreach (object[] monitor in monitors)
        {
            var connector = (string)((object[])monitor[0])[0];
            foreach (object[] mode in (List<object>)monitor[1])
            {
                var properties = (Dictionary<object, object>)mode[6];
                if (properties.TryGetValue("is-current", out var current) && ((DBusVariant)current).Value is true)
                {
                    yield return (connector, (int)mode[1], (int)mode[2]);
                }
            }
        }
    }

    private void Touch(string member, string stream, uint slot, double x, double y) =>
        Notify(member, "sudd", writer =>
        {
            writer.WriteString(stream);
            writer.WriteUInt32(slot);
            writer.WriteDouble(x);
            writer.WriteDouble(y);
        });

    private void Notify(string member, string signature, Action<DBusWriter> body) =>
        bus.Call(DBusMessage.MethodCall(RemoteDesktop, session, RemoteDesktop + ".Session", member, signature, body));
}
