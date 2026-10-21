using Adamantium.Mathematics;

namespace Adamantium.UI.Platforms.Wayland;

/// <summary>Where the pointer was at the seat's last pointer event: the window it was over (null after it left ours),
/// the logical point of it, and the event's number.</summary>
internal sealed record WaylandPointerSnapshot(WaylandWindowWorker Window, Vector2 Position, long Sequence);
