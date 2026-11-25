using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Adamantium.DBus;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>Icons of a Linux panel's status area (StatusNotifierItem, as KDE Plasma, the GNOME AppIndicator extension,
/// Xfce and the wlroots bars show them): each on a session bus connection of its own, registered with the panel's watcher
/// - again whenever a watcher starts - and its menu served for the panel to show itself.</summary>
internal sealed class StatusNotifierArea : INotificationAreaPlatform
{
    private int _lastId;

    public INotificationAreaIcon AddIcon(string toolTip, INotificationAreaMenu menu) =>
        StatusNotifierIcon.Start(toolTip, menu, Interlocked.Increment(ref _lastId));

    /// <summary>A watcher runs on the session bus and a panel showing the icons is registered with it.</summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                using var bus = DBusConnection.ConnectSession();
                return IsShownOn(bus, StatusNotifierIcon.WatcherName);
            }
            catch (Exception e) when (e is DBusException or IOException or SocketException)
            {
                Serilog.Log.Logger.Debug(e, "No session bus: status icons show nowhere");
                return false;
            }
        }
    }

    /// <summary>Whether <paramref name="watcher"/> runs on <paramref name="bus"/> with a panel registered.</summary>
    internal static bool IsShownOn(DBusConnection bus, string watcher)
    {
        var watched = bus.Call(DBusMessage.MethodCall("org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus", "NameHasOwner", "s", writer => writer.WriteString(watcher))).Reader().ReadBoolean();
        if (!watched)
        {
            return false;
        }

        var shown = bus.Call(DBusMessage.MethodCall(watcher, StatusNotifierIcon.WatcherPath,
            "org.freedesktop.DBus.Properties", "Get", "ss", writer =>
            {
                writer.WriteString(StatusNotifierIcon.WatcherInterface);
                writer.WriteString("IsStatusNotifierHostRegistered");
            })).Reader().ReadVariant();
        return shown.Value is true;
    }
}
