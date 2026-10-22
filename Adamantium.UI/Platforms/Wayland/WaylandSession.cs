using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The application's session with the compositor (xdg_session_management): windows added by name get back the
/// place and size they had in the last run. Its id is kept in the user's state directory.</summary>
internal sealed class WaylandSession
{
    private readonly object sync = new();
    private readonly XdgSessionV1 session;
    private readonly HashSet<string> names = [];

    public WaylandSession(XdgSessionManagerV1 manager, string appId)
    {
        var file = IdFile(appId);
        session = manager.GetSession(XdgSessionManagerV1.Reason.Launch, ReadId(file));
        session.Created += id => WriteId(file, id);
    }

    /// <summary>Asks for the window's state of the last run, before its first commit; null when a window of this run
    /// already had the name - the compositor keeps a name for the whole session, closed window or not.</summary>
    public XdgToplevelSessionV1 Restore(XdgToplevel toplevel, string name)
    {
        lock (sync)
        {
            return names.Add(name) ? session.RestoreToplevel(toplevel, name) : null;
        }
    }

    private static string IdFile(string appId)
    {
        var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(state, appId, "wayland-session");
    }

    private static string ReadId(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file).Trim() is { Length: > 0 } id ? id : null : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteId(string file, string id)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, id);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
