using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Shadings;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>What fills or strokes paint with (ISO 32000-2, 8.6.8): a color in a space, or a pattern - with the color of
/// an uncolored one's stencil.</summary>
internal sealed class PdfPaint
{
    public PdfPaint(PdfColorSpace space, double[] components, PdfPattern pattern = null)
    {
        Space = space;
        Components = components;
        Pattern = pattern;
    }

    public PdfColorSpace Space { get; }

    /// <summary>The color's components in <see cref="Space"/> - for a pattern, in its underlying space.</summary>
    public double[] Components { get; }

    public PdfPattern Pattern { get; }

    /// <summary>The matrix in force when the stream that names <see cref="Pattern"/> began: where pattern space starts,
    /// before the pattern's own matrix.</summary>
    public PdfMatrix PatternBase { get; init; } = PdfMatrix.Identity;

    /// <summary>A tiling pattern's cell as drawn - in this paint's color, for an uncolored one - once its stream has
    /// been walked; null until then, or when it could not be.</summary>
    public PdfContentDrawing Cell { get; set; }

    public static PdfPaint Initial(PdfColorSpace space)
    {
        var components = new double[space.Components];
        space.InitialColor(components);
        return new PdfPaint(space, components);
    }

    /// <summary>The color in sRGB, 0 to 1 each: the components through the space, or for a pattern through its
    /// underlying space.</summary>
    public (double R, double G, double B) Rgb()
    {
        var space = Space is PdfPatternColorSpace pattern ? pattern.Underlying : Space;
        if (space == null)
        {
            return (0, 0, 0);
        }

        System.Span<double> rgb = stackalloc double[3];
        System.Span<double> components = stackalloc double[System.Math.Max(space.Components, 1)];
        for (var i = 0; i < components.Length && i < Components.Length; i++)
        {
            components[i] = Components[i];
        }

        space.ToRgb(components, rgb);
        return (rgb[0], rgb[1], rgb[2]);
    }
}
