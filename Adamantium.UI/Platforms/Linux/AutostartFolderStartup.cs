using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Adamantium.UI.Core;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The user's autostart folder of the XDG Autostart specification: a desktop entry named after the application
/// starts it at sign-in. The desktop's settings turn one off with Hidden=true or, on GNOME,
/// X-GNOME-Autostart-enabled=false, or take it out.</summary>
internal sealed class AutostartFolderStartup : ISystemStartup
{
    private const string ConfigHomeVariable = "XDG_CONFIG_HOME";
    private const string ConfigFolder = ".config";
    private const string AutostartFolder = "autostart";
    private const string DesktopExtension = ".desktop";
    private const string ExecKey = "Exec";
    private const string HiddenKey = "Hidden";
    private const string GnomeEnabledKey = "X-GNOME-Autostart-enabled";
    private const string True = "true";
    private const string False = "false";

    private readonly string _entryPath;
    private readonly string _name;
    private readonly string _exec;

    public AutostartFolderStartup() : this(ConfigHome(), Wayland.WaylandPlatform.AppId,
        DesktopEntry.NameOf(Assembly.GetEntryAssembly()),
        DesktopEntry.ExecOf(Assembly.GetEntryAssembly(), ISystemStartup.Argument))
    {
    }

    /// <summary>The entry <paramref name="appId"/> in the autostart folder under <paramref name="configHome"/>, named
    /// <paramref name="name"/>, running <paramref name="exec"/>.</summary>
    internal AutostartFolderStartup(string configHome, string appId, string name, string exec)
    {
        _entryPath = Path.Combine(configHome, AutostartFolder, appId + DesktopExtension);
        _name = name;
        _exec = exec;
    }

    public bool IsOn
    {
        get => IsListed();
        set
        {
            if (value)
                PutIn();
            else
                TakeOut();
        }
    }

    // Our entry, not turned off by the desktop.
    private bool IsListed()
    {
        if (!File.Exists(_entryPath))
        {
            return false;
        }

        var values = DesktopEntry.Values(_entryPath);
        return values.GetValueOrDefault(ExecKey) == _exec && values.GetValueOrDefault(HiddenKey) != True &&
               values.GetValueOrDefault(GnomeEnabledKey) != False;
    }

    private void PutIn()
    {
        var text = new StringBuilder()
            .Append("[Desktop Entry]\n")
            .Append("Type=Application\n")
            .Append("Name=").Append(DesktopEntry.Escape(_name)).Append('\n')
            .Append(ExecKey).Append('=').Append(_exec).Append('\n')
            .Append(GnomeEnabledKey).Append('=').Append(True).Append('\n')
            .ToString();
        DesktopEntry.WriteIfChanged(_entryPath, Encoding.UTF8.GetBytes(text));
    }

    private void TakeOut()
    {
        if (File.Exists(_entryPath))
        {
            File.Delete(_entryPath);
        }
    }

    private static string ConfigHome() =>
        Environment.GetEnvironmentVariable(ConfigHomeVariable) is { Length: > 0 } set && Path.IsPathRooted(set)
            ? set
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ConfigFolder);
}
