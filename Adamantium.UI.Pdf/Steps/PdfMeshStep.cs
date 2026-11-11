using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Steps;

/// <summary>A mesh shading's triangles, each corner its color.</summary>
internal sealed class PdfMeshStep(ColoredMesh mesh, Matrix4x4F matrix) : PdfDrawStep
{
    public ColoredMesh Mesh { get; } = mesh;

    public Matrix4x4F Matrix { get; } = matrix;

    public override void Draw(IDrawingSession session, Matrix4x4F transform) =>
        session.DrawColoredMesh(Mesh, Matrix * transform);
}
