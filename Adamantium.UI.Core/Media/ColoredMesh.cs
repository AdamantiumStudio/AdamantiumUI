using System;
using System.Collections.Generic;

namespace Adamantium.UI.Core.Media;

/// <summary>Triangles with a color at each corner, each triangle's color running smoothly between its three: a mesh
/// gradient as a PDF shading of free-form, lattice or patch meshes gives it (<see cref="Graphics.IDrawingSession.DrawColoredMesh"/>).
/// Every three vertices are a triangle. Immutable, so a page drawn again draws the same mesh.</summary>
public sealed class ColoredMesh
{
    public ColoredMesh(IReadOnlyList<ColoredVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        var count = vertices.Count - vertices.Count % 3;
        var copy = new ColoredVertex[count];
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        for (var i = 0; i < count; i++)
        {
            copy[i] = vertices[i];
            var p = copy[i].Position;
            (left, right) = (Math.Min(left, p.X), Math.Max(right, p.X));
            (top, bottom) = (Math.Min(top, p.Y), Math.Max(bottom, p.Y));
        }

        Vertices = copy;
        Bounds = count == 0 ? Rect.Empty : new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>The corners, three a triangle.</summary>
    public IReadOnlyList<ColoredVertex> Vertices { get; }

    /// <summary>Where the triangles lie in the mesh's own space.</summary>
    public Rect Bounds { get; }
}
