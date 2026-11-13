using Adamantium.Graphics.Core;
using Adamantium.UI.Core.Media;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering.Retained;

/// <summary>A run of one geometry's backdrop material instances, all reading one source: the frame or the desktop
/// (<paramref name="Wallpaper"/>), through one <paramref name="Treatment"/>, captured from <paramref name="Region"/>, or a
/// picture of the author's own (<paramref name="Source"/>) pinned to <paramref name="Anchor"/>.</summary>
internal readonly record struct MaterialRun(KeySegment Segment, uint First, uint Count, bool Wallpaper,
    MaterialTreatment Treatment, Rect2D Region, ITexture Source, MaterialAnchor Anchor);
