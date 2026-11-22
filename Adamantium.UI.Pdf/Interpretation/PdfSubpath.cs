using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>One subpath of the current path: where it starts, its points - a line to each point that is not a control
/// point, a cubic curve through each pair of control points and the point after them - and whether <c>h</c> closed it.
/// Begun again for the next path, its lists keep their room.</summary>
internal sealed class PdfSubpath
{
    public Vector2<double> Start { get; private set; }

    /// <summary>The points after the start, three for each curve.</summary>
    public List<Vector2<double>> Points { get; } = [];

    /// <summary>Whether each point ends a line (false) or is part of a curve (true), parallel to
    /// <see cref="Points"/>.</summary>
    public List<bool> OnCurve { get; } = [];

    public bool Closed { get; set; }

    /// <summary>Starts it over at <paramref name="start"/>, with no points, open.</summary>
    public PdfSubpath Begin(Vector2<double> start)
    {
        Start = start;
        Points.Clear();
        OnCurve.Clear();
        Closed = false;
        return this;
    }
}
