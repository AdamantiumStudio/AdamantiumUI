using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Adamantium.UI.Automation;

/// <summary>Starts an application on a headless Wayland compositor's virtual monitor instead of the desktop, so it takes no
/// focus from whoever works there. Linux only: Mutter with <c>--headless</c>, on a D-Bus session of its own, runs the
/// application and exits with it.</summary>
internal static class VirtualDisplay
{
    private const string Width = "1920";
    private const string Height = "1080";

    /// <summary>How to start <paramref name="path"/> on a virtual monitor, or null - saying why on standard error - where
    /// there is none: not on Linux, or no Mutter. Started through a shell that lets go of every inherited stream, so a
    /// run left going outlives whoever started it.</summary>
    internal static ProcessStartInfo Wrap(string path, string arguments)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var session = OnPath("dbus-run-session");
        var mutter = OnPath("mutter");
        if (session == null || mutter == null)
        {
            Console.Error.WriteLine($"[VirtualDisplay] {(mutter == null ? "mutter" : "dbus-run-session")} is not installed; " +
                                    $"{Path.GetFileName(path)} shows on the desktop.");
            return null;
        }

        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var argument in (string[])["-c", "exec \"$@\" </dev/null >/dev/null 2>&1", "sh", session, "--", mutter,
                     "--headless", "--wayland", "--no-x11", "--virtual-monitor", $"{Width}x{Height}",
                     "--wayland-display", $"adam-virtual-{Guid.NewGuid():N}", "--"])
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add(path);
        foreach (var argument in Split(arguments ?? string.Empty))
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    /// <summary>The arguments of a command line, split as <see cref="ProcessStartInfo.Arguments"/> splits them: blanks
    /// part them, double quotes hold blanks in, and a backslash escapes a quote.</summary>
    internal static List<string> Split(string line)
    {
        List<string> arguments = [];
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\')
            {
                var slashes = 0;
                while (i < line.Length && line[i] == '\\')
                {
                    slashes++;
                    i++;
                }

                if (i < line.Length && line[i] == '"')
                {
                    current.Append('\\', slashes / 2);
                    if (slashes % 2 == 1)
                    {
                        current.Append('"');
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else
                {
                    current.Append('\\', slashes);
                    i--;
                }

                any = true;
            }
            else if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    private static string OnPath(string name)
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(folder, name);
            if (folder.Length > 0 && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
