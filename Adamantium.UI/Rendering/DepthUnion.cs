using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

/// <summary>Draws something whose pieces overlap so that each pixel is laid down once, by its nearest piece: the shared
/// depth in a rectangle is scratch - <see cref="Settle"/> clears it and draws depth alone, <see cref="Lay"/> draws the
/// colour where a piece is the nearest, <see cref="Finish"/> puts the state back. Both draws must give the same depth.
/// </summary>
internal static class DepthUnion
{
    /// <summary>Every colour channel written.</summary>
    public const ColorComponentFlagBits AllColorComponents = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit |
                                                             ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;

    /// <summary>Clears the depth in <paramref name="scratch"/> - the stencil stays - and sets the depth-only draw.</summary>
    public static void Settle(IGraphicsDevice device, Rect2D scratch)
    {
        device.ClearDepthInRect(scratch);
        device.ColorComponentFlags = 0;
        device.DepthCompareFunction = CompareOp.Less;
        device.DepthWriteEnable = true;
    }

    /// <summary>Sets the colour draw: only where the depth settled is this fragment's.</summary>
    public static void Lay(IGraphicsDevice device)
    {
        device.ColorComponentFlags = AllColorComponents;
        device.DepthCompareFunction = CompareOp.Equal;
        device.DepthWriteEnable = false;
    }

    /// <summary>Puts back the depth state every other draw expects.</summary>
    public static void Finish(IGraphicsDevice device)
    {
        device.DepthCompareFunction = CompareOp.Always;
        device.DepthWriteEnable = true;
    }
}
