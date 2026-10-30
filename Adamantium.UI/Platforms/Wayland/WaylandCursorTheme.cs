using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Wayland;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>The user's cursor theme, for a compositor that does not draw cursors by shape (no wp_cursor_shape): the
/// theme named by <c>XCURSOR_THEME</c>, else <c>default</c>, looked up along <c>XCURSOR_PATH</c> or the usual icon
/// folders, with the themes it inherits; the size from <c>XCURSOR_SIZE</c>, else 24.</summary>
internal static class WaylandCursorTheme
{
    private const int DefaultSize = 24;

    private static readonly Lazy<string[]> Folders = new(SearchFolders);

    /// <summary>The logical size the theme's cursors are shown at.</summary>
    public static int Size => int.TryParse(Environment.GetEnvironmentVariable("XCURSOR_SIZE"), out var size) && size > 0
        ? size
        : DefaultSize;

    /// <summary>The theme's file for <paramref name="shape"/>, under its name or an older one; null when the theme has
    /// none.</summary>
    public static string Find(WpCursorShapeDeviceV1.Shape shape)
    {
        var theme = Environment.GetEnvironmentVariable("XCURSOR_THEME");
        foreach (var name in NamesOf(shape))
        {
            if (Find(string.IsNullOrEmpty(theme) ? "default" : theme, name, []) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string Find(string theme, string name, HashSet<string> seen)
    {
        if (!seen.Add(theme))
        {
            return null;
        }

        foreach (var folder in Folders.Value)
        {
            var file = Path.Combine(folder, theme, "cursors", name);
            if (File.Exists(file))
            {
                return file;
            }
        }

        foreach (var inherited in Inherited(theme))
        {
            if (Find(inherited, name, seen) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // The themes "Inherits=" names in the theme's index.theme, the first one found.
    private static IEnumerable<string> Inherited(string theme)
    {
        foreach (var folder in Folders.Value)
        {
            var index = Path.Combine(folder, theme, "index.theme");
            if (!File.Exists(index))
            {
                continue;
            }

            string line;
            try
            {
                line = File.ReadLines(index).FirstOrDefault(text => text.TrimStart().StartsWith("Inherits", StringComparison.Ordinal));
            }
            catch (IOException)
            {
                continue;
            }

            if (line?.Split('=', 2) is [_, var names])
            {
                return names.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return [];
    }

    private static string[] SearchFolders()
    {
        if (Environment.GetEnvironmentVariable("XCURSOR_PATH") is { Length: > 0 } path)
        {
            return path.Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => folder.StartsWith('~')
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + folder[1..]
                    : folder)
                .ToArray();
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dataHome
            ? dataHome
            : Path.Combine(home, ".local", "share");
        var dataDirs = (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs
                ? dirs
                : "/usr/local/share:/usr/share")
            .Split(':', StringSplitOptions.RemoveEmptyEntries);
        return [Path.Combine(data, "icons"), Path.Combine(home, ".icons"), .. dataDirs.Select(dir => Path.Combine(dir, "icons")),
            "/usr/share/pixmaps"];
    }

    // The shape's name in the cursor naming the protocol uses, then the X11 one older themes have.
    private static string[] NamesOf(WpCursorShapeDeviceV1.Shape shape) => shape switch
    {
        WpCursorShapeDeviceV1.Shape.Pointer => ["pointer", "hand2"],
        WpCursorShapeDeviceV1.Shape.Text => ["text", "xterm"],
        WpCursorShapeDeviceV1.Shape.Progress => ["progress", "left_ptr_watch"],
        WpCursorShapeDeviceV1.Shape.Wait => ["wait", "watch"],
        WpCursorShapeDeviceV1.Shape.Crosshair => ["crosshair", "cross"],
        WpCursorShapeDeviceV1.Shape.Help => ["help", "question_arrow"],
        WpCursorShapeDeviceV1.Shape.NotAllowed => ["not-allowed", "crossed_circle"],
        WpCursorShapeDeviceV1.Shape.Move => ["move", "fleur"],
        WpCursorShapeDeviceV1.Shape.NsResize => ["ns-resize", "sb_v_double_arrow"],
        WpCursorShapeDeviceV1.Shape.EwResize => ["ew-resize", "sb_h_double_arrow"],
        WpCursorShapeDeviceV1.Shape.NeswResize => ["nesw-resize", "fd_double_arrow"],
        WpCursorShapeDeviceV1.Shape.NwseResize => ["nwse-resize", "bd_double_arrow"],
        WpCursorShapeDeviceV1.Shape.Copy => ["copy", "dnd-copy"],
        WpCursorShapeDeviceV1.Shape.Alias => ["alias", "dnd-link"],
        _ => ["default", "left_ptr"]
    };
}
