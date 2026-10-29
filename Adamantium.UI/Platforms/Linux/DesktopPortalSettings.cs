using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Adamantium.DBus;
using Adamantium.UI.Core.Resources;

namespace Adamantium.UI.Platforms.Linux;

/// <summary>The desktop's own settings through the Settings portal: its light or dark appearance, followed as it
/// changes, and its interface and monospace fonts.</summary>
internal sealed class DesktopPortalSettings : IDisposable
{
    private const string Portal = "org.freedesktop.portal.Desktop";
    private const string PortalPath = "/org/freedesktop/portal/desktop";
    private const string Settings = "org.freedesktop.portal.Settings";
    private const string Appearance = "org.freedesktop.appearance";
    private const string ColorScheme = "color-scheme";
    private const string GnomeInterface = "org.gnome.desktop.interface";
    private const string FontName = "font-name";
    private const string MonospaceFontName = "monospace-font-name";

    private static readonly string[] StyleWords =
    [
        "normal", "roman", "oblique", "italic", "small-caps", "thin", "ultra-light", "extra-light", "light", "semi-light",
        "demi-light", "book", "regular", "medium", "semi-bold", "demi-bold", "bold", "ultra-bold", "extra-bold", "heavy",
        "black", "ultra-heavy", "ultra-condensed", "extra-condensed", "condensed", "semi-condensed", "semi-expanded",
        "expanded", "extra-expanded", "ultra-expanded",
    ];

    private readonly DBusConnection bus;
    private readonly IDisposable changes;

    private DesktopPortalSettings(DBusConnection bus, bool? prefersDark, string fontFamily, string monospaceFontFamily)
    {
        this.bus = bus;
        PrefersDark = prefersDark;
        FontFamily = fontFamily;
        MonospaceFontFamily = monospaceFontFamily;
        changes = bus.Subscribe(Portal, PortalPath, Settings, "SettingChanged", OnSettingChanged);
    }

    /// <summary>Whether the desktop asked for a dark appearance when opened; null where it does not say.</summary>
    public bool? PrefersDark { get; }

    /// <summary>The family of the desktop's interface font when opened; null where it does not say.</summary>
    public string FontFamily { get; }

    /// <summary>The family of the desktop's monospace font when opened; null where it does not say.</summary>
    public string MonospaceFontFamily { get; }

    /// <summary>Connects to the portal, reads its settings and follows them as they change; null where there is no
    /// session bus or no portal.</summary>
    public static DesktopPortalSettings Open()
    {
        DBusConnection bus = null;
        try
        {
            bus = DBusConnection.ConnectSession();
            var prefersDark = Read(bus, Appearance, ColorScheme) is uint scheme ? scheme == 1 : (bool?)null;
            var fontFamily = Read(bus, GnomeInterface, FontName) is string font ? FamilyOf(font) : null;
            var monospaceFontFamily = Read(bus, GnomeInterface, MonospaceFontName) is string monospace
                ? FamilyOf(monospace)
                : null;
            return new DesktopPortalSettings(bus, prefersDark, fontFamily, monospaceFontFamily);
        }
        catch (Exception e) when (e is DBusException or SocketException or IOException or InvalidCastException
                                      or OperationCanceledException)
        {
            bus?.Dispose();
            return null;
        }
    }

    /// <summary>The family of a Pango font description: "Adwaita Sans Bold 11" is Adwaita Sans.</summary>
    public static string FamilyOf(string description)
    {
        var words = description.Trim().TrimEnd(',').Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && double.TryParse(words[^1].Replace("px", string.Empty), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            words.RemoveAt(words.Count - 1);
        }

        while (words.Count > 1 && StyleWords.Contains(words[^1].ToLowerInvariant()))
        {
            words.RemoveAt(words.Count - 1);
        }

        var family = string.Join(' ', words).TrimEnd(',');
        return family.Length > 0 ? family : null;
    }

    public void Dispose()
    {
        changes.Dispose();
        bus.Dispose();
    }

    private static object Read(DBusConnection bus, string group, string key)
    {
        try
        {
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            return bus.CallAsync(DBusMessage.MethodCall(Portal, PortalPath, Settings, "ReadOne", "ss", writer =>
            {
                writer.WriteString(group);
                writer.WriteString(key);
            }), patience.Token).GetAwaiter().GetResult().Reader().ReadVariant().Value;
        }
        catch (DBusException)
        {
            return null;
        }
    }

    private void OnSettingChanged(DBusMessage message)
    {
        var reader = message.Reader();
        var group = reader.ReadString();
        var key = reader.ReadString();
        var value = reader.ReadVariant().Value;
        if (group == Appearance && key == ColorScheme && value is uint scheme)
        {
            Threading.Dispatcher.CurrentDispatcher?.InvokeAsync(() => SystemAppearance.PrefersDark = scheme == 1);
        }
    }
}
