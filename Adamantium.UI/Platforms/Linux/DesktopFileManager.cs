using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.DBus;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The desktop's file manager through its D-Bus interface, <c>org.freedesktop.FileManager1</c>, which Files,
/// Dolphin and the rest serve: the folder opens with the file selected. The answer is not waited for - a file manager
/// started cold takes its time, and the window asking must not stop meanwhile. A file is opened through xdg-open.
/// </summary>
internal sealed class DesktopFileManager : IFileManagerPlatform
{
    private const string Service = "org.freedesktop.FileManager1";
    private const string ServicePath = "/org/freedesktop/FileManager1";
    private const string DownloadKey = "XDG_DOWNLOAD_DIR";
    private const string HomeToken = "$HOME";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>The downloads folder xdg-user-dirs keeps in <c>user-dirs.dirs</c>, read as glib reads it: the last
    /// assignment, a quoted path under the home folder (<c>"$HOME/Загрузки"</c>) or a full one, backslash escapes
    /// undone; set to the home folder itself, the home folder. Null when the file does not say, or says what is no
    /// path.</summary>
    public string DownloadsFolder
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Path.IsPathFullyQualified(home))
            {
                return null;
            }

            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { } set && Path.IsPathFullyQualified(set)
                ? set
                : Path.Combine(home, ".config");
            string found = null;
            try
            {
                foreach (var line in File.ReadLines(Path.Combine(config, "user-dirs.dirs")))
                {
                    if (UserDir(line, home) is { } folder)
                    {
                        found = folder;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }

            return found;
        }
    }

    private static string UserDir(string line, string home)
    {
        var entry = line.AsSpan().Trim();
        if (!entry.StartsWith(DownloadKey, StringComparison.Ordinal))
        {
            return null;
        }

        entry = entry[DownloadKey.Length..].TrimStart();
        if (entry.Length == 0 || entry[0] != '=')
        {
            return null;
        }

        entry = entry[1..].TrimStart();
        if (entry.Length == 0 || entry[0] != '"')
        {
            return null;
        }

        // Up to the closing quote, backslash escapes undone; what follows it - a comment - is not the path.
        var value = new System.Text.StringBuilder();
        var closed = false;
        for (var i = 1; i < entry.Length; i++)
        {
            if (entry[i] == '\\' && i + 1 < entry.Length)
            {
                value.Append(entry[++i]);
            }
            else if (entry[i] == '"')
            {
                closed = true;
                break;
            }
            else
            {
                value.Append(entry[i]);
            }
        }

        var path = value.ToString();
        if (!closed)
        {
            return null;
        }

        if (path == HomeToken || path == HomeToken + "/")
        {
            return home;
        }

        if (path.StartsWith(HomeToken + "/", StringComparison.Ordinal))
        {
            return Path.Combine(home, path[(HomeToken.Length + 1)..]);
        }

        return path.StartsWith('/') ? path : null;
    }

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

    // xdg-open asks the desktop which application opens the file, as the file manager would.
    public bool OpenItem(string path)
    {
        try
        {
            using var open = Process.Start(new ProcessStartInfo("xdg-open")
            {
                ArgumentList = { path },
                UseShellExecute = false
            });
            return open != null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or ArgumentException)
        {
            return false;
        }
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
