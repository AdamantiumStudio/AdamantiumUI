using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf;

/// <summary>A path a page draws - its outline and where it stands - as a measurement snaps to its ends, middles and
/// crossings.</summary>
public sealed class PdfDrawnPath
{
    internal PdfDrawnPath(Geometry geometry, Matrix4x4F matrix, bool isStroked, bool isFilled)
    {
        Geometry = geometry;
        Matrix = matrix;
        IsStroked = isStroked;
        IsFilled = isFilled;
    }

    /// <summary>Its outline, in its own units; its <see cref="Geometry.Bounds"/> are worked out once it is processed -
    /// <see cref="PdfPageDrawing.PrepareForDrawing"/> does, as does drawing it.</summary>
    public Geometry Geometry { get; }

    /// <summary>From its own units to the drawing's: the page's points, y down, from the crop box's top left.</summary>
    public Matrix4x4F Matrix { get; }

    /// <summary>Whether it is drawn as a line.</summary>
    public bool IsStroked { get; }

    /// <summary>Whether it is filled.</summary>
    public bool IsFilled { get; }
}
