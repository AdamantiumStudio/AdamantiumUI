using Adamantium.Mathematics;

namespace Adamantium.UI.Core.Graphics.Vector;

/// <summary>Where a vector writer - a PDF page, an XPS page - takes a path: figures of lines and Bézier curves, in the
/// units it is being drawn in. The shapes of <see cref="VectorShapes"/> and the runs of <see cref="StrokeRuns"/> are
/// written through it, so every writer draws them alike.</summary>
internal interface IVectorPath
{
    void MoveTo(Vector2<double> point);

    void LineTo(Vector2<double> point);

    void QuadraticTo(Vector2<double> control, Vector2<double> end);

    void CubicTo(Vector2<double> first, Vector2<double> second, Vector2<double> end);

    /// <summary>Closes the figure begun by the last <see cref="MoveTo"/>.</summary>
    void Close();
}
