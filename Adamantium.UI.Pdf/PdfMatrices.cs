using Adamantium.Mathematics;
using Adamantium.Pdf.Document;

namespace Adamantium.UI.Pdf;

/// <summary>PDF matrices as the drawing's: both put a row vector on the left, so <c>a.Then(b)</c> is <c>a * b</c>.</summary>
internal static class PdfMatrices
{
    public static Matrix4x4F ToDrawing(PdfMatrix m)
    {
        var matrix = Matrix4x4F.Identity;
        matrix.M11 = (float)m.A;
        matrix.M12 = (float)m.B;
        matrix.M21 = (float)m.C;
        matrix.M22 = (float)m.D;
        matrix.M41 = (float)m.E;
        matrix.M42 = (float)m.F;
        return matrix;
    }

    /// <summary>How much the matrix scales lengths, as the square root of its area scale.</summary>
    public static double Scale(PdfMatrix m) => System.Math.Sqrt(System.Math.Abs(m.A * m.D - m.B * m.C));

    /// <summary>Whether the matrix keeps the axes: no turn and no shear, a flip at most.</summary>
    public static bool KeepsAxes(PdfMatrix m) => (m.B == 0 && m.C == 0) || (m.A == 0 && m.D == 0);
}
