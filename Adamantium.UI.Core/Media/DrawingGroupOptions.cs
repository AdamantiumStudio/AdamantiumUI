using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media.Drawings;

namespace Adamantium.UI.Core.Media;

/// <summary>What a group of draws is put down with (<see cref="Graphics.IDrawingSession.PushGroup"/>): cut to a path and
/// to glyphs, let through a soft mask, mixed with what lies under it by a blend mode, faded as a whole. Everything left
/// at its default leaves the draws as they would be without the group.
/// <para>The group is drawn apart, starting from nothing (an isolated group in PDF's terms), and then laid down.</para>
/// </summary>
public sealed class DrawingGroupOptions
{
    /// <summary>The path the group is cut to, by its own fill rule; null cuts nothing.</summary>
    public Geometry Clip { get; init; }

    /// <summary>Where <see cref="Clip"/> lies in the drawing's units.</summary>
    public Matrix4x4F ClipTransform { get; init; } = Matrix4x4F.Identity;

    /// <summary>The placement <see cref="Clip"/> is given in and moves with, as anything drawn with it does; null for the
    /// drawing's own units. Set with it, <see cref="ClipTransform"/> stays the identity - another one is an error. It
    /// places <see cref="Clip"/> alone, not <see cref="TextClip"/>.</summary>
    public Placement ClipPlacement { get; init; }

    /// <summary>The glyphs the group is cut to - all of them together; null or empty cuts nothing.</summary>
    public IReadOnlyList<PlacedGlyphRun> TextClip { get; init; }

    /// <summary>The soft mask's picture; null for none. Read by <see cref="MaskKind"/>.</summary>
    public Drawing Mask { get; init; }

    /// <summary>Where <see cref="Mask"/> is drawn in the drawing's units.</summary>
    public Matrix4x4F MaskTransform { get; init; } = Matrix4x4F.Identity;

    public MaskKind MaskKind { get; init; }

    /// <summary>What a <see cref="MaskKind.Luminosity"/> mask is drawn over: its luminosity holds wherever the picture
    /// draws nothing.</summary>
    public Color MaskBackdrop { get; init; } = Colors.Black;

    public BlendMode BlendMode { get; init; }

    /// <summary>How opaque the group is laid down, 0 to 1.</summary>
    public double Opacity { get; init; } = 1;
}
