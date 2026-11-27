using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Adamantium.DBus;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The desktop's own open and save dialogs, through the FileChooser portal. The calling thread waits for the
/// answer, as it waits for a modal dialog on Windows.</summary>
internal sealed class PortalFileDialog : IFileDialogPlatform
{
    private const string Portal = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string FileChooser = "org.freedesktop.portal.FileChooser";
    private const string Request = "org.freedesktop.portal.Request";
    private const string BusName = "org.freedesktop.DBus";
    private const string BusPath = "/org/freedesktop/DBus";

    private readonly Func<IntPtr, string> parentOf;
    private int requests;

    /// <param name="parentOf">The portal's name of the window a dialog belongs to, from its handle; empty for none.</param>
    public PortalFileDialog(Func<IntPtr, string> parentOf)
    {
        this.parentOf = parentOf;
    }

    public string Save(SaveFileRequest request)
    {
        var path = Ask("SaveFile", request?.Owner ?? IntPtr.Zero, request?.Title ?? "Save", SaveOptions(request))
            .FirstOrDefault();
        if (path != null && request?.DefaultExtension is { Length: > 0 } extension && Path.GetExtension(path).Length == 0)
        {
            path += "." + extension;
        }

        return path;
    }

    public string Open(OpenFileRequest request) =>
        Ask("OpenFile", request?.Owner ?? IntPtr.Zero, request?.Title ?? "Open", Options(request?.FileTypes))
            .FirstOrDefault();

    public IReadOnlyList<string> OpenMany(OpenFileRequest request) =>
        Ask("OpenFile", request?.Owner ?? IntPtr.Zero, request?.Title ?? "Open", ManyOptions(request?.FileTypes));

    // A portal older than FileChooser 3 does not know "directory" and lets a file be chosen: that is no answer here.
    public string OpenFolder(OpenFolderRequest request) =>
        Ask("OpenFile", request?.Owner ?? IntPtr.Zero, request?.Title ?? "Choose a folder", FolderOptions())
            .FirstOrDefault() is { } path
        && Directory.Exists(path)
            ? path
            : null;

    /// <summary>The options of an open dialog that lets several files be chosen at once.</summary>
    public static Dictionary<string, DBusVariant> ManyOptions(IReadOnlyList<FileType> fileTypes)
    {
        var options = Options(fileTypes);
        options["multiple"] = DBusVariant.Boolean(true);
        return options;
    }

    /// <summary>The options of a save dialog: the filters, the name, and the folder - as bytes ending in a zero.</summary>
    public static Dictionary<string, DBusVariant> SaveOptions(SaveFileRequest request)
    {
        var options = Options(request?.FileTypes);
        if (request?.FileName is { Length: > 0 } name)
        {
            options["current_name"] = DBusVariant.String(name);
        }

        if (Directory.Exists(request?.Folder))
        {
            var folder = Path.GetFullPath(request.Folder);
            options["current_folder"] = new DBusVariant("ay", System.Text.Encoding.UTF8.GetBytes(folder + "\0"));
        }

        return options;
    }

    /// <summary>The options of an open dialog that chooses a folder.</summary>
    public static Dictionary<string, DBusVariant> FolderOptions() => new() { ["directory"] = DBusVariant.Boolean(true) };

    /// <summary>The filters for these file types, as the portal takes them: glob patterns, the first one selected.</summary>
    public static Dictionary<string, DBusVariant> Options(IReadOnlyList<FileType> fileTypes)
    {
        var options = new Dictionary<string, DBusVariant>();
        var filters = (fileTypes ?? []).Where(type => type.Extensions.Count > 0)
            .Select(type => (object)new object[]
            {
                type.Name,
                type.Extensions.SelectMany(extension => new[] { extension.ToLowerInvariant(), extension.ToUpperInvariant() })
                    .Distinct().Select(extension => (object)new object[] { 0u, "*." + extension }).ToList(),
            })
            .ToList();
        if (filters.Count > 0)
        {
            options["filters"] = new DBusVariant("a(sa(us))", filters);
            options["current_filter"] = new DBusVariant("(sa(us))", filters[0]);
        }

        return options;
    }

    // The local files chosen; none when nothing was, or the portal could not be asked.
    private IReadOnlyList<string> Ask(string method, IntPtr owner, string title, Dictionary<string, DBusVariant> options)
    {
        try
        {
            using var bus = DBusConnection.ConnectSession();
            var token = $"adamantium{Interlocked.Increment(ref requests)}";
            options["handle_token"] = DBusVariant.String(token);
            var requestPath = $"{PortalPath}/request/{bus.UniqueName.TrimStart(':').Replace('.', '_')}/{token}";
            IReadOnlyList<string> chosen = [];
            using var answered = new ManualResetEventSlim();
            using (bus.Subscribe(Portal, requestPath, Request, "Response", response =>
                   {
                       try
                       {
                           chosen = AllChosen(response);
                       }
                       finally
                       {
                           answered.Set();
                       }
                   }))
            using (bus.Subscribe(BusName, BusPath, BusName, "NameOwnerChanged", change =>
                   {
                       var reader = change.Reader();
                       var name = reader.ReadString();
                       reader.ReadString();
                       var newOwner = reader.ReadString();
                       if (name == Portal && newOwner.Length == 0)
                       {
                           answered.Set();
                       }
                   }))
            {
                bus.Call(DBusMessage.MethodCall(Portal, PortalPath, FileChooser, method, "ssa{sv}", writer =>
                {
                    writer.WriteString(parentOf?.Invoke(owner) ?? string.Empty);
                    writer.WriteString(title);
                    writer.WriteDictionary(options);
                }));
                answered.Wait();
            }

            return chosen;
        }
        catch (Exception e) when (e is DBusException or SocketException or IOException)
        {
            return [];
        }
    }

    /// <summary>The path a Response signal carries; null when the user chose nothing, or something that is not a local
    /// file.</summary>
    internal static string Chosen(DBusMessage response) => AllChosen(response).FirstOrDefault();

    /// <summary>The local files a Response signal carries, in its order; what is not a local file is passed by.</summary>
    internal static IReadOnlyList<string> AllChosen(DBusMessage response)
    {
        var reader = response.Reader();
        var code = reader.ReadUInt32();
        var results = (Dictionary<object, object>)reader.ReadValue("a{sv}");
        if (code != 0 || !results.TryGetValue("uris", out var uris) || ((DBusVariant)uris).Value is not List<object> list)
        {
            return [];
        }

        return list.Select(item => Uri.TryCreate(item as string, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null)
            .Where(path => path != null)
            .ToArray();
    }
}
