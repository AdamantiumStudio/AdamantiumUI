using Adamantium.UI.Core;

namespace Adamantium.UI.Universes;

/// <summary>Where a universe's surface stands in its window - the window, its world transform, and the surface's with its
/// inverse - read on the surface's own thread for the universe's thread to map the pointer by.</summary>
internal sealed record SurfacePlacement(IRootVisualComponent Root, Matrix4x4F RootWorld, Matrix4x4F SurfaceWorld,
    Matrix4x4F WorldToSurface);
