using System.Collections.Generic;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering.RenderUnits;
using Adamantium.Vulkan.Core;

namespace Adamantium.UI.Rendering;

/// <summary>A group of draws as the walk put it down: decided when its begin mark is walked, held on its marks for the
/// frames that replay the walk. One for the life of its begin mark, so the targets it draws into are kept from frame to
/// frame.</summary>
internal sealed class CommandGroup(GroupMarkRenderUnit begin)
{
    /// <summary>The most masks a group has: its path, its glyphs and its soft mask.</summary>
    public const int MostMasks = 3;

    public GroupMarkRenderUnit Begin { get; } = begin;

    public DrawingGroupOptions Options => Begin.Payload.Options;

    public CommandGroupMode Mode;

    /// <summary><see cref="CommandGroupMode.Layer"/>: the window region its targets hold, in device pixels.</summary>
    public Rect2D Region;

    /// <summary><see cref="CommandGroupMode.Scissor"/>: the scissor its draws are cut by, in device pixels.</summary>
    public Rect2D Scissor;

    /// <summary>The scissor its draws were cut by before it, for its end to go back to; null for none.</summary>
    public Rect2D OuterScissor;

    /// <summary>How deep among the groups open in the walk it stands: which copy of what lies under it is its own.</summary>
    public int Depth;

    /// <summary>Its masks drawn so far this frame.</summary>
    public readonly List<(GroupTarget Target, bool Luminosity)> Masks = new(MostMasks);

    /// <summary>Whether one of its masks' targets is open.</summary>
    public bool MaskOpen;

    /// <summary>Whether the mask being drawn is read by its luminosity.</summary>
    public bool MaskByLuminosity;

    /// <summary>Whether what lies under it was copied for its blend mode this frame.</summary>
    public bool BackdropTaken;

    /// <summary>The keys its targets are kept under from frame to frame: its content's, then its masks'.</summary>
    public readonly object ContentKey = new();

    public readonly object[] MaskKeys = [new(), new(), new()];
}
