using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Rendering.Payloads;

/// <summary>A mark in a group of draws (see <see cref="GroupMarkKind"/>): what the walk opens or lays down there.</summary>
public sealed class GroupMarkPayload
{
    private GroupMarkPayload(GroupMarkKind kind)
    {
        Kind = kind;
    }

    public GroupMarkKind Kind { get; }

    /// <summary><see cref="GroupMarkKind.Begin"/>: how the group is laid down.</summary>
    public DrawingGroupOptions Options { get; private init; }

    /// <summary><see cref="GroupMarkKind.Begin"/>: the clip as a rectangle in the element's units, when the group does
    /// nothing but cut to one lying along the axes - a scissor can cut it then; null otherwise.</summary>
    public Rect? ClipRect { get; private init; }

    /// <summary><see cref="GroupMarkKind.MaskBegin"/>: what of the mask's picture lets the group through.</summary>
    public MaskKind MaskKind { get; private init; }

    /// <summary><see cref="GroupMarkKind.MaskBegin"/>: what a luminosity mask is drawn over.</summary>
    public Color MaskBackdrop { get; private init; }

    public static GroupMarkPayload Begin(DrawingGroupOptions options, Rect? clipRect) =>
        new(GroupMarkKind.Begin) { Options = options, ClipRect = clipRect };

    public static GroupMarkPayload MaskBegin(MaskKind kind, Color backdrop) =>
        new(GroupMarkKind.MaskBegin) { MaskKind = kind, MaskBackdrop = backdrop };

    public static readonly GroupMarkPayload MaskEnd = new(GroupMarkKind.MaskEnd);

    public static readonly GroupMarkPayload ContentBegin = new(GroupMarkKind.ContentBegin);

    public static readonly GroupMarkPayload End = new(GroupMarkKind.End);
}
