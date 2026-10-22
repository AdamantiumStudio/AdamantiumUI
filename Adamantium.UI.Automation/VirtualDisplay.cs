using System;
using System.Diagnostics;
using System.IO;

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

        if (string.IsNullOrWhiteSpace(arguments))
        {
            start.ArgumentList.Add(path);
        }
        else
        {
            start.ArgumentList.Add("/bin/sh");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add($"exec \"$0\" {arguments}");
            start.ArgumentList.Add(path);
        }

        return start;
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
