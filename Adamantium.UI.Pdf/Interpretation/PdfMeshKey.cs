using Adamantium.Pdf.Shadings;

namespace Adamantium.UI.Pdf.Interpretation;

/// <summary>A mesh shading at the scale it is shown at: what one tessellation of it is kept for.</summary>
internal readonly record struct PdfMeshKey(PdfMeshShading Shading, double Scale);
