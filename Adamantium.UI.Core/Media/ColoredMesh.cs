using System;
using System.Collections.Generic;

namespace Adamantium.UI.Core.Media;

/// <summary>Triangles with a color at each corner, each triangle's color running smoothly between its three: a mesh
/// gradient as a PDF shading of free-form, lattice or patch meshes gives it (<see cref="Graphics.IDrawingSession.DrawColoredMesh"/>).
/// Every three vertices are a triangle. Immutable, so a page drawn again draws the same mesh.</summary>
public sealed class ColoredMesh
{
    public ColoredMesh(IReadOnlyList<ColoredVertex> vertices) : this(vertices, null)
    {
    }

    /// <summary>A mesh cut to <paramref name="outline"/>: the triangles give the color, the outline the edge - exact and
    /// smooth at any zoom, as a filled geometry's. The triangles should reach a little past the outline.</summary>
    public ColoredMesh(IReadOnlyList<ColoredVertex> vertices, Geometry outline)
    {
        Outline = outline;
        ArgumentNullException.ThrowIfNull(vertices);
        var count = vertices.Count - vertices.Count % 3;
        var copy = new ColoredVertex[count];
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        for (var index = 0; index < count; index++)
        {
            copy[index] = vertices[index];
            var position = copy[index].Position;
            left = Math.Min(left, position.X);
            right = Math.Max(right, position.X);
            top = Math.Min(top, position.Y);
            bottom = Math.Max(bottom, position.Y);
        }

        Vertices = copy;
        Bounds = count == 0 ? Rect.Empty : new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>The corners, three a triangle.</summary>
    public IReadOnlyList<ColoredVertex> Vertices { get; }

    /// <summary>Where the triangles lie in the mesh's own space.</summary>
    public Rect Bounds { get; }

    /// <summary>The edge the mesh is cut to, in its own space; null leaves the triangles' own edges.</summary>
    public Geometry Outline { get; }
}
