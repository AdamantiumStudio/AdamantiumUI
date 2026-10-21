namespace Adamantium.UI.Platforms.Wayland;

/// <summary>A monitor recorded in a GNOME remote desktop session: its stream, and its size in the stream's pixels.</summary>
internal sealed record GnomeMonitorStream(string Connector, string Stream, int Width, int Height);
