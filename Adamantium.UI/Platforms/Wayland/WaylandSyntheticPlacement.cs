using Adamantium.Mathematics;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>Where a window stands in a monitor's stream: the stream point of its origin, and logical pixels per stream
/// pixel.</summary>
internal sealed record WaylandSyntheticPlacement(WaylandWindowWorker Worker, GnomeMonitorStream Monitor, Vector2 Origin, double Scale)
{
    public Vector2 ToStream(Vector2 local) => new(Origin.X + local.X / Scale, Origin.Y + local.Y / Scale);
}
