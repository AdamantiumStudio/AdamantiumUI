using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

internal readonly record struct OpenOffscreenLayer(
    IRenderTarget Target,
    IDepthStencilBuffer Depth,
    NativePoint Origin,
    Color Clear,
    MSAALevel Samples,
    IRenderTarget Color,
    uint TextureWidth,
    uint TextureHeight,
    Rect2D Region,
    CachedLayer Entry,
    int Slot,
    ulong Print,
    bool Reusable,
    bool Reused);
