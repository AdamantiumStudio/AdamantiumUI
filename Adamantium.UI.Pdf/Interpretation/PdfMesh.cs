using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>A mesh shading as drawn: the colored mesh, cut to its outline, whose first <see cref="BorderVertexCount"/>
/// vertices are the border reaching past that outline under the rest.</summary>
internal sealed class PdfMesh(ColoredMesh mesh, int borderVertexCount)
{
    public ColoredMesh Mesh { get; } = mesh;

    public int BorderVertexCount { get; } = borderVertexCount;
}
