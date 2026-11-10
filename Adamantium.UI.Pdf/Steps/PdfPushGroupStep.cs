using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;

namespace Adamantium.UI.Pdf.Steps;

/// <summary>The start of a group: what cuts it, masks it and how it is laid down, in the page's units. The options are
/// made anew on each replay, the page's own placement folded into their matrices.</summary>
internal sealed class PdfPushGroupStep : PdfDrawStep
{
    public Geometry Clip { get; init; }

    public Matrix4x4F ClipMatrix { get; init; } = Matrix4x4F.Identity;

    public IReadOnlyList<PlacedGlyphRun> TextClip { get; init; }

    /// <summary>The soft mask's picture, drawn in the page's units.</summary>
    public Drawing Mask { get; init; }

    public MaskKind MaskKind { get; init; }

    public Color MaskBackdrop { get; init; } = Colors.Black;

    public BlendMode BlendMode { get; init; }

    public double Opacity { get; init; } = 1;

    public override void Draw(IDrawingSession session, Matrix4x4F transform)
    {
        List<PlacedGlyphRun> glyphs = null;
        if (TextClip is { Count: > 0 })
        {
            glyphs = new List<PlacedGlyphRun>(TextClip.Count);
            foreach (var run in TextClip)
            {
                glyphs.Add(new PlacedGlyphRun(run.Run, run.Transform * transform));
            }
        }

        session.PushGroup(new DrawingGroupOptions
        {
            Clip = Clip,
            ClipTransform = ClipMatrix * transform,
            TextClip = glyphs,
            Mask = Mask,
            MaskTransform = transform,
            MaskKind = MaskKind,
            MaskBackdrop = MaskBackdrop,
            BlendMode = BlendMode,
            Opacity = Opacity,
        });
    }
}
