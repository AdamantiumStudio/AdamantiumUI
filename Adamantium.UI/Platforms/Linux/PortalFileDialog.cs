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
        var options = Options(request.FileTypes);
        if (request.FileName is { Length: > 0 } name)
        {
            options["current_name"] = DBusVariant.String(name);
        }

        var path = Ask("SaveFile", request.Owner, request.Title ?? "Save", options);
        if (path != null && request.DefaultExtension is { Length: > 0 } extension && Path.GetExtension(path).Length == 0)
        {
            path += "." + extension;
        }

        return path;
    }

    public string Open(OpenFileRequest request) => Ask("OpenFile", request.Owner, request.Title ?? "Open", Options(request.FileTypes));

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

    private string Ask(string method, IntPtr owner, string title, Dictionary<string, DBusVariant> options)
    {
        try
        {
            using var bus = DBusConnection.ConnectSession();
            var token = $"adamantium{Interlocked.Increment(ref requests)}";
            options["handle_token"] = DBusVariant.String(token);
            var requestPath = $"{PortalPath}/request/{bus.UniqueName.TrimStart(':').Replace('.', '_')}/{token}";
            string chosen = null;
            using var answered = new ManualResetEventSlim();
            using (bus.Subscribe(Portal, requestPath, Request, "Response", response =>
                   {
                       try
                       {
                           chosen = Chosen(response);
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
            return null;
        }
    }

    /// <summary>The path a Response signal carries; null when the user chose nothing.</summary>
    internal static string Chosen(DBusMessage response)
    {
        var reader = response.Reader();
        var code = reader.ReadUInt32();
        var results = (Dictionary<object, object>)reader.ReadValue("a{sv}");
        return code == 0 && results.TryGetValue("uris", out var uris) && ((DBusVariant)uris).Value is List<object> { Count: > 0 } list
            ? new Uri((string)list[0]).LocalPath
            : null;
    }
}
