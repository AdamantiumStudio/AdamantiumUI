using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.DBus;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The desktop's file manager through its D-Bus interface, <c>org.freedesktop.FileManager1</c>, which Files,
/// Dolphin and the rest serve: the folder opens with the file selected. The answer is not waited for - a file manager
/// started cold takes its time, and the window asking must not stop meanwhile.</summary>
internal sealed class DesktopFileManager : IFileManagerPlatform
{
    private const string Service = "org.freedesktop.FileManager1";
    private const string ServicePath = "/org/freedesktop/FileManager1";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    public bool ShowItem(string path)
    {
        DBusConnection bus;
        string uri;
        try
        {
            uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
            bus = DBusConnection.ConnectSession();
        }
        catch (Exception e) when (e is DBusException or SocketException or IOException or UriFormatException)
        {
            return false;
        }

        _ = AskAsync(bus, uri);
        return true;
    }

    private static async Task AskAsync(DBusConnection bus, string uri)
    {
        using (bus)
        {
            using var patience = new CancellationTokenSource(Patience);
            try
            {
                await bus.CallAsync(DBusMessage.MethodCall(Service, ServicePath, Service, "ShowItems", "ass", writer =>
                {
                    writer.WriteValue("as", new List<object> { uri });
                    writer.WriteString(string.Empty);
                }), patience.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is DBusException or SocketException or IOException or OperationCanceledException)
            {
                Serilog.Log.Logger.Warning("The file manager did not show {Uri}: {Reason}", uri, e.Message);
            }
        }
    }
}
